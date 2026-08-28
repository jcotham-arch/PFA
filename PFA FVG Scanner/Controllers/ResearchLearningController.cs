using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers;

[ApiController]
[Route("api/research/learnings")]
public sealed class ResearchLearningController(PfaDatabase database, MarketChartService charts) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? instrumentId = null,
        CancellationToken cancellationToken = default)
    {
        var instrument = string.IsNullOrWhiteSpace(instrumentId) ? null : instrumentId.Trim().ToUpperInvariant();
        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var setups = await Rows(connection, """
            SELECT InstrumentId, ContractId, Timeframe, ModuleId, PatternType, Direction,
                   COUNT(*) ObservationCount, MIN(FormationTimeUtc) EarliestUtc, MAX(FormationTimeUtc) LatestUtc
            FROM UniversalMarketObservations WHERE ($instrument IS NULL OR InstrumentId = $instrument)
            GROUP BY InstrumentId, ContractId, Timeframe, ModuleId, PatternType, Direction
            ORDER BY InstrumentId, Timeframe, ObservationCount DESC;
            """, instrument, cancellationToken);
        var sequences = await Rows(connection, """
            SELECT InstrumentId, ContractId, Timeframe, SequenceDefinitionId, State,
                   COUNT(*) SequenceCount, ROUND(AVG(CAST(PointInTimeConfidence AS REAL)), 4) AveragePointInTimeConfidence,
                   MIN(StartedAtUtc) EarliestUtc, MAX(UpdatedAtUtc) LatestUtc
            FROM MarketSequenceInstances WHERE ($instrument IS NULL OR InstrumentId = $instrument)
            GROUP BY InstrumentId, ContractId, Timeframe, SequenceDefinitionId, State
            ORDER BY InstrumentId, Timeframe, SequenceCount DESC;
            """, instrument, cancellationToken);
        var outcomeMetrics = await Rows(connection, """
            SELECT o.InstrumentId, o.ContractId, o.Timeframe, o.ModuleId, o.PatternType,
                   m.MetricName, m.HorizonMinutes, m.Unit, COUNT(*) SampleCount,
                   ROUND(AVG(CAST(m.Value AS REAL)), 6) AverageValue,
                   ROUND(MIN(CAST(m.Value AS REAL)), 6) MinimumValue,
                   ROUND(MAX(CAST(m.Value AS REAL)), 6) MaximumValue
            FROM UniversalOutcomeMetrics m
            JOIN UniversalMarketOutcomes u ON u.OutcomeId = m.OutcomeId
            JOIN UniversalMarketObservations o ON o.ObservationId = u.ObservationId
            WHERE ($instrument IS NULL OR o.InstrumentId = $instrument)
            GROUP BY o.InstrumentId, o.ContractId, o.Timeframe, o.ModuleId, o.PatternType,
                     m.MetricName, m.HorizonMinutes, m.Unit
            ORDER BY o.InstrumentId, o.ModuleId, m.MetricName, m.HorizonMinutes;
            """, instrument, cancellationToken);
        var coverage = (await charts.GetAllCoverageAsync(cancellationToken))
            .Where(x => instrument is null || x.Symbol.StartsWith(instrument, StringComparison.OrdinalIgnoreCase)).ToArray();
        return Ok(new
        {
            generatedAtUtc = DateTime.UtcNow, instrumentFilter = instrument, coverage, setups, sequences, outcomeMetrics,
            interpretation = new
            {
                historicalEvidenceOnly = true, strategyActivationAuthorized = false, liveRoutingAuthorized = false,
                emptyOutcomeMetricsMeans = "Setups were detected but their universal forward outcomes have not yet been evaluated.",
                contractBoundary = "Results are grouped by explicit dated contract, not an implied continuous future."
            }
        });
    }

    private static async Task<List<Dictionary<string, object?>>> Rows(SqliteConnection connection, string sql,
        string? instrument, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        command.Parameters.AddWithValue("$instrument", instrument is null ? DBNull.Value : instrument);
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
                row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            rows.Add(row);
        }
        return rows;
    }
}
