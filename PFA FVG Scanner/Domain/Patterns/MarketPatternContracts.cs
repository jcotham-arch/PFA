using System.Security.Cryptography;
using System.Text;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Patterns;

public enum PatternDirection { Bullish, Bearish, Neutral }
public enum PatternLifecycleState { Detected, Active, PartiallyResolved, Resolved, Invalidated }

public interface IPatternGeometry { }

public sealed record PriceZoneGeometry(decimal LowerBoundary, decimal UpperBoundary) : IPatternGeometry;

public sealed record MarketPatternContext(
    string InstrumentId,
    string? ContractId,
    string Timeframe,
    DateTime AsOfUtc,
    IReadOnlyList<CanonicalBar> Bars,
    MarketDataQualityFlags QualityFlags);

public sealed record MarketPatternObservation(
    string ObservationId,
    string ModuleId,
    string ModuleVersion,
    string PatternType,
    string InstrumentId,
    string? ContractId,
    string Timeframe,
    PatternDirection Direction,
    DateTime FormationTimeUtc,
    DateTime KnownAtUtc,
    PatternLifecycleState LifecycleState,
    IPatternGeometry Geometry,
    IReadOnlyList<string> SourceCanonicalBarIds,
    MarketDataQualityFlags QualityFlags);

public sealed record PatternDetectionResult(
    IReadOnlyList<MarketPatternObservation> Observations,
    bool Accepted,
    string? RejectionReason)
{
    public static PatternDetectionResult Rejected(string reason) => new([], false, reason);
    public static PatternDetectionResult Success(IReadOnlyList<MarketPatternObservation> observations) =>
        new(observations, true, null);
}

public interface IMarketPatternDetector
{
    string ModuleId { get; }
    string ModuleVersion { get; }
    IReadOnlySet<string> SupportedTimeframes { get; }
    PatternDetectionResult Detect(MarketPatternContext context);
}

public sealed record PatternModuleDefinition(
    string ModuleId,
    string DisplayName,
    string Version,
    IReadOnlySet<string> SupportedTimeframes,
    string Maturity,
    string Description);

public interface IMarketPatternModuleRegistry
{
    IReadOnlyList<PatternModuleDefinition> GetAll();
    PatternModuleDefinition? Find(string moduleId);
}

public static class MarketPatternContract
{
    private static readonly MarketDataQualityFlags RejectedQuality =
        MarketDataQualityFlags.Incomplete |
        MarketDataQualityFlags.InvalidOhlc |
        MarketDataQualityFlags.UnresolvedInstrument |
        MarketDataQualityFlags.ProviderConflict;

    public static string? Validate(IMarketPatternDetector detector, MarketPatternContext context)
    {
        if (!detector.SupportedTimeframes.Contains(context.Timeframe))
            return $"Timeframe {context.Timeframe} is not supported by {detector.ModuleId}.";
        if (context.Bars.Count == 0)
            return "At least one canonical bar is required.";
        if (context.Bars.Any(x => x.CloseTimeUtc > context.AsOfUtc))
            return "Future bars are not allowed in a point-in-time pattern context.";
        if ((context.QualityFlags & RejectedQuality) != 0)
            return $"Context quality is not eligible for detection: {context.QualityFlags}.";
        return null;
    }

    public static string CreateObservationId(string moduleId, string moduleVersion,
        string instrumentId, string timeframe, DateTime formationTimeUtc, string discriminator)
    {
        var value = string.Join('|', moduleId.Trim().ToUpperInvariant(), moduleVersion,
            instrumentId.Trim().ToUpperInvariant(), timeframe.Trim().ToLowerInvariant(),
            formationTimeUtc.ToUniversalTime().ToString("O"), discriminator.Trim().ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
