namespace PFA_FVG_Scanner.Domain.Sandbox;

public enum ExploratoryPaperCampaignStatus
{
    CompletedBlindReplay,
    AwaitingBlindSamples,
    Tier2ReviewEligible,
    AccumulatingProspectiveEvidence,
    Terminated
}

public sealed record ExploratoryContractVariantResult(int Contracts,decimal NetProfitLoss,
    decimal MaximumFavorableExcursionDollars,decimal MaximumAdverseExcursionDollars);

public sealed record ExploratoryPaperExecution(string ExecutionId,string CampaignId,string CandidateId,
    string SourceSampleId,string ObservationId,string InstrumentId,string? ContractId,string Direction,
    DateTime DecisionTimeUtc,DateTime EntryTimeUtc,decimal RequestedEntryPrice,decimal SimulatedFillPrice,
    decimal StopPrice,decimal TargetPrice,DateTime ExitTimeUtc,decimal ExitPrice,string Outcome,
    decimal GrossR,decimal NetR,decimal MaximumFavorableExcursionR,decimal MaximumAdverseExcursionR,
    long? TimeToMfeMilliseconds,long? TimeToMaeMilliseconds,decimal EntryFrictionTicks,
    decimal EstimatedRoundTripCostTicks,decimal? PrevailingSpreadTicks,decimal? L2DepthImbalance,
    decimal? RollingOneMinuteCvd,decimal? RollingFiveMinuteCvd,decimal? DistanceToSessionVwapTicks,
    string TelemetryResolution,IReadOnlyList<ExploratoryContractVariantResult> ContractVariants,
    string ContentHash,bool TestPartitionWasBlind=true,bool CanActivateStrategy=false,
    bool CanRouteToRealBroker=false);

public sealed record ExploratoryContractMetrics(int Contracts,int Trades,int Wins,decimal WinRate,
    decimal MeanNetR,decimal ProfitFactor,decimal NetProfitLoss,decimal MaximumDrawdownDollars,
    decimal WorstTradeDollars,decimal BestTradeDollars);

public sealed record ExploratoryPaperCampaign(string CampaignId,string CandidateId,string StrategyId,
    string StrategyVersion,string InstrumentId,string SourcePatternTradeRunId,string HypothesisId,
    string Mode,ExploratoryPaperCampaignStatus Status,string Recommendation,DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,int SourceTestSamples,int ResolvedExecutions,
    IReadOnlyList<ExploratoryContractMetrics> ContractMetrics,string ContentHash,
    bool AdmissionUsedTestPartition=false,bool IsStatisticallyValidated=false,
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false,
    string Interpretation="Blind historical replay is genuine unseen evaluation, but prospective market observation is still required before certification.");

public sealed record ExploratoryPaperDashboard(string InstrumentId,int FrozenCandidateVersions,
    int Campaigns,int Executions,IReadOnlyList<ExploratoryPaperCampaign> Results,
    bool HasStartedActualSandboxTesting,bool IsBlindHistoricalReplay=true,
    bool HasProspectiveLiveFeed=false,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record ExploratoryPaperCampaignDetail(ExploratoryPaperCampaign Campaign,
    IReadOnlyList<ExploratoryPaperExecution> Executions);
