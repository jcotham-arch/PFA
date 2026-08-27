using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Domain.Sandbox;

namespace PFA_FVG_Scanner.Domain.Certification;

public enum CertificationOrderState { Accepted,Working,Triggered,PartiallyFilled,Filled,Cancelled,Rejected,Expired,ReconciliationHold }
public enum CertificationRejectReason { None,VenueUnavailable,StaleMarket,InvalidPrice,InsufficientLiquidity,RiskRejected,UnsupportedOrder,DuplicateClientOrderId }
public enum ReconciliationSeverity { Information,Warning,Critical }

public sealed record ExecutionRealismProfile(
    string ProfileId,string Version,int Seed,int BaseLatencyMilliseconds,int JitterMilliseconds,
    int MaximumMarketAgeMilliseconds,decimal SpreadTicks,decimal BaseSlippageTicks,
    decimal VolatilitySlippageFactor,decimal QuantityImpactTicks,decimal MaximumParticipationRate,
    decimal QueueAheadContracts,decimal TouchFillProbability,decimal CommissionPerContract,
    bool AllowPriceImprovement,bool RejectMarketOrdersWithoutQuote,bool CanRouteToRealBroker=false)
{
    public void Validate(){if(string.IsNullOrWhiteSpace(ProfileId)||string.IsNullOrWhiteSpace(Version)||BaseLatencyMilliseconds<0||JitterMilliseconds<0||MaximumMarketAgeMilliseconds<1||SpreadTicks<0||BaseSlippageTicks<0||VolatilitySlippageFactor<0||QuantityImpactTicks<0||MaximumParticipationRate is <=0 or >1||QueueAheadContracts<0||TouchFillProbability is <0 or >1||CommissionPerContract<0||CanRouteToRealBroker)throw new ArgumentException("Execution realism profile is invalid or unsafe.");}
    public string ContentHash()=>CertificationHash.Of(JsonSerializer.Serialize(this));
}

public sealed record CertificationOrderRequest(
    string ClientOrderId,string AccountId,string StrategyId,string StrategyVersion,string InstrumentId,string ContractId,
    SandboxOrderSide Side,SandboxOrderType Type,int Quantity,decimal? LimitPrice,decimal? StopPrice,
    DateTime SubmittedAtUtc,string SignalReference,string GovernanceDecisionReference);

public sealed record CertificationMarketEvent(
    string EventId,string InstrumentId,string ContractId,long Sequence,DateTime EventTimeUtc,DateTime KnownAtUtc,
    decimal? Bid,decimal? Ask,int BidSize,int AskSize,decimal Last,int LastSize,
    decimal ShortHorizonVolatilityTicks,bool VenueAvailable,string DataRevision);

public sealed record CertificationFill(
    string FillId,string ClientOrderId,int Quantity,decimal Price,decimal Commission,decimal SlippageTicks,
    DateTime FilledAtUtc,string MarketEventId,string DataRevision,string ProfileHash);

public sealed record CertificationOrderResult(
    string ClientOrderId,CertificationOrderState State,CertificationRejectReason RejectReason,int FilledQuantity,
    int RemainingQuantity,DateTime EligibleAtUtc,IReadOnlyList<CertificationFill> Fills,
    IReadOnlyList<string> AuditReasons,string ProfileHash,bool CanRouteToRealBroker=false);

public sealed record VenueAccountSnapshot(string SnapshotId,string AccountId,DateTime KnownAtUtc,
    IReadOnlyDictionary<string,int> SignedPositions,IReadOnlySet<string> WorkingClientOrderIds,decimal CashBalance);
public sealed record InternalAccountSnapshot(string SnapshotId,string AccountId,DateTime AsOfUtc,
    IReadOnlyDictionary<string,int> SignedPositions,IReadOnlySet<string> WorkingClientOrderIds,decimal CashBalance);
public sealed record ReconciliationBreak(string Category,string Key,string Expected,string Actual,ReconciliationSeverity Severity);
public sealed record ReconciliationReport(string ReportId,string AccountId,DateTime ReconciledAtUtc,
    IReadOnlyList<ReconciliationBreak> Breaks,bool TradingHeld,string ContentHash,bool CanRouteToRealBroker=false);

internal static class CertificationHash
{internal static string Of(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));}
