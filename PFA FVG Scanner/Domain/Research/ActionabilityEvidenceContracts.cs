using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Research;

public enum ActionabilitySubjectKind { Pattern,Sequence,StructuralEvent }
public enum ActionabilityCoverageStatus { Evaluated,PartiallyEvaluated,AwaitingScenarioEvaluation }

public sealed record ActionabilityScenario(
    string ScenarioId,string HypothesisId,string Direction,string? DirectionPolicy,string? EntryPolicy,
    string? StopPolicy,string? ExitPolicy,decimal? TargetR,int? MaximumHoldingMinutes,
    DateTime DecisionTimeUtc,DateTime? EntryTimeUtc,decimal? EntryPrice,decimal? StopPrice,decimal? TargetPrice,
    DateTime? ExitTimeUtc,decimal? ExitPrice,string Outcome,string Classification,decimal? GrossR,decimal? NetR,
    decimal? MaximumFavorableExcursionR,decimal? MaximumAdverseExcursionR,string Reason,
    bool EligibleForAgentTraining,bool IsActionable=false,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record ActionabilityEvidenceRecord(
    string RecordId,ActionabilitySubjectKind SubjectKind,string SourceId,string SourceVersion,string InstrumentId,
    string? ContractId,string Timeframe,string EventType,string Direction,DateTime EventTimeUtc,DateTime RecognizedAtUtc,
    ActionabilityCoverageStatus CoverageStatus,JsonElement KnownFacts,IReadOnlyList<ActionabilityScenario> Scenarios,
    IReadOnlyList<string> MissingEvaluationFields,string SourceContentHash,bool IsResearchOnly=true,
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record ActionabilityCoverageSummary(int Subjects,int Evaluated,int PartiallyEvaluated,int AwaitingEvaluation,
    int Scenarios,int AgentTrainingEligibleScenarios);

public sealed record ActionabilityDayReport(DateOnly DateUtc,DateTime GeneratedAtUtc,
    ActionabilityCoverageSummary Coverage,IReadOnlyList<ActionabilityEvidenceRecord> Records,string ContractVersion,
    string Interpretation="Successful detection or sequence completion is not equivalent to a profitable trade.");
