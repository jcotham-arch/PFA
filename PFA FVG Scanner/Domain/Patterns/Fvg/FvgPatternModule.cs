using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Domain.Patterns.Fvg;

public sealed class FvgPatternModule : IMarketPatternDetector
{
    public const string ModuleIdentifier = "fvg";
    public const string CompatibilityVersion = "legacy-1.0.0";
    private readonly FvgDetectionService _legacyDetector;

    public FvgPatternModule(FvgDetectionService legacyDetector) => _legacyDetector = legacyDetector;
    public string ModuleId => ModuleIdentifier;
    public string ModuleVersion => CompatibilityVersion;
    public IReadOnlySet<string> SupportedTimeframes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "5m" };

    public PatternDetectionResult Detect(MarketPatternContext context)
    {
        var rejection = MarketPatternContract.Validate(this, context);
        if (rejection is not null) return PatternDetectionResult.Rejected(rejection);
        if (context.Bars.Count < 3) return PatternDetectionResult.Rejected("FVG detection requires three canonical bars.");
        var source = context.Bars.OrderBy(x => x.OpenTimeUtc).TakeLast(3).ToArray();
        var candles = source.Select(MapCandle).ToArray();
        var legacy = _legacyDetector.Detect(candles[0], candles[1], candles[2]);
        if (legacy is null) return PatternDetectionResult.Success([]);
        var observation = new MarketPatternObservation(
            CreateLegacyObservationId(legacy), ModuleId, ModuleVersion, "FairValueGap",
            context.InstrumentId, context.ContractId, context.Timeframe,
            legacy.Direction == FvgDirection.Bullish ? PatternDirection.Bullish : PatternDirection.Bearish,
            legacy.FormationTimeUtc, source[^1].CloseTimeUtc, PatternLifecycleState.Detected,
            new PriceZoneGeometry(legacy.LowerBoundary, legacy.UpperBoundary),
            source.Select(x => x.CanonicalBarId).ToArray(), context.QualityFlags);
        return PatternDetectionResult.Success([observation]);
    }

    public static Candle MapCandle(CanonicalBar bar) => new()
    {
        Symbol = bar.ProviderSymbol, Timeframe = bar.Timeframe, OpenTimeUtc = bar.OpenTimeUtc,
        Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close,
        Volume = bar.Volume, IsClosed = bar.IsComplete
    };

    public static string CreateLegacyObservationId(FairValueGap fvg)
    {
        var naturalKey = string.Join("|", fvg.Symbol.Trim().ToUpperInvariant(),
            fvg.Timeframe.Trim().ToLowerInvariant(), fvg.FormationTimeUtc.ToUniversalTime().ToString("O"),
            fvg.Direction, fvg.LowerBoundary.ToString("G29", CultureInfo.InvariantCulture),
            fvg.UpperBoundary.ToString("G29", CultureInfo.InvariantCulture),
            fvg.GapSize.ToString("G29", CultureInfo.InvariantCulture), "1.0.0");
        return "FVG-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(naturalKey)));
    }
}
