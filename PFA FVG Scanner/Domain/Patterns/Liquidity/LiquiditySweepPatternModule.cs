using System.Globalization;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Patterns.Liquidity;

public enum LiquiditySide { BuySide, SellSide }

public sealed record LiquiditySweepGeometry(
    LiquiditySide LiquiditySide,
    decimal ReferenceLevel,
    decimal SweepExtreme,
    decimal PenetrationDepth,
    bool ReclaimedOnDetectionBar,
    int EqualLevelCount,
    IReadOnlyList<string> ReferenceBarIds) : IPatternGeometry;

public sealed class LiquiditySweepPatternModule : IMarketPatternDetector
{
    public const string ModuleIdentifier = "liquidity-sweep";
    public const string CurrentVersion = "capture-1.0.0";
    private readonly int _lookbackBars;
    private readonly decimal _minimumPenetration;
    public LiquiditySweepPatternModule(int lookbackBars = 20, decimal minimumPenetration = 0m)
    {
        if (lookbackBars < 2) throw new ArgumentOutOfRangeException(nameof(lookbackBars));
        if (minimumPenetration < 0) throw new ArgumentOutOfRangeException(nameof(minimumPenetration));
        _lookbackBars = lookbackBars; _minimumPenetration = minimumPenetration;
    }
    public string ModuleId => ModuleIdentifier;
    public string ModuleVersion => CurrentVersion;
    public IReadOnlySet<string> SupportedTimeframes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1m", "5m", "15m", "1h" };

    public PatternDetectionResult Detect(MarketPatternContext context)
    {
        var rejection = MarketPatternContract.Validate(this, context);
        if (rejection is not null) return PatternDetectionResult.Rejected(rejection);
        var ordered = context.Bars.OrderBy(x => x.OpenTimeUtc).ToArray();
        var detection = ordered[^1];
        var prior = ordered.Take(ordered.Length - 1)
            .Where(x => x.TradingSessionId == detection.TradingSessionId)
            .TakeLast(_lookbackBars).ToArray();
        if (prior.Length < 2)
            return PatternDetectionResult.Rejected("Liquidity sweep detection requires two prior bars in the same trading session.");

        var observations = new List<MarketPatternObservation>(2);
        var priorHigh = prior.Max(x => x.High);
        if (detection.High > priorHigh + _minimumPenetration)
            observations.Add(Create(context, detection, prior, LiquiditySide.BuySide, priorHigh,
                detection.High, detection.Close <= priorHigh, PatternDirection.Bearish));
        var priorLow = prior.Min(x => x.Low);
        if (detection.Low < priorLow - _minimumPenetration)
            observations.Add(Create(context, detection, prior, LiquiditySide.SellSide, priorLow,
                detection.Low, detection.Close >= priorLow, PatternDirection.Bullish));
        return PatternDetectionResult.Success(observations);
    }

    private MarketPatternObservation Create(MarketPatternContext context, CanonicalBar detection,
        CanonicalBar[] prior, LiquiditySide side, decimal level, decimal extreme, bool reclaimed,
        PatternDirection direction)
    {
        var references = side == LiquiditySide.BuySide
            ? prior.Where(x => x.High == level).Select(x => x.CanonicalBarId).ToArray()
            : prior.Where(x => x.Low == level).Select(x => x.CanonicalBarId).ToArray();
        var depth = Math.Abs(extreme - level);
        var discriminator = string.Join('|', side, level.ToString("G29", CultureInfo.InvariantCulture),
            extreme.ToString("G29", CultureInfo.InvariantCulture), reclaimed);
        var geometry = new LiquiditySweepGeometry(side, level, extreme, depth, reclaimed,
            references.Length, references);
        return new(MarketPatternContract.CreateObservationId(ModuleId, ModuleVersion, context.InstrumentId,
                context.Timeframe, detection.OpenTimeUtc, discriminator), ModuleId, ModuleVersion,
            "LiquiditySweep", context.InstrumentId, context.ContractId, context.Timeframe, direction,
            detection.OpenTimeUtc, detection.CloseTimeUtc, PatternLifecycleState.Detected, geometry,
            references.Append(detection.CanonicalBarId).ToArray(), context.QualityFlags);
    }
}
