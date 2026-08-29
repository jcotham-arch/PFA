using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class ActionabilityOutcomeDatasetService(PfaDatabase database)
{
    public const string Version="actionability-outcome-dataset-1.2.0";

    public async Task<GenericOutcomeDatasetManifest> BuildAsync(ActionabilityOutcomeDatasetRequest request,
        CancellationToken token=default)
    {
        var asOf=Utc(request.AsOfUtc);if(asOf==default)throw new ArgumentException("A non-default AsOfUtc is required.");
        var instruments=Normalize(request.InstrumentIds,true);var modules=Normalize(request.ModuleIds,false);
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        var run=await LatestRun(connection,token)??throw new InvalidOperationException("No pattern trade research run exists.");
        var definitions=run.Summaries.GroupBy(x=>x.HypothesisId,StringComparer.Ordinal)
            .ToDictionary(x=>x.Key,x=>x.First(),StringComparer.Ordinal);
        var examples=await ReadExamples(connection,run,definitions,asOf,instruments,modules,token);
        if(examples.Count<3)throw new InvalidOperationException("At least three finalized point-in-time trade outcomes are required.");
        var ordered=examples.OrderBy(x=>x.EventTimeUtc).ThenBy(x=>x.ExampleId,StringComparer.Ordinal).ToArray();
        var revision=AgentTrainingDatasetBuilder.Hash(string.Join('|',ordered.Select(x=>x.SourceRevision)));
        var hash=AgentTrainingDatasetBuilder.Hash(JsonSerializer.Serialize(new{Version,asOf,run.RunId,revision,
            Examples=ordered.Select(x=>new{x.ExampleId,x.ContentHash})}));var id=$"ARDS-{hash[..32]}";
        var manifest=new GenericOutcomeDatasetManifest(id,Version,revision,asOf,0,ordered.Length,
            ordered.Count(x=>x.Split=="Train"),ordered.Count(x=>x.Split=="Validation"),ordered.Count(x=>x.Split=="Test"),
            ordered[0].EventTimeUtc,ordered[^1].EventTimeUtc,ordered.Select(x=>x.InstrumentId).Distinct().Order().ToArray(),
            ordered.Select(x=>x.ModuleId).Distinct().Order().ToArray(),ordered.SelectMany(x=>x.NumericFeatures.Keys).Distinct().Order().ToArray(),
            ordered.SelectMany(x=>x.Labels.Keys).Distinct().Order().ToArray(),hash);
        await Persist(connection,manifest,ordered,token);return manifest;
    }

    private static async Task<PatternTradeResearchRun?> LatestRun(SqliteConnection connection,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText="SELECT RunJson FROM PatternTradeResearchRuns ORDER BY CreatedAtUtc DESC LIMIT 1";var json=await command.ExecuteScalarAsync(token) as string;return json is null?null:JsonSerializer.Deserialize<PatternTradeResearchRun>(json);}

    private static async Task<List<GenericOutcomeResearchExample>> ReadExamples(SqliteConnection connection,
        PatternTradeResearchRun run,IReadOnlyDictionary<string,PatternTradeHypothesisSummary> definitions,DateTime asOf,
        string[] instruments,string[] modules,CancellationToken token)
    {
        var values=new List<GenericOutcomeResearchExample>();await using var command=connection.CreateCommand();command.CommandText="""
            SELECT s.SampleJson,o.FormationTimeUtc,o.KnownAtUtc,o.Timeframe,o.PayloadJson,o.ContentHash,
                   (SELECT json_object('close',b.Close,'high',b.High,'low',b.Low,'volume',b.Volume)
                    FROM CanonicalResolvedResearchBars b WHERE b.InstrumentId=o.InstrumentId AND b.Timeframe='1m'
                      AND b.CloseTimeUtc<=json_extract(s.SampleJson,'$.EntryTimeUtc') ORDER BY b.CloseTimeUtc DESC LIMIT 1) latestBar,
                   (SELECT b.Close FROM CanonicalResolvedResearchBars b WHERE b.InstrumentId=o.InstrumentId AND b.Timeframe='1m'
                      AND b.CloseTimeUtc<=json_extract(s.SampleJson,'$.EntryTimeUtc') ORDER BY b.CloseTimeUtc DESC LIMIT 1 OFFSET 5) priorClose,
                   (SELECT json_object('meanRange20',AVG(x.barRange),'meanVolume20',AVG(x.volume),'meanBody20',AVG(x.body),'high20',MAX(x.high),'low20',MIN(x.low))
                    FROM (SELECT CAST(b.High AS REAL)-CAST(b.Low AS REAL) barRange,CAST(b.Volume AS REAL) volume,
                                 ABS(CAST(b.Close AS REAL)-CAST(b.Open AS REAL)) body,CAST(b.High AS REAL) high,CAST(b.Low AS REAL) low
                          FROM CanonicalResolvedResearchBars b WHERE b.InstrumentId=o.InstrumentId AND b.Timeframe='1m'
                            AND b.CloseTimeUtc<=json_extract(s.SampleJson,'$.EntryTimeUtc') ORDER BY b.CloseTimeUtc DESC LIMIT 20) x) context20
            FROM PatternTradeResearchSamples s JOIN UniversalMarketObservations o ON o.ObservationId=s.ObservationId
            WHERE s.RunId=$run AND s.NetR IS NOT NULL ORDER BY o.FormationTimeUtc,s.SampleId;
            """;command.Parameters.AddWithValue("$run",run.RunId);await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))
        {
            var sample=JsonSerializer.Deserialize<PatternTradeHypothesisSample>(reader.GetString(0));
            if(sample is null||sample.ExitTimeUtc is null||sample.NetR is null||sample.EntryTimeUtc is null||
               sample.ExitTimeUtc>asOf||sample.ExitTimeUtc<=sample.DecisionTimeUtc||
               sample.Outcome is HypothesisExitOutcome.Ambiguous or HypothesisExitOutcome.NoEntry or HypothesisExitOutcome.InvalidRisk||
               instruments.Length>0&&!instruments.Contains(sample.InstrumentId,StringComparer.Ordinal)||
               modules.Length>0&&!modules.Contains(sample.ModuleId,StringComparer.Ordinal))continue;
            definitions.TryGetValue(sample.HypothesisId,out var definition);var features=Geometry(reader.GetString(4));
            var actionClock=sample.EntryTimeUtc.Value;
            features["direction"]=sample.Direction.Equals("Bullish",StringComparison.OrdinalIgnoreCase)?1m:-1m;
            features[$"context.instrument.{sample.InstrumentId}"]=1m;features[$"context.module.{sample.ModuleId}"]=1m;
            features[$"context.pattern.{sample.PatternType}"]=1m;features[$"policy.entry.{definition?.EntryPolicy??"unknown"}"]=1m;
            features[$"policy.stop.{definition?.StopPolicy??"unknown"}"]=1m;features[$"policy.exit.{definition?.ExitPolicy??"unknown"}"]=1m;
            features[$"policy.direction.{definition?.DirectionPolicy.ToString()??"unknown"}"]=1m;
            features["policy.targetR"]=definition?.TargetR??0;features["policy.maximumHoldingMinutes"]=definition?.MaximumHoldingMinutes??0;
            features["policy.initialRiskPoints"]=Math.Abs(sample.EntryPrice!.Value-sample.StopPrice!.Value);
            features["policy.minutesRecognitionToEntry"]=(decimal)(actionClock-sample.DecisionTimeUtc).TotalMinutes;
            var minute=actionClock.Hour*60+actionClock.Minute;
            features["time.hourSin"]=(decimal)Math.Sin(2*Math.PI*minute/1440d);features["time.hourCos"]=(decimal)Math.Cos(2*Math.PI*minute/1440d);
            features["time.weekdaySin"]=(decimal)Math.Sin(2*Math.PI*(int)actionClock.DayOfWeek/7d);features["time.weekdayCos"]=(decimal)Math.Cos(2*Math.PI*(int)actionClock.DayOfWeek/7d);
            features[$"context.session.{SessionSegment(actionClock.Hour)}"]=1m;features["context.session.progressUtcDay"]=minute/1440m;
            AddMarketContext(features,reader.IsDBNull(6)?null:reader.GetString(6),reader.IsDBNull(7)?null:reader.GetString(7),reader.IsDBNull(8)?null:reader.GetString(8));
            var labels=new Dictionary<string,decimal>{{"netR",sample.NetR.Value},{"grossR",sample.GrossR??sample.NetR.Value},
                {"maximumFavorableExcursionR",sample.MaximumFavorableExcursionR??0},{"maximumAdverseExcursionR",sample.MaximumAdverseExcursionR??0},
                {"profitable",sample.NetR>0?1m:0m}};
            var sourceRevision=AgentTrainingDatasetBuilder.Hash($"{run.ContentHash}|{sample.ContentHash}|{reader.GetString(5)}");
            var content=AgentTrainingDatasetBuilder.Hash(JsonSerializer.Serialize(new{sample.SampleId,sample.Split,actionClock,
                OutcomeKnownAt=sample.ExitTimeUtc.Value,features,labels,sourceRevision}));
            values.Add(new(sample.SampleId,sample.ObservationId,sample.SampleId,sample.InstrumentId,sample.ContractId,
                reader.GetString(3),sample.ModuleId,sample.PatternType,sample.Direction,Parse(reader.GetString(1)),
                actionClock,actionClock,sample.ExitTimeUtc.Value,sample.Split,features,labels,sourceRevision,content));
        }
        return values;
    }

    private static async Task Persist(SqliteConnection connection,GenericOutcomeDatasetManifest manifest,
        IReadOnlyList<GenericOutcomeResearchExample> examples,CancellationToken token)
    {
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);await using(var command=connection.CreateCommand())
        {command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO AgentResearchDatasets
            (DatasetId,DatasetVersion,DataRevision,AsOfUtc,TargetHorizonMinutes,ExampleCount,TrainCount,ValidationCount,TestCount,
             EarliestEventUtc,LatestEventUtc,ContentHash,ManifestJson,CreatedAtUtc,CanActivateStrategy,CanRouteToRealBroker)
            VALUES($id,$version,$revision,$asOf,0,$examples,$train,$validation,$test,$earliest,$latest,$hash,$json,$created,0,0);
            """;Add(command,"$id",manifest.DatasetId);Add(command,"$version",manifest.DatasetVersion);Add(command,"$revision",manifest.DataRevision);
            Add(command,"$asOf",manifest.AsOfUtc.ToString("O"));Add(command,"$examples",manifest.ExampleCount);Add(command,"$train",manifest.TrainCount);
            Add(command,"$validation",manifest.ValidationCount);Add(command,"$test",manifest.TestCount);Add(command,"$earliest",manifest.EarliestEventUtc.ToString("O"));
            Add(command,"$latest",manifest.LatestEventUtc.ToString("O"));Add(command,"$hash",manifest.ContentHash);Add(command,"$json",JsonSerializer.Serialize(manifest));Add(command,"$created",DateTime.UtcNow.ToString("O"));await command.ExecuteNonQueryAsync(token);}
        foreach(var x in examples){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO AgentResearchExamples
            (DatasetId,ExampleId,ObservationId,OutcomeId,InstrumentId,ContractId,Timeframe,ModuleId,PatternType,Direction,EventTimeUtc,
             FeatureKnownAtUtc,DecisionTimeUtc,OutcomeKnownAtUtc,Split,FeatureJson,LabelJson,SourceRevision,ContentHash)
            VALUES($dataset,$example,$observation,$outcome,$instrument,$contract,$timeframe,$module,$pattern,$direction,$event,
             $known,$decision,$outcomeKnown,$split,$features,$labels,$revision,$hash);
            """;Add(command,"$dataset",manifest.DatasetId);Add(command,"$example",x.ExampleId);Add(command,"$observation",x.ObservationId);Add(command,"$outcome",x.OutcomeId);Add(command,"$instrument",x.InstrumentId);Add(command,"$contract",x.ContractId);Add(command,"$timeframe",x.Timeframe);Add(command,"$module",x.ModuleId);Add(command,"$pattern",x.PatternType);Add(command,"$direction",x.Direction);Add(command,"$event",x.EventTimeUtc.ToString("O"));Add(command,"$known",x.FeatureKnownAtUtc.ToString("O"));Add(command,"$decision",x.DecisionTimeUtc.ToString("O"));Add(command,"$outcomeKnown",x.OutcomeKnownAtUtc.ToString("O"));Add(command,"$split",x.Split);Add(command,"$features",JsonSerializer.Serialize(x.NumericFeatures));Add(command,"$labels",JsonSerializer.Serialize(x.Labels));Add(command,"$revision",x.SourceRevision);Add(command,"$hash",x.ContentHash);await command.ExecuteNonQueryAsync(token);}await transaction.CommitAsync(token);
    }

    private static Dictionary<string,decimal> Geometry(string json){var values=new Dictionary<string,decimal>(StringComparer.Ordinal);try{using var document=JsonDocument.Parse(json);Walk(document.RootElement,"geometry",values);}catch(JsonException){}return values;}
    private static void AddMarketContext(Dictionary<string,decimal> features,string? latestJson,string? priorCloseText,string? contextJson)
    {
        decimal close=0,volume=0;if(!string.IsNullOrWhiteSpace(latestJson)){using var latest=JsonDocument.Parse(latestJson);var root=latest.RootElement;
            close=TextDecimal(root,"close");var high=TextDecimal(root,"high");var low=TextDecimal(root,"low");volume=TextDecimal(root,"volume");
            features["market.rangeFraction"]=close==0?0:(high-low)/close;features["market.closeLocation"]=high==low?.5m:(close-low)/(high-low);
            features["market.volumeLog"]=(decimal)Math.Log(1+(double)Math.Max(0,volume));}
        if(decimal.TryParse(priorCloseText,NumberStyles.Number,CultureInfo.InvariantCulture,out var prior)&&prior!=0)features["context.momentum.return5Fraction"]=(close-prior)/prior;
        if(string.IsNullOrWhiteSpace(contextJson))return;using var context=JsonDocument.Parse(contextJson);var node=context.RootElement;
        decimal Read(string name)=>node.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetDecimal(out var number)?number:0;
        var range=Read("meanRange20");var meanVolume=Read("meanVolume20");features["context.volatility.meanRange20"]=range;
        features["context.volume.meanVolume20"]=meanVolume;features["context.volume.relativeVolume"]=meanVolume==0?0:volume/meanVolume;
        features["context.trend.meanBodyToRange20"]=range==0?0:Read("meanBody20")/range;features["context.trend.rangeWidth20"]=Read("high20")-Read("low20");
    }
    private static decimal TextDecimal(JsonElement root,string name)=>root.TryGetProperty(name,out var node)&&decimal.TryParse(node.GetString(),NumberStyles.Number,CultureInfo.InvariantCulture,out var value)?value:0;
    private static string SessionSegment(int hour)=>hour switch{<8=>"Overnight",<13=>"Premarket",<16=>"RegularMorning",<18=>"RegularMidday",<20=>"RegularAfternoon",_=>"PostMarket"};
    private static void Walk(JsonElement value,string path,Dictionary<string,decimal> output){if(output.Count>=64)return;if(value.ValueKind==JsonValueKind.Object)foreach(var p in value.EnumerateObject())Walk(p.Value,$"{path}.{p.Name}",output);else if(value.ValueKind==JsonValueKind.Number&&value.TryGetDecimal(out var number))output[path]=number;}
    private static string[] Normalize(IReadOnlyList<string>? values,bool uppercase)=>(values??[]).Where(x=>!string.IsNullOrWhiteSpace(x))
        .Select(x=>uppercase?x.Trim().ToUpperInvariant():x.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Order().ToArray();
    private static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(value,DateTimeKind.Utc):value.ToUniversalTime();
    private static DateTime Parse(string value)=>DateTime.Parse(value,null,DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}
