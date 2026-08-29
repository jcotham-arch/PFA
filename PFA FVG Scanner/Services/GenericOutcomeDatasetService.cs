using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;

namespace PFA_FVG_Scanner.Services;

public sealed class GenericOutcomeDatasetService(PfaDatabase database)
{
    public const string Version = "generic-outcome-dataset-1.6.0";

    public async Task<GenericOutcomeDatasetManifest> BuildAsync(GenericOutcomeDatasetRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var asOf = Utc(request.AsOfUtc);
        if (asOf == default) throw new ArgumentException("A non-default AsOfUtc is required.");
        if (request.TargetHorizonMinutes is not (5 or 15 or 60))
            throw new ArgumentException("Target horizon must be 5, 15, or 60 minutes.");
        var instruments = (request.InstrumentIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Order().ToArray();
        var candidates = await ReadCandidatesAsync(asOf, request.TargetHorizonMinutes, instruments, token);
        if (candidates.Count < 3)
            throw new InvalidOperationException("At least three point-in-time labeled examples are required.");

        var examples = candidates.GroupBy(x => x.InstrumentId, StringComparer.Ordinal)
            .SelectMany(group => AssignSplits(group.OrderBy(x => x.EventTimeUtc).ThenBy(x => x.ExampleId).ToArray()))
            .OrderBy(x => x.EventTimeUtc).ThenBy(x => x.InstrumentId).ThenBy(x => x.ExampleId).ToArray();
        examples = examples.Select(x => x with { ContentHash = HashExample(x) }).ToArray();
        var dataRevision = AgentTrainingDatasetBuilder.Hash(string.Join('|', examples.Select(x => x.SourceRevision)));
        var datasetSeed = JsonSerializer.Serialize(new
        {
            Version, asOf, request.TargetHorizonMinutes, instruments,
            Examples = examples.Select(x => new { x.ExampleId, x.ContentHash })
        });
        var contentHash = AgentTrainingDatasetBuilder.Hash(datasetSeed);
        var datasetId = $"AGDS-{contentHash[..32]}";
        var manifest = new GenericOutcomeDatasetManifest(datasetId, Version, dataRevision, asOf,
            request.TargetHorizonMinutes, examples.Length, examples.Count(x => x.Split == "Train"),
            examples.Count(x => x.Split == "Validation"), examples.Count(x => x.Split == "Test"),
            examples[0].EventTimeUtc, examples[^1].EventTimeUtc,
            examples.Select(x => x.InstrumentId).Distinct().Order().ToArray(),
            examples.Select(x => x.ModuleId).Distinct().Order().ToArray(),
            examples.SelectMany(x => x.NumericFeatures.Keys).Distinct().Order().ToArray(),
            examples.SelectMany(x => x.Labels.Keys).Distinct().Order().ToArray(), contentHash);
        await PersistAsync(manifest, examples, token);
        return manifest;
    }

    public async Task<IReadOnlyList<GenericOutcomeDatasetManifest>> GetAllAsync(CancellationToken token = default)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ManifestJson FROM AgentResearchDatasets ORDER BY CreatedAtUtc DESC";
        var values = new List<GenericOutcomeDatasetManifest>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
            values.Add(JsonSerializer.Deserialize<GenericOutcomeDatasetManifest>(reader.GetString(0))!);
        return values;
    }

