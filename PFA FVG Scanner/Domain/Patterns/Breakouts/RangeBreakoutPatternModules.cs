using System.Globalization;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Patterns.Breakouts;

public enum RangeBoundarySide { Upper, Lower }

public sealed record RangeBreakoutGeometry(
    RangeBoundarySide BoundarySide,
    decimal RangeLower,
    decimal RangeUpper,
    decimal BreakExtreme,
    decimal DetectionClose,
    decimal ExcursionDepth,
    bool ClosedBeyondBoundary,
    IReadOnlyList<string> RangeBarIds) : IPatternGeometry;

internal sealed record PriorRange(decimal Lower, decimal Upper, CanonicalBar Detection, CanonicalBar[] Bars);

internal static class PriorRangeEvaluator
{
    public static PriorRange? Evaluate(MarketPatternContext context, int lookback)
    {
        var ordered = context.Bars.OrderBy(x => x.OpenTimeUtc).ToArray();
        var detection = ordered[^1];
        var prior = ordered.Take(ordered.Length - 1).Where(x => x.TradingSessionId == detection.TradingSessionId)
            .TakeLast(lookback).ToArray();
        return prior.Length < 2 ? null : new(prior.Min(x => x.Low), prior.Max(x => x.High), detection, prior);
    }
}

public abstract class RangeBoundaryPatternModule : IMarketPatternDetector
{
    private readonly int _lookback;
    protected RangeBoundaryPatternModule(int lookbackBars = 20)
    { if (lookbackBars < 2) throw new ArgumentOutOfRangeException(nameof(lookbackBars)); _lookback = lookbackBars; }
    public abstract string ModuleId { get; }
    public abstract string ModuleVersion { get; }
    protected abstract bool WantsClosedBeyond { get; }
    protected abstract string PatternType { get; }
    public IReadOnlySet<string> SupportedTimeframes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1m", "5m", "15m", "1h" };

    public PatternDetectionResult Detect(MarketPatternContext context)
    {
        var rejection = MarketPatternContract.Validate(this, context);
        if (rejection is not null) return PatternDetectionResult.Rejected(rejection);
        var range = PriorRangeEvaluator.Evaluate(context, _lookback);
        if (range is null) return PatternDetectionResult.Rejected("Range detection requires two prior bars in the same trading session.");
        var observations = new List<MarketPatternObservation>(2);
        AddIfMatch(observations, context, range, RangeBoundarySide.Upper);
        AddIfMatch(observations, context, range, RangeBoundarySide.Lower);
        return PatternDetectionResult.Success(observations);
    }

    private void AddIfMatch(List<MarketPatternObservation> observations, MarketPatternContext context,
        PriorRange range, RangeBoundarySide side)
    {
        var boundary = side == RangeBoundarySide.Upper ? range.Upper : range.Lower;
        var extreme = side == RangeBoundarySide.Upper ? range.Detection.High : range.Detection.Low;
        var penetrated = side == RangeBoundarySide.Upper ? extreme > boundary : extreme < boundary;
        var closedBeyond = side == RangeBoundarySide.Upper
            ? range.Detection.Close > boundary : range.Detection.Close < boundary;
        if (!penetrated || closedBeyond != WantsClosedBeyond) return;
        var geometry = new RangeBreakoutGeometry(side, range.Lower, range.Upper, extreme,
            range.Detection.Close, Math.Abs(extreme - boundary), closedBeyond,
            range.Bars.Select(x => x.CanonicalBarId).ToArray());
        var discriminator = string.Join('|', side, range.Lower.ToString("G29", CultureInfo.InvariantCulture),
            range.Upper.ToString("G29", CultureInfo.InvariantCulture), extreme.ToString("G29", CultureInfo.InvariantCulture));
        observations.Add(new(MarketPatternContract.CreateObservationId(ModuleId, ModuleVersion,
                context.InstrumentId, context.Timeframe, range.Detection.OpenTimeUtc, discriminator),
            ModuleId, ModuleVersion, PatternType, context.InstrumentId, context.ContractId, context.Timeframe,
            side == RangeBoundarySide.Upper ? PatternDirection.Bullish : PatternDirection.Bearish,
            range.Detection.OpenTimeUtc, range.Detection.CloseTimeUtc, PatternLifecycleState.Detected,
            geometry, range.Bars.Select(x => x.CanonicalBarId).Append(range.Detection.CanonicalBarId).ToArray(),
            context.QualityFlags));
    }
}

public sealed class RangeBreakoutPatternModule(int lookbackBars = 20) : RangeBoundaryPatternModule(lookbackBars)
{
    public const string Identifier = "range-breakout";
    public override string ModuleId => Identifier;
    public override string ModuleVersion => "capture-1.0.0";
    protected override bool WantsClosedBeyond => true;
    protected override string PatternType => "RangeBreakout";
}

public sealed class FailedBreakoutPatternModule(int lookbackBars = 20) : RangeBoundaryPatternModule(lookbackBars)
{
    public const string Identifier = "failed-breakout";
    public override string ModuleId => Identifier;
    public override string ModuleVersion => "capture-1.0.0";
    protected override bool WantsClosedBeyond => false;
    protected override string PatternType => "FailedBreakout";
}
