using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Certification;

public enum PropDrawdownMode { Static,EndOfDayTrailing,IntradayTrailing }
public enum PropAutomationMode { ResearchOnly,AlertOnly,ManualApprovalRequired,ManualOriginWithCopying,AutomatedExecutionPermitted,Unsupported }
public enum PropAccountCertificationStatus { Active,PassedChallenge,PayoutEligible,Failed,OperationallyInvalid }
public enum PropRuleViolationCode { DailyLoss,MaximumLoss,TrailingDrawdown,MaximumContracts,Consistency,MinimumTradingDays,NewsRestriction,SessionFlatten,AutomationProhibited,StaleRulePack,OperationalData }

public sealed record PropFirmRulePack(
    string FirmId,string ProgramId,string AccountSizeId,string RuleVersion,DateTime EffectiveFromUtc,DateTime? EffectiveToUtc,
    decimal StartingBalance,decimal ProfitTarget,decimal MaximumLoss,PropDrawdownMode DrawdownMode,
    decimal? DailyLossLimit,int MaximumContracts,decimal MicroToMiniRatio,decimal MaximumBestDayProfitPercent,
    int MinimumTradingDays,int MinimumProfitableDays,decimal MinimumProfitableDayDollars,
    decimal PayoutMinimumBalance,decimal PayoutSafetyBuffer,int MinimumDaysBeforePayout,
    bool FlattenAtSessionEnd,bool NewsEntriesAllowed,PropAutomationMode AutomationMode,
    string SourceReference,string SourceContentHash,bool IsOfficiallyVerified,bool CanRouteToRealBroker=false)
{
    public void Validate(){if(string.IsNullOrWhiteSpace(FirmId)||string.IsNullOrWhiteSpace(ProgramId)||string.IsNullOrWhiteSpace(RuleVersion)||StartingBalance<=0||ProfitTarget<=0||MaximumLoss<=0||MaximumContracts<1||MicroToMiniRatio<=0||MaximumBestDayProfitPercent is <=0 or >100||MinimumTradingDays<0||MinimumProfitableDays<0||MinimumDaysBeforePayout<0||EffectiveToUtc<=EffectiveFromUtc||CanRouteToRealBroker)throw new ArgumentException("Prop-firm rule pack is invalid or unsafe.");}
    public string ContentHash(){Validate();return CertificationHash.Of(JsonSerializer.Serialize(this));}
}

public sealed record PropTradingDayResult(
    DateOnly TradingDate,decimal StartBalance,decimal EndBalance,decimal EndEquity,decimal IntradayHighEquity,
    decimal IntradayLowEquity,decimal GrossProfitLoss,decimal Commissions,int MaximumSimultaneousContracts,
    int ClosedTrades,bool EnteredDuringRestrictedNews,bool HeldPositionAtRequiredFlatten,
    PropAutomationMode ExecutionMode,bool OperationalDataComplete,string SourceReference,DateTime KnownAtUtc);

public sealed record PropRuleViolation(PropRuleViolationCode Code,DateOnly TradingDate,string Detail,bool Terminal);
public sealed record PropAccountCertificationResult(
    string ResultId,string AccountId,string RulePackHash,PropAccountCertificationStatus Status,
    decimal Balance,decimal NetProfit,decimal HighWaterMark,decimal ActiveDrawdownFloor,
    int TradingDays,int ProfitableDays,decimal BestDayProfit,decimal BestDayPercentOfProfit,
    IReadOnlyList<PropRuleViolation> Violations,bool PassedProfitTarget,bool PayoutRulesSatisfied,
    DateTime EvaluatedAtUtc,string ContentHash,bool CanRouteToRealBroker=false);

public static class PropFirmRulePackCatalog
{
    public static PropFirmRulePack PfaConservative50K(DateTime effectiveFromUtc)=>new(
        "PFA-INTERNAL","CERTIFICATION","50K","1.0.0",effectiveFromUtc,null,50000m,3000m,2000m,
        PropDrawdownMode.IntradayTrailing,1000m,1,10m,40m,5,3,100m,52600m,400m,10,
        true,false,PropAutomationMode.AutomatedExecutionPermitted,
        "internal-design:PFA-conservative-50K-1.0.0",CertificationHash.Of("PFA-conservative-50K-1.0.0"),true,false);
}