    private async Task<List<GenericOutcomeResearchExample>> ReadCandidatesAsync(DateTime asOf, int horizon,
        IReadOnlyList<string> instruments, CancellationToken token)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        var filter = instruments.Count == 0 ? "" :
            $" AND o.InstrumentId IN ({string.Join(',', instruments.Select((_, index) => $"$instrument{index}"))})";
        command.CommandText = $"""
            SELECT o.ObservationId,o.Revision,o.ModuleId,o.ModuleVersion,o.PatternType,o.InstrumentId,
                   o.ContractId,o.Timeframe,o.Direction,o.FormationTimeUtc,o.KnownAtUtc,o.PayloadJson,o.ContentHash,
                   u.OutcomeId,u.OutcomeVersion,u.EvaluatedThroughUtc,
                   close.Value,mfe.Value,mae.Value,
                   (SELECT json_object('close',b.Close,'high',b.High,'low',b.Low,'volume',b.Volume)
                    FROM CanonicalResolvedResearchBars b
                    WHERE b.InstrumentId=o.InstrumentId AND b.Timeframe='1m' AND b.CloseTimeUtc<=o.KnownAtUtc
                    ORDER BY b.CloseTimeUtc DESC LIMIT 1) latestBar,
                   (SELECT b.Close FROM CanonicalResolvedResearchBars b
                    WHERE b.InstrumentId=o.InstrumentId AND b.Timeframe='1m' AND b.CloseTimeUtc<=o.KnownAtUtc
                    ORDER BY b.CloseTimeUtc DESC LIMIT 1 OFFSET 5) priorClose,
                   (SELECT json_object(
                        'barCount',COUNT(*),'meanRange5',AVG(CASE WHEN x.rn<=5 THEN x.barRange END),'meanRange20',AVG(x.barRange),
                        'meanVolume5',AVG(CASE WHEN x.rn<=5 THEN x.volume END),'meanVolume20',AVG(x.volume),
                        'meanBody20',AVG(x.body),'high20',MAX(x.high),'low20',MIN(x.low))
                    FROM (SELECT CAST(b.High AS REAL)-CAST(b.Low AS REAL) barRange,
                                 CAST(b.Volume AS REAL) volume,
                                 ABS(CAST(b.Close AS REAL)-CAST(b.Open AS REAL)) body,
                                 CAST(b.High AS REAL) high,CAST(b.Low AS REAL) low,
                                 ROW_NUMBER() OVER (ORDER BY b.CloseTimeUtc DESC) rn
                          FROM CanonicalResolvedResearchBars b
                          WHERE b.InstrumentId=o.InstrumentId AND b.Timeframe='1m' AND b.CloseTimeUtc<=o.KnownAtUtc
                          ORDER BY b.CloseTimeUtc DESC LIMIT 20) x) context20,
                   (SELECT f.SnapshotJson FROM OrderFlowFeatureSnapshots f WHERE f.InstrumentId=o.InstrumentId
                       AND f.WindowEndUtc<=o.KnownAtUtc AND f.KnownAtUtc<=o.KnownAtUtc
                       AND julianday(f.WindowEndUtc)>=julianday(o.KnownAtUtc)-(5.0/1440.0)
                       AND (f.ContractId IS NULL OR f.ContractId=o.ContractId)
                       ORDER BY f.WindowEndUtc DESC,f.KnownAtUtc DESC LIMIT 1) orderFlow
            FROM UniversalMarketObservations o
            JOIN UniversalMarketOutcomes u ON u.ObservationId=o.ObservationId
            JOIN UniversalOutcomeMetrics close ON close.OutcomeId=u.OutcomeId
                AND close.MetricName='directional-close-change' AND close.HorizonMinutes=$horizon AND close.Unit='ticks'
            JOIN UniversalOutcomeMetrics mfe ON mfe.OutcomeId=u.OutcomeId
                AND mfe.MetricName='maximum-favorable-excursion' AND mfe.HorizonMinutes=$horizon AND mfe.Unit='ticks'
            JOIN UniversalOutcomeMetrics mae ON mae.OutcomeId=u.OutcomeId
                AND mae.MetricName='maximum-adverse-excursion' AND mae.HorizonMinutes=$horizon AND mae.Unit='ticks'
            WHERE o.KnownAtUtc<u.EvaluatedThroughUtc AND u.EvaluatedThroughUtc<=$asOf {filter}
            ORDER BY o.FormationTimeUtc,o.ObservationId;
            """;
        command.Parameters.AddWithValue("$horizon", horizon);
        command.Parameters.AddWithValue("$asOf", asOf.ToString("O"));
        for (var index = 0; index < instruments.Count; index++)
            command.Parameters.AddWithValue($"$instrument{index}", instruments[index]);
        var values = new List<GenericOutcomeResearchExample>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var observationId = reader.GetString(0);
            var formation = Parse(reader.GetString(9));
            var known = Parse(reader.GetString(10));
            var outcomeKnown = Parse(reader.GetString(15));
            if (known > asOf || outcomeKnown > asOf || outcomeKnown <= known) continue;
            var features = ExtractNumericFeatures(reader.GetString(11));
            features["direction"] = string.Equals(reader.GetString(8), "Bullish", StringComparison.OrdinalIgnoreCase) ? 1m : -1m;
            features["timeframeMinutes"] = TimeframeMinutes(reader.GetString(7));
            features[$"context.instrument.{reader.GetString(5)}"] = 1m;
            features[$"context.module.{reader.GetString(2)}"] = 1m;
            features[$"context.pattern.{reader.GetString(4)}"] = 1m;
            var minuteOfDay=formation.Hour*60+formation.Minute;
            features["time.hourSin"]=(decimal)Math.Sin(2*Math.PI*minuteOfDay/1440d);
            features["time.hourCos"]=(decimal)Math.Cos(2*Math.PI*minuteOfDay/1440d);
            features["time.weekdaySin"]=(decimal)Math.Sin(2*Math.PI*(int)formation.DayOfWeek/7d);
            features["time.weekdayCos"]=(decimal)Math.Cos(2*Math.PI*(int)formation.DayOfWeek/7d);
            features["time.monthSin"]=(decimal)Math.Sin(2*Math.PI*(formation.Month-1)/12d);
            features["time.monthCos"]=(decimal)Math.Cos(2*Math.PI*(formation.Month-1)/12d);
            features[$"context.session.{SessionSegment(known.Hour)}"]=1m;
            features["context.session.progressUtcDay"]=minuteOfDay/1440m;
            PointInTimeContextFeatureEncoder.Add(features,reader.IsDBNull(19)?null:reader.GetString(19),
                reader.IsDBNull(20)?null:reader.GetString(20),reader.IsDBNull(21)?null:reader.GetString(21),
                reader.IsDBNull(22)?null:reader.GetString(22));
            var labels = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["directionalCloseTicks"] = Decimal(reader.GetString(16)),
                ["maximumFavorableExcursionTicks"] = Decimal(reader.GetString(17)),
                ["maximumAdverseExcursionTicks"] = Decimal(reader.GetString(18))
            };
            var sourceRevision = AgentTrainingDatasetBuilder.Hash(string.Join('|', observationId,
                reader.GetInt32(1), reader.GetString(12), reader.GetString(13), reader.GetString(14)));
            var exampleId = $"AGEX-{AgentTrainingDatasetBuilder.Hash($"{observationId}|{reader.GetString(13)}|{horizon}")[..32]}";
            values.Add(new(exampleId, observationId, reader.GetString(13), reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetString(7), reader.GetString(2),
                reader.GetString(4), reader.GetString(8), formation, known, known, outcomeKnown, "Unassigned",
                features, labels, sourceRevision, ""));
        }
        return values;
    }

    private async Task PersistAsync(GenericOutcomeDatasetManifest manifest,
        IReadOnlyList<GenericOutcomeResearchExample> examples, CancellationToken token)
    {
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO AgentResearchDatasets
                (DatasetId,DatasetVersion,DataRevision,AsOfUtc,TargetHorizonMinutes,ExampleCount,TrainCount,
                 ValidationCount,TestCount,EarliestEventUtc,LatestEventUtc,ContentHash,ManifestJson,CreatedAtUtc,
                 CanActivateStrategy,CanRouteToRealBroker)
                VALUES($id,$version,$revision,$asOf,$horizon,$examples,$train,$validation,$test,$earliest,$latest,
                       $hash,$json,$created,0,0);
                """;
            Add(command, "$id", manifest.DatasetId); Add(command, "$version", manifest.DatasetVersion);
            Add(command, "$revision", manifest.DataRevision); Add(command, "$asOf", manifest.AsOfUtc.ToString("O"));
            Add(command, "$horizon", manifest.TargetHorizonMinutes); Add(command, "$examples", manifest.ExampleCount);
            Add(command, "$train", manifest.TrainCount); Add(command, "$validation", manifest.ValidationCount);
            Add(command, "$test", manifest.TestCount); Add(command, "$earliest", manifest.EarliestEventUtc.ToString("O"));
            Add(command, "$latest", manifest.LatestEventUtc.ToString("O")); Add(command, "$hash", manifest.ContentHash);
            Add(command, "$json", JsonSerializer.Serialize(manifest)); Add(command, "$created", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(token);
        }
        foreach (var example in examples)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO AgentResearchExamples
                (DatasetId,ExampleId,ObservationId,OutcomeId,InstrumentId,ContractId,Timeframe,ModuleId,PatternType,
                 Direction,EventTimeUtc,FeatureKnownAtUtc,DecisionTimeUtc,OutcomeKnownAtUtc,Split,FeatureJson,
                 LabelJson,SourceRevision,ContentHash)
                VALUES($dataset,$example,$observation,$outcome,$instrument,$contract,$timeframe,$module,$pattern,
                       $direction,$event,$featureKnown,$decision,$outcomeKnown,$split,$features,$labels,$revision,$hash);
                """;
            Add(command, "$dataset", manifest.DatasetId); Add(command, "$example", example.ExampleId);
            Add(command, "$observation", example.ObservationId); Add(command, "$outcome", example.OutcomeId);
            Add(command, "$instrument", example.InstrumentId); Add(command, "$contract", (object?)example.ContractId ?? DBNull.Value);
            Add(command, "$timeframe", example.Timeframe); Add(command, "$module", example.ModuleId);
            Add(command, "$pattern", example.PatternType); Add(command, "$direction", example.Direction);
            Add(command, "$event", example.EventTimeUtc.ToString("O")); Add(command, "$featureKnown", example.FeatureKnownAtUtc.ToString("O"));
            Add(command, "$decision", example.DecisionTimeUtc.ToString("O")); Add(command, "$outcomeKnown", example.OutcomeKnownAtUtc.ToString("O"));
            Add(command, "$split", example.Split); Add(command, "$features", JsonSerializer.Serialize(example.NumericFeatures));
            Add(command, "$labels", JsonSerializer.Serialize(example.Labels)); Add(command, "$revision", example.SourceRevision);
            Add(command, "$hash", example.ContentHash); await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

    private static Dictionary<string, decimal> ExtractNumericFeatures(string json)
    {
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            Walk(document.RootElement, "geometry", values);
        }
        catch (JsonException) { }
        return values;
    }

    private static IReadOnlyList<GenericOutcomeResearchExample> AssignSplits(
        IReadOnlyList<GenericOutcomeResearchExample> examples)
    {
        if (examples.Count < 3) return [];
        var trainEnd = Math.Max(1, (int)Math.Floor(examples.Count * 0.70m));
        var validationEnd = Math.Max(trainEnd + 1, (int)Math.Floor(examples.Count * 0.85m));
        validationEnd = Math.Min(validationEnd, examples.Count - 1);
        return examples.Select((value, index) => value with
        { Split = index < trainEnd ? "Train" : index < validationEnd ? "Validation" : "Test" }).ToArray();
    }

    private static void Walk(JsonElement element, string path, Dictionary<string, decimal> values)
    {
        if (values.Count >= 64) return;
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) Walk(property.Value, $"{path}.{property.Name}", values);
        else if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var value)) values[path] = value;
    }

    private static string SessionSegment(int hour)=>hour switch
    {<8=>"Overnight",<13=>"Premarket",<16=>"RegularMorning",<18=>"RegularMidday",<20=>"RegularAfternoon",_=>"PostMarket"};

    private static string HashExample(GenericOutcomeResearchExample example) => AgentTrainingDatasetBuilder.Hash(
        JsonSerializer.Serialize(new { example.ExampleId,example.ObservationId,example.OutcomeId,example.Split,
            example.FeatureKnownAtUtc,example.DecisionTimeUtc,example.OutcomeKnownAtUtc,
            example.NumericFeatures,example.Labels,example.SourceRevision }));
    private static int TimeframeMinutes(string timeframe) => timeframe.ToLowerInvariant() switch
    { "1m" => 1, "5m" => 5, "15m" => 15, "1h" => 60, _ => 0 };
    private static decimal Decimal(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
    private static DateTime Parse(string value) => DateTime.Parse(value, null, DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static DateTime Utc(DateTime value) => value.Kind switch
    { DateTimeKind.Utc => value, DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc), _ => value.ToUniversalTime() };
    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
}
