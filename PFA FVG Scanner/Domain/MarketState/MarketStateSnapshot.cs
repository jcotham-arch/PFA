using PFA_FVG_Scanner.Domain.Features;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.MarketState;

public sealed record MarketStateSnapshot(
    string MarketStateSnapshotId,
    string InstrumentId,
    string? ContractId,
    DateTime AsOfUtc,
    DateTime KnownAtUtc,
    string DataRevision,
    string EngineVersion,
    string TradingSessionId,
    MarketDataQualityFlags QualityFlags,
    IReadOnlyList<FeatureValue> Facts,
    IReadOnlyList<string> SourceCanonicalBarIds);

public interface IMarketStateEngine
{
    MarketStateSnapshot Build(string instrumentId, string? contractId, DateTime asOfUtc,
        string dataRevision, IReadOnlyList<CanonicalBar> bars);
}
