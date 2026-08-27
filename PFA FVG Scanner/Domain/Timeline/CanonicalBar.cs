namespace PFA_FVG_Scanner.Domain.Timeline;

[Flags]
public enum MarketDataQualityFlags
{
    None = 0,
    Incomplete = 1,
    InvalidOhlc = 2,
    UnresolvedInstrument = 4,
    UnresolvedContract = 8,
    LegacySession = 16,
    ProviderConflict = 32,
    Corrected = 64
}

public enum CorrectionState
{
    Original,
    DuplicateEquivalent,
    CorrectedRevision,
    ProviderConflict
}

public sealed record CanonicalBar(
    string CanonicalBarId,
    int Revision,
    string InstrumentId,
    string? ContractId,
    string ProviderSymbol,
    string Timeframe,
    DateTime OpenTimeUtc,
    DateTime CloseTimeUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    bool IsComplete,
    string TradingSessionId,
    DateOnly TradingDate,
    string CanonicalizationVersion,
    string TransformationVersion,
    CorrectionState CorrectionState,
    MarketDataQualityFlags QualityFlags,
    DateTime RevisionEffectiveUtc,
    string ContentHash);

public sealed record CanonicalBarSource(
    string SourceId,
    string CanonicalBarId,
    int Revision,
    string Provider,
    string ProviderSymbol,
    string SourceEventType,
    string SourceResolution,
    DateTime SourceTimestampUtc,
    DateTime ReceivedTimestampUtc,
    string SourceVersion,
    string IngestionRunId,
    string? RawReference);

public sealed record CanonicalBarWriteResult(
    CanonicalBar Bar,
    bool CreatedRevision,
    bool AddedSource,
    bool WasEquivalentDuplicate,
    bool WasProviderConflict);
