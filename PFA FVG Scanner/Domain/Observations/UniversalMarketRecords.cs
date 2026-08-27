using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Observations;

public sealed record UniversalMarketObservation(
    string ObservationId,
    int Revision,
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
    string PayloadSchema,
    string PayloadJson,
    IReadOnlyList<string> SourceReferences,
    MarketDataQualityFlags QualityFlags,
    string ContentHash);

public sealed record UniversalOutcomeMetric(
    string MetricName,
    int? HorizonMinutes,
    decimal Value,
    string Unit,
    DateTime? MeasuredAtUtc = null);

public sealed record UniversalOutcomeEvent(
    string EventType,
    DateTime OccurredAtUtc,
    int Ordinal,
    string PayloadJson = "{}");

public sealed record UniversalMarketOutcome(
    string OutcomeId,
    string ObservationId,
    string OutcomeVersion,
    DateTime EvaluatedThroughUtc,
    int SamplesEvaluated,
    string PayloadSchema,
    string PayloadJson,
    IReadOnlyList<UniversalOutcomeMetric> Metrics,
    IReadOnlyList<UniversalOutcomeEvent> Events,
    MarketDataQualityFlags QualityFlags);
