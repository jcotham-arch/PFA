using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services;

public sealed record GenericOutcomeReplaySummary(string InstrumentId, string ContractId, string Timeframe,
    int ObservationsEvaluated, int OutcomesSaved, DateTime CompletedAtUtc, bool StrategyActivationAuthorized = false,
    bool LiveRoutingAuthorized = false);

public sealed class GenericPatternOutcomeReplayService(
    IInstrumentDefinitionRegistry instruments,
    CandleRepository candles,
    UniversalMarketRecordRepository records)
{
    public const string Version = "generic-forward-1.0.0";
    private static readonly int[] Horizons = [5, 15, 60];

    public async Task<GenericOutcomeReplaySummary> ReplayAsync(string instrumentId, string contractId,
        string timeframe, CancellationToken cancellationToken = default)
    {
        instrumentId = instrumentId.Trim().ToUpperInvariant();
        contractId = contractId.Trim().ToUpperInvariant();
        timeframe = timeframe.Trim().ToLowerInvariant();
        var definition = instruments.Find(instrumentId, DateOnly.FromDateTime(DateTime.UtcNow))
            ?? throw new ArgumentException($"Instrument '{instrumentId}' is not registered.");
        var observations = await records.GetReplayObservationsAsync(instrumentId, contractId, timeframe, cancellationToken);
        var oneMinute = await candles.GetRangeAsync(contractId, "1m", cancellationToken: cancellationToken);
        var saved = 0;
        foreach (var observation in observations)
        {
            var outcome = Calculate(observation, oneMinute, definition, Horizons);
            if (outcome is null) continue;
            await records.SaveOutcomeAsync(outcome, cancellationToken);
            saved++;
        }
        return new(instrumentId, contractId, timeframe, observations.Count, saved, DateTime.UtcNow);
    }

    public static UniversalMarketOutcome? Calculate(UniversalMarketObservation observation,
        IReadOnlyList<Candle> orderedOneMinuteCandles, InstrumentDefinition instrument,
        IReadOnlyList<int>? horizons = null)
    {
        horizons ??= Horizons;
        IReadOnlyList<Candle> ordered = orderedOneMinuteCandles as Candle[] ?? orderedOneMinuteCandles
            .OrderBy(x => x.OpenTimeUtc).ToArray();
        var firstIndex = LowerBound(ordered, observation.KnownAtUtc);
        while (firstIndex < ordered.Count && !ordered[firstIndex].IsClosed) firstIndex++;
        if (firstIndex >= ordered.Count) return null;
        var entryCandle = ordered[firstIndex];
        var entry = entryCandle.Open;
        var sign = observation.Direction switch { PatternDirection.Bullish => 1m, PatternDirection.Bearish => -1m, _ => 0m };
        var metrics = new List<UniversalOutcomeMetric>();
        var evaluatedThrough = entryCandle.OpenTimeUtc.AddMinutes(1);
        var samples = 0;
        foreach (var horizon in horizons.Where(x => x > 0).Distinct().Order())
        {
            var end = observation.KnownAtUtc.AddMinutes(horizon);
            var window = new List<Candle>(horizon);
            for (var index = firstIndex; index < ordered.Count && ordered[index].OpenTimeUtc < end; index++)
                if (ordered[index].IsClosed) window.Add(ordered[index]);
            if (window.Count == 0) continue;
            samples++;
            var final = window[^1]; evaluatedThrough = final.OpenTimeUtc.AddMinutes(1);
            var closePoints = sign == 0m ? final.Close - entry : (final.Close - entry) * sign;
            var favorablePoints = sign switch
            {
                1m => window.Max(x => x.High) - entry,
                -1m => entry - window.Min(x => x.Low),
                _ => window.Max(x => Math.Abs(x.Close - entry))
            };
            var adversePoints = sign switch
            {
                1m => entry - window.Min(x => x.Low),
                -1m => window.Max(x => x.High) - entry,
                _ => 0m
            };
            Add(metrics, "directional-close-change", horizon, closePoints, instrument, final.OpenTimeUtc.AddMinutes(1));
            Add(metrics, "maximum-favorable-excursion", horizon, favorablePoints, instrument, final.OpenTimeUtc.AddMinutes(1));
            Add(metrics, "maximum-adverse-excursion", horizon, adversePoints, instrument, final.OpenTimeUtc.AddMinutes(1));
        }
        if (samples == 0) return null;
        var payload = JsonSerializer.Serialize(new
        {
            entryReference = "first-complete-one-minute-bar-open-at-or-after-known-at",
            entryPrice = entry, instrument.InstrumentId, instrument.DefinitionVersion,
            instrument.TickSize, instrument.PointValue, horizonsMinutes = horizons
        });
        var idSeed = $"{observation.ObservationId}|{Version}|{evaluatedThrough:O}";
        var id = $"OUT-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idSeed)))[..32]}";
        return new(id, observation.ObservationId, Version, evaluatedThrough, samples,
            "pfa.generic-forward-outcome/1.0", payload, metrics,
            [new("EntryReferenceEstablished", entryCandle.OpenTimeUtc, 1,
                JsonSerializer.Serialize(new { price = entry, source = "next-bar-open" }))], observation.QualityFlags);
    }

    private static int LowerBound(IReadOnlyList<Candle> candles, DateTime target)
    {
        var low = 0; var high = candles.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (candles[middle].OpenTimeUtc < target) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static void Add(List<UniversalOutcomeMetric> metrics, string name, int horizon, decimal points,
        InstrumentDefinition instrument, DateTime measuredAt)
    {
        metrics.Add(new(name, horizon, points, "points", measuredAt));
        metrics.Add(new(name, horizon, points / instrument.TickSize, "ticks", measuredAt));
        metrics.Add(new(name, horizon, points * instrument.PointValue, "usd-per-contract", measuredAt));
    }
}
