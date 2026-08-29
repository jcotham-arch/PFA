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
