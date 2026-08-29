namespace PFA_FVG_Scanner.Domain.Research;

public enum TradeJournalMovement { OpenLong=1,CloseLong=2,OpenShort=3,CloseShort=4 }
public enum TradeJournalDirection { Long,Short }

public sealed record TradeJournalExecution(string ExecutionId,string ImportId,string AccountHash,string OrderHash,string SourceEpisodeKey,
    string ProviderSymbol,string InstrumentId,string ContractId,DateTime MovementTimeUtc,TradeJournalMovement Movement,
    int SignedQuantity,decimal Price,decimal? PointContracts,decimal? NetProfit,int SourceRow,string ContentHash);

public sealed record TradeJournalEpisode(string EpisodeId,string ImportId,string AccountHash,string ProviderSymbol,
    string InstrumentId,string ContractId,TradeJournalDirection Direction,DateTime OpenedAtUtc,DateTime ClosedAtUtc,
    int MaximumContracts,int ExecutionCount,int RealizedLegCount,decimal PointContracts,decimal GrossProfit,
    decimal EstimatedCosts,decimal NetProfit,decimal DurationMinutes,string Outcome,string ContentHash);

public sealed record TradeJournalImportManifest(string ImportId,string ImporterVersion,string SourceFileName,
    string SourceContentHash,DateTime ImportedAtUtc,int SourceRows,int ExecutionCount,int EpisodeCount,
    DateTime EarliestExecutionUtc,DateTime LatestExecutionUtc,decimal GrossProfit,decimal EstimatedCosts,
    decimal NetProfit,int Wins,int Losses,decimal WinRate,decimal ProfitFactor,
    IReadOnlyList<string> Instruments,IReadOnlyList<string> Contracts,IReadOnlyList<string> Warnings,
    bool IsBehavioralEvidence=true,bool IsRecommendedStrategy=false,bool CanActivateStrategy=false,
    bool CanRouteToRealBroker=false);

public sealed record TradeJournalPatternMatch(string ObservationId,int ObservationRevision,string ModuleId,
    string PatternType,string PatternDirection,DateTime PatternKnownAtUtc,decimal MinutesBeforeEntry,
    bool DirectionAgrees,string ObservationContentHash);

public sealed record TradeJournalStructuralEventMatch(string EventId,string EventType,string EventDirection,
    DateTime KnownAtUtc,decimal MinutesBeforeEntry,decimal Strength,bool DirectionAgrees,string Evidence);

public sealed record TradeJournalEpisodeAlignment(string EpisodeId,string InstrumentId,string ContractId,
    TradeJournalDirection Direction,DateTime EntryTimeUtc,decimal NetProfit,bool CanonicalBarAvailable,
    string? CanonicalBarId,DateTime? CanonicalBarCloseTimeUtc,IReadOnlyList<TradeJournalPatternMatch> PatternMatches,
    IReadOnlyList<TradeJournalStructuralEventMatch> StructuralEventMatches,string ContentHash);

public sealed record TradeJournalPatternBehaviorMetric(string ModuleId,string PatternType,int MatchedEpisodes,
    int Wins,int Losses,decimal NetProfit,decimal WinRate,decimal ProfitFactor,decimal DirectionAgreementRate);

public sealed record TradeJournalStructuralBehaviorMetric(string EventType,int MatchedEpisodes,int Wins,int Losses,
    decimal NetProfit,decimal WinRate,decimal ProfitFactor,decimal DirectionAgreementRate);

public sealed record TradeJournalDirectionalBehaviorMetric(string SourceKind,string SignalType,
    string DirectionRelationship,int MatchedEpisodes,int Wins,int Losses,decimal NetProfit,
    decimal WinRate,decimal ProfitFactor);

public sealed record TradeJournalAlignmentReport(string ReportId,string AlignmentVersion,string ImportId,
    DateTime CreatedAtUtc,int Episodes,int CanonicalBarAlignedEpisodes,int PatternMatchedEpisodes,
    int StructuralEventMatchedEpisodes,int UnmatchedEpisodes,IReadOnlyList<TradeJournalPatternBehaviorMetric> PatternMetrics,
    IReadOnlyList<TradeJournalStructuralBehaviorMetric> StructuralMetrics,
    IReadOnlyList<TradeJournalDirectionalBehaviorMetric> DirectionalSegments,
    IReadOnlyList<string> Limitations,string ContentHash,bool IsBehavioralEvidence=true,
    bool IsStrategyValidation=false,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);
