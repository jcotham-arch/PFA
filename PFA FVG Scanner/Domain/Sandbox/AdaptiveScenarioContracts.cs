namespace PFA_FVG_Scanner.Domain.Sandbox;

public enum AdaptiveScenarioGenerationStatus
{
    AwaitingDevelopmentEvidence,
    AwaitingNewBlindDays,
    ReadyForBlindReplay
}

public sealed record AdaptiveScenarioSegment(string Timeframe,string TradingDate,int Trades,int Wins,
    decimal WinRate,decimal MeanNetR,decimal ProfitFactor);

public sealed record AdaptiveScenarioChallenger(string ChallengerId,string ParentCandidateId,string MutationType,
    string Rationale,string EntryPolicy,string StopPolicy,string ExitPolicy,decimal TargetR,
    int MaximumHoldingMinutes,string EvaluationState="QueuedForDevelopmentReplay",
    bool HasSeenBlindResults=false,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record AdaptiveScenarioChampion(string CandidateId,string PatternTradeRunId,string HypothesisId,
    string ModuleId,string EntryPolicy,string StopPolicy,string ExitPolicy,decimal TargetR,
    int MaximumHoldingMinutes,int DevelopmentTrades,int DistinctDevelopmentDays,
    IReadOnlyList<string> Timeframes,decimal DevelopmentMeanNetR,decimal DevelopmentProfitFactor,
    IReadOnlyList<AdaptiveScenarioSegment> Segments);

public sealed record AdaptiveScenarioGeneration(string GenerationId,int GenerationNumber,string InstrumentId,
    string PolicyVersion,string SourcePatternTradeRunId,DateTime DevelopmentCutoffUtc,
    DateOnly EarliestNextBlindTradingDate,AdaptiveScenarioGenerationStatus Status,string Interpretation,
    AdaptiveScenarioChampion? Champion,IReadOnlyList<AdaptiveScenarioChallenger> Challengers,
    int MinimumDistinctDevelopmentDays,int MinimumDevelopmentTrades,
    IReadOnlyList<string> RequiredTimeframes,DateTime CreatedAtUtc,string ContentHash,
    bool UsedTestPartitionForSelection=false,bool MutatesFrozenVersion=false,
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record AdaptiveScenarioEvaluation(string EvaluationId,string GenerationId,string ChallengerId,
    string ResearchRunId,string InstrumentId,string ModuleId,int TrainResolved,decimal TrainMeanNetR,
    decimal TrainProfitFactor,int ValidationResolved,decimal ValidationMeanNetR,decimal ValidationProfitFactor,
    string Result,string Interpretation,DateTime EvaluatedAtUtc,string ContentHash,
    bool EvaluatedTestPartition=false,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record AdaptiveScenarioDashboard(string InstrumentId,int Generations,
    AdaptiveScenarioGeneration? Latest,IReadOnlyList<AdaptiveScenarioGeneration> History,
    IReadOnlyList<AdaptiveScenarioEvaluation>? Evaluations=null,
    string LearningRule="Development evidence proposes a new immutable challenger; blind evidence audits it and never rewrites the version under test.",
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);
