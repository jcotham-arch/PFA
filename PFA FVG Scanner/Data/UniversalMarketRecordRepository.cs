using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Patterns.Fvg;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Data;

public sealed class UniversalMarketRecordRepository
{
    private readonly PfaDatabase _database;
    public UniversalMarketRecordRepository(PfaDatabase database) => _database = database;

    public async Task SaveObservationAsync(UniversalMarketObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertObservationAsync(connection, transaction, observation, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveOutcomeAsync(UniversalMarketOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        await SaveOutcomesAsync([outcome], cancellationToken);
    }

    public async Task SaveOutcomesAsync(IReadOnlyList<UniversalMarketOutcome> outcomes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        if (outcomes.Count == 0) return;
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var outcome in outcomes)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            await InsertOutcomeAsync(connection, transaction, outcome, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertOutcomeAsync(SqliteConnection connection, SqliteTransaction transaction,
        UniversalMarketOutcome outcome, CancellationToken cancellationToken)
    {

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO UniversalMarketOutcomes
                    (OutcomeId, ObservationId, OutcomeVersion, EvaluatedThroughUtc,
                     SamplesEvaluated, PayloadSchema, PayloadJson, QualityFlags, CreatedAtUtc)
                VALUES ($id, $observationId, $version, $evaluatedThrough, $samples,
                        $schema, $payload, $quality, $createdAt);
                """;
            command.Parameters.AddWithValue("$id", outcome.OutcomeId);
            command.Parameters.AddWithValue("$observationId", outcome.ObservationId);
            command.Parameters.AddWithValue("$version", outcome.OutcomeVersion);
            command.Parameters.AddWithValue("$evaluatedThrough", Utc(outcome.EvaluatedThroughUtc).ToString("O"));
            command.Parameters.AddWithValue("$samples", outcome.SamplesEvaluated);
            command.Parameters.AddWithValue("$schema", outcome.PayloadSchema);
            command.Parameters.AddWithValue("$payload", outcome.PayloadJson);
            command.Parameters.AddWithValue("$quality", (int)outcome.QualityFlags);
            command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var metric in outcome.Metrics)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO UniversalOutcomeMetrics
                    (OutcomeId, MetricName, HorizonMinutes, Value, Unit, MeasuredAtUtc)
                VALUES ($outcomeId, $name, $horizon, $value, $unit, $measuredAt);
                """;
            command.Parameters.AddWithValue("$outcomeId", outcome.OutcomeId);
            command.Parameters.AddWithValue("$name", metric.MetricName);
            command.Parameters.AddWithValue("$horizon", metric.HorizonMinutes ?? -1);
            command.Parameters.AddWithValue("$value", metric.Value.ToString("G29", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$unit", metric.Unit);
            command.Parameters.AddWithValue("$measuredAt", metric.MeasuredAtUtc is null
                ? DBNull.Value : Utc(metric.MeasuredAtUtc.Value).ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var occurrence in outcome.Events.OrderBy(x => x.Ordinal))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO UniversalOutcomeEvents
                    (OutcomeEventId, OutcomeId, ObservationId, EventType, OccurredAtUtc, Ordinal, PayloadJson)
                VALUES ($id, $outcomeId, $observationId, $type, $occurredAt, $ordinal, $payload);
                """;
            command.Parameters.AddWithValue("$id", StableHash(outcome.OutcomeId, occurrence.EventType,
                Utc(occurrence.OccurredAtUtc).ToString("O"), occurrence.Ordinal.ToString(CultureInfo.InvariantCulture)));
            command.Parameters.AddWithValue("$outcomeId", outcome.OutcomeId);
            command.Parameters.AddWithValue("$observationId", outcome.ObservationId);
            command.Parameters.AddWithValue("$type", occurrence.EventType);
            command.Parameters.AddWithValue("$occurredAt", Utc(occurrence.OccurredAtUtc).ToString("O"));
            command.Parameters.AddWithValue("$ordinal", occurrence.Ordinal);
            command.Parameters.AddWithValue("$payload", occurrence.PayloadJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<UniversalMarketObservation>> GetObservationsAsync(
        string? moduleId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ObservationId, Revision, ModuleId, ModuleVersion, PatternType, InstrumentId,
                   ContractId, Timeframe, Direction, FormationTimeUtc, KnownAtUtc, LifecycleState,
                   PayloadSchema, PayloadJson, SourceReferencesJson, QualityFlags, ContentHash
            FROM UniversalMarketObservations
            WHERE ($moduleId IS NULL OR ModuleId = $moduleId)
            ORDER BY FormationTimeUtc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$moduleId", string.IsNullOrWhiteSpace(moduleId) ? DBNull.Value : moduleId);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var records = new List<UniversalMarketObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            records.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7), Enum.Parse<PatternDirection>(reader.GetString(8)), DateTime.Parse(reader.GetString(9), null,
                    DateTimeStyles.RoundtripKind), DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
                Enum.Parse<PatternLifecycleState>(reader.GetString(11)), reader.GetString(12), reader.GetString(13),
                JsonSerializer.Deserialize<string[]>(reader.GetString(14)) ?? [], (MarketDataQualityFlags)reader.GetInt32(15),
                reader.GetString(16)));
        return records;
    }

    public async Task<IReadOnlyList<UniversalMarketObservation>> GetReplayObservationsAsync(
        string instrumentId, string contractId, string timeframe,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ObservationId, Revision, ModuleId, ModuleVersion, PatternType, InstrumentId,
                   ContractId, Timeframe, Direction, FormationTimeUtc, KnownAtUtc, LifecycleState,
                   PayloadSchema, PayloadJson, SourceReferencesJson, QualityFlags, ContentHash
            FROM UniversalMarketObservations
            WHERE InstrumentId = $instrument AND ContractId = $contract AND Timeframe = $timeframe
            ORDER BY KnownAtUtc, ObservationId;
            """;
        command.Parameters.AddWithValue("$instrument", instrumentId.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$contract", contractId.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$timeframe", timeframe.Trim().ToLowerInvariant());
        var records = new List<UniversalMarketObservation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            records.Add(ReadObservation(reader));
        return records;
    }

    public async Task<IReadOnlyList<object>> GetOutcomesAsync(string? observationId = null, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OutcomeId, ObservationId, OutcomeVersion, EvaluatedThroughUtc, SamplesEvaluated,
                   PayloadSchema, PayloadJson, QualityFlags, CreatedAtUtc
            FROM UniversalMarketOutcomes
            WHERE ($observationId IS NULL OR ObservationId = $observationId)
            ORDER BY EvaluatedThroughUtc DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$observationId",
            string.IsNullOrWhiteSpace(observationId) ? DBNull.Value : observationId);
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var records = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            records.Add(new
            {
                outcomeId = reader.GetString(0), observationId = reader.GetString(1),
                outcomeVersion = reader.GetString(2), evaluatedThroughUtc = reader.GetString(3),
                samplesEvaluated = reader.GetInt32(4), payloadSchema = reader.GetString(5),
                payloadJson = reader.GetString(6), qualityFlags = reader.GetInt32(7),
                createdAtUtc = reader.GetString(8)
            });
        return records;
    }

    private static UniversalMarketObservation ReadObservation(SqliteDataReader reader) =>
        new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetString(7), Enum.Parse<PatternDirection>(reader.GetString(8)), DateTime.Parse(reader.GetString(9), null,
                DateTimeStyles.RoundtripKind), DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
            Enum.Parse<PatternLifecycleState>(reader.GetString(11)), reader.GetString(12), reader.GetString(13),
            JsonSerializer.Deserialize<string[]>(reader.GetString(14)) ?? [], (MarketDataQualityFlags)reader.GetInt32(15),
            reader.GetString(16));

    public static UniversalMarketObservation FromFvg(FairValueGap fvg)
    {
        var id = FvgPatternModule.CreateLegacyObservationId(fvg);
        var payload = JsonSerializer.Serialize(new { fvg.LowerBoundary, fvg.UpperBoundary, fvg.GapSize,
            fvg.Midpoint, Status = fvg.Status.ToString(), LegacyFvgId = fvg.Id });
        return new(id, 1, "fvg", FvgPatternModule.CompatibilityVersion, "FairValueGap",
            fvg.Symbol.Trim().ToUpperInvariant(), null, fvg.Timeframe.Trim().ToLowerInvariant(),
            fvg.Direction == FvgDirection.Bullish ? PatternDirection.Bullish : PatternDirection.Bearish,
            Utc(fvg.FormationTimeUtc), Utc(fvg.DetectedAtUtc == default ? fvg.FormationTimeUtc : fvg.DetectedAtUtc),
            PatternLifecycleState.Detected, "pfa.fvg.observation/1.0", payload, [],
            MarketDataQualityFlags.None, StableHash(payload));
    }

    public static UniversalMarketObservation FromPattern(MarketPatternObservation observation)
    {
        var payload = JsonSerializer.Serialize(observation.Geometry, observation.Geometry.GetType());
        return new(observation.ObservationId, 1, observation.ModuleId, observation.ModuleVersion,
            observation.PatternType, observation.InstrumentId, observation.ContractId,
            observation.Timeframe, observation.Direction, Utc(observation.FormationTimeUtc),
            Utc(observation.KnownAtUtc), observation.LifecycleState,
            $"pfa.{observation.ModuleId}.observation/1.0", payload,
            observation.SourceCanonicalBarIds, observation.QualityFlags, StableHash(payload));
    }

    public static UniversalMarketOutcome FromFvgOutcome(FvgOutcome outcome)
    {
        var fvg = new FairValueGap { Symbol = outcome.Symbol, Timeframe = outcome.Timeframe,
            FormationTimeUtc = outcome.FormationTimeUtc, Direction = outcome.Direction,
            LowerBoundary = outcome.LowerBoundary, UpperBoundary = outcome.UpperBoundary, GapSize = outcome.GapSize };
        var metrics = new List<UniversalOutcomeMetric>();
        AddMetric(metrics, "return", 5, outcome.Return5Minutes, "points");
        AddMetric(metrics, "return", 15, outcome.Return15Minutes, "points");
        AddMetric(metrics, "return", 30, outcome.Return30Minutes, "points");
        AddMetric(metrics, "return", 60, outcome.Return60Minutes, "points");
        AddMetric(metrics, "mfe", null, outcome.MaximumFavorableExcursion, "points");
        AddMetric(metrics, "mae", null, outcome.MaximumAdverseExcursion, "points");
        var rawEvents = new List<(string Type, DateTime? At)>
        {
            ("first-touch", outcome.FirstTouchTimeUtc), ("fill-25", outcome.TwentyFivePercentFillTimeUtc),
            ("fill-50", outcome.FiftyPercentFillTimeUtc), ("fill-75", outcome.SeventyFivePercentFillTimeUtc),
            ("full-fill", outcome.FullFillTimeUtc)
        };
        var events = rawEvents.Where(x => x.At.HasValue).OrderBy(x => x.At)
            .Select((x, index) => new UniversalOutcomeEvent(x.Type, x.At!.Value, index + 1)).ToArray();
        return new(outcome.OutcomeId.ToString(), FvgPatternModule.CreateLegacyObservationId(fvg),
            outcome.EngineVersion, Utc(outcome.EvaluatedThroughUtc), outcome.MinuteCandlesEvaluated,
            "pfa.fvg.outcome/1.1", JsonSerializer.Serialize(outcome), metrics, events, MarketDataQualityFlags.None);
    }

    internal static async Task InsertObservationAsync(SqliteConnection connection, SqliteTransaction transaction,
        UniversalMarketObservation observation, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO UniversalMarketObservations
                (ObservationId, Revision, ModuleId, ModuleVersion, PatternType, InstrumentId, ContractId,
                 Timeframe, Direction, FormationTimeUtc, KnownAtUtc, LifecycleState, PayloadSchema,
                 PayloadJson, SourceReferencesJson, QualityFlags, ContentHash, CreatedAtUtc)
            VALUES ($id, $revision, $module, $version, $type, $instrument, $contract, $timeframe,
                    $direction, $formation, $known, $lifecycle, $schema, $payload, $sources,
                    $quality, $hash, $createdAt);
            INSERT OR IGNORE INTO UniversalObservationLifecycleEvents
                (LifecycleEventId, ObservationId, ObservationRevision, LifecycleState, OccurredAtUtc, Reason)
            VALUES ($eventId, $id, $revision, $lifecycle, $known, 'initial-capture');
            """;
        command.Parameters.AddWithValue("$id", observation.ObservationId);
        command.Parameters.AddWithValue("$revision", observation.Revision);
        command.Parameters.AddWithValue("$module", observation.ModuleId);
        command.Parameters.AddWithValue("$version", observation.ModuleVersion);
        command.Parameters.AddWithValue("$type", observation.PatternType);
        command.Parameters.AddWithValue("$instrument", observation.InstrumentId);
        command.Parameters.AddWithValue("$contract", (object?)observation.ContractId ?? DBNull.Value);
        command.Parameters.AddWithValue("$timeframe", observation.Timeframe);
        command.Parameters.AddWithValue("$direction", observation.Direction.ToString());
        command.Parameters.AddWithValue("$formation", Utc(observation.FormationTimeUtc).ToString("O"));
        command.Parameters.AddWithValue("$known", Utc(observation.KnownAtUtc).ToString("O"));
        command.Parameters.AddWithValue("$lifecycle", observation.LifecycleState.ToString());
        command.Parameters.AddWithValue("$schema", observation.PayloadSchema);
        command.Parameters.AddWithValue("$payload", observation.PayloadJson);
        command.Parameters.AddWithValue("$sources", JsonSerializer.Serialize(observation.SourceReferences));
        command.Parameters.AddWithValue("$quality", (int)observation.QualityFlags);
        command.Parameters.AddWithValue("$hash", observation.ContentHash);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$eventId", StableHash(observation.ObservationId,
            observation.Revision.ToString(CultureInfo.InvariantCulture), observation.LifecycleState.ToString()));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddMetric(List<UniversalOutcomeMetric> metrics, string name, int? horizon,
        decimal? value, string unit) { if (value.HasValue) metrics.Add(new(name, horizon, value.Value, unit)); }
    private static DateTime Utc(DateTime value) => value.Kind switch
    { DateTimeKind.Utc => value, DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc), _ => value.ToUniversalTime() };
    private static string StableHash(params string[] values) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', values))));
}
