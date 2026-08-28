using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Patterns.Breakouts;
using PFA_FVG_Scanner.Domain.Patterns.Fvg;
using PFA_FVG_Scanner.Domain.Patterns.Liquidity;
using PFA_FVG_Scanner.Domain.Sequences;
using PFA_FVG_Scanner.Domain.Timeline;
using System.Security.Cryptography;
using System.Text;

namespace PFA_FVG_Scanner.Services;

public sealed record PatternReplaySummary(string InstrumentId, string ContractId, string Timeframe, int BarsEvaluated,
    int ObservationsDetected, IReadOnlyDictionary<string, int> ModuleCounts,
    int SequenceInstancesPersisted, DateTime CompletedAtUtc);

public sealed class PatternSequenceReplayService(
    CanonicalTimelineRepository timeline,
    CandleRepository legacyCandles,
    UniversalMarketRecordRepository observations,
    MarketSequenceRepository sequences,
    IMarketSequenceDefinitionRegistry definitions,
    IMarketSequenceEngine sequenceEngine,
    FvgPatternModule fvg,
    LiquiditySweepPatternModule liquiditySweep,
    RangeBreakoutPatternModule rangeBreakout,
    FailedBreakoutPatternModule failedBreakout)
{
    private static readonly MarketDataQualityFlags Rejected = MarketDataQualityFlags.Incomplete |
        MarketDataQualityFlags.InvalidOhlc | MarketDataQualityFlags.UnresolvedInstrument |
        MarketDataQualityFlags.ProviderConflict;

    public async Task<PatternReplaySummary> ReplayAsync(string instrumentId = "MES", string? contractId = null,
        string timeframe = "5m", DateTime? startUtc = null, DateTime? endUtc = null,
        CancellationToken cancellationToken = default)
    {
        instrumentId = instrumentId.Trim().ToUpperInvariant();
        timeframe = timeframe.Trim().ToLowerInvariant();
        contractId = string.IsNullOrWhiteSpace(contractId) ? instrumentId : contractId.Trim().ToUpperInvariant();
        var bars = await timeline.GetCurrentBarsAsync(instrumentId, timeframe, cancellationToken);
        if (startUtc.HasValue) bars = bars.Where(x => x.OpenTimeUtc >= startUtc.Value.ToUniversalTime()).ToArray();
        if (endUtc.HasValue) bars = bars.Where(x => x.OpenTimeUtc < endUtc.Value.ToUniversalTime()).ToArray();
        if (bars.Count == 0)
            bars = await GetLegacyBarsAsync(instrumentId, contractId, timeframe, startUtc, endUtc, cancellationToken);
        var detectors = new IMarketPatternDetector[] { fvg, liquiditySweep, rangeBreakout, failedBreakout };
        var detected = new Dictionary<string, UniversalMarketObservation>(StringComparer.Ordinal);
        var counts = detectors.ToDictionary(x => x.ModuleId, _ => 0, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < bars.Count; index++)
        {
            var current = bars[index];
            var window = bars.Skip(Math.Max(0, index - 20)).Take(Math.Min(21, index + 1)).ToArray();
            var flags = window.Aggregate(MarketDataQualityFlags.None, (value, bar) => value | bar.QualityFlags);
            if ((flags & Rejected) != 0) continue;
            foreach (var detector in detectors.Where(x => x.SupportedTimeframes.Contains(timeframe)))
            {
                var result = detector.Detect(new(instrumentId.Trim().ToUpperInvariant(), current.ContractId,
                    timeframe.Trim().ToLowerInvariant(), current.CloseTimeUtc, window, flags));
                if (!result.Accepted) continue;
                foreach (var item in result.Observations)
                {
                    var universal = UniversalMarketRecordRepository.FromPattern(item);
                    if (detected.TryAdd(universal.ObservationId, universal)) counts[detector.ModuleId]++;
                }
            }
        }

        foreach (var observation in detected.Values)
            await observations.SaveObservationAsync(observation, cancellationToken);

        var ordered = detected.Values.OrderBy(x => x.FormationTimeUtc).ToArray();
        var sequenceCount = 0;
        foreach (var definition in definitions.GetAll())
        foreach (var instance in sequenceEngine.Replay(definition, ordered,
                     ordered.Length == 0 ? DateTime.UtcNow : ordered[^1].KnownAtUtc))
        {
            await sequences.SaveAsync(definition, instance, cancellationToken);
            sequenceCount++;
        }

        return new(instrumentId, contractId, timeframe, bars.Count,
            detected.Count, counts, sequenceCount, DateTime.UtcNow);
    }

    private async Task<IReadOnlyList<CanonicalBar>> GetLegacyBarsAsync(string instrumentId, string contractId,
        string timeframe, DateTime? startUtc, DateTime? endUtc,
        CancellationToken cancellationToken)
    {
        var minutes = timeframe.ToLowerInvariant() switch { "1m" => 1, "5m" => 5, "15m" => 15, "1h" => 60, _ => 0 };
        if (minutes == 0) return [];
        var source = await legacyCandles.GetRangeAsync(contractId, "1m", startUtc, endUtc, cancellationToken);
        return MarketChartService.Aggregate(source.OrderBy(x => x.OpenTimeUtc).ToArray(), minutes)
            .Where(x => x.IsComplete).Select(x =>
            {
                var naturalKey = $"{instrumentId}|{timeframe}|{x.OpenTimeUtc:O}|legacy";
                var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(naturalKey)));
                return new CanonicalBar(id, 1, instrumentId.Trim().ToUpperInvariant(), contractId, contractId,
                    timeframe.ToLowerInvariant(), x.OpenTimeUtc, x.CloseTimeUtc, x.Open, x.High, x.Low,
                    x.Close, x.Volume, true, $"legacy-{x.OpenTimeUtc:yyyyMMdd}",
                    DateOnly.FromDateTime(x.OpenTimeUtc), "legacy-replay-1.0", "aggregate-1.0",
                    CorrectionState.Original, MarketDataQualityFlags.LegacySession, x.CloseTimeUtc, id);
            }).ToArray();
    }
}
