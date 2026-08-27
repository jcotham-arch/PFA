using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Governance;

public enum GovernanceDecisionOutcome { Authorized,Vetoed }
public enum GovernanceApprovalScope { Account,StrategyVersion }
public enum GovernanceSuspensionScope { Global,Account,StrategyVersion,Instrument }
public enum GovernanceVetoReason
{
    MissingPolicy,InvalidPolicy,MissingHealth,FeedUnhealthy,FeedStale,HealthCheckStale,DecisionTooLate,FutureDatedEvidence,
    AccountUnhealthy,MissingRiskSnapshot,AccountApprovalMissing,StrategyApprovalMissing,ApprovalExpired,
    GlobalEmergencyStop,ScopeSuspended,DailyLossLimit,DrawdownLimit,OpenRiskLimit,PositionLimit,
    CorrelatedExposureLimit,InstrumentNotAllowed,StrategyIdentityMismatch,NonSandboxDestination
}

public sealed record GovernancePolicy(
    string PolicyId,string PolicyVersion,string Name,decimal MaximumDailyLossDollars,decimal MaximumDrawdownDollars,
    decimal MaximumOpenRiskDollars,int MaximumContractsPerInstrument,decimal MaximumCorrelatedRiskDollars,
    decimal FallbackRiskPerContractDollars,int MaximumFeedAgeSeconds,int MaximumHealthCheckAgeSeconds,
    int MaximumDecisionLatencySeconds,bool RequireAccountApproval,bool RequireStrategyApproval,
    HashSet<string> AllowedInstrumentIds,Dictionary<string,string> CorrelationGroups,
    DateTime EffectiveFromUtc,DateTime? EffectiveToUtc,string CreatedBy,DateTime CreatedAtUtc)
{
    public string ContentHash()=>GovernanceHash.Of(JsonSerializer.Serialize(new{PolicyId,PolicyVersion,Name,MaximumDailyLossDollars,MaximumDrawdownDollars,MaximumOpenRiskDollars,MaximumContractsPerInstrument,MaximumCorrelatedRiskDollars,FallbackRiskPerContractDollars,MaximumFeedAgeSeconds,MaximumHealthCheckAgeSeconds,MaximumDecisionLatencySeconds,RequireAccountApproval,RequireStrategyApproval,AllowedInstrumentIds=AllowedInstrumentIds.OrderBy(x=>x,StringComparer.Ordinal).ToArray(),CorrelationGroups=CorrelationGroups.OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray(),EffectiveFromUtc,EffectiveToUtc,CreatedBy}));
    public bool IsValid()=>!string.IsNullOrWhiteSpace(PolicyId)&&!string.IsNullOrWhiteSpace(PolicyVersion)&&MaximumDailyLossDollars>0&&MaximumDrawdownDollars>0&&MaximumOpenRiskDollars>0&&MaximumContractsPerInstrument>0&&MaximumCorrelatedRiskDollars>0&&FallbackRiskPerContractDollars>0&&MaximumFeedAgeSeconds>0&&MaximumHealthCheckAgeSeconds>0&&MaximumDecisionLatencySeconds>0;
}

public sealed record GovernanceApproval(string ApprovalId,GovernanceApprovalScope Scope,string ScopeId,string PolicyId,string PolicyVersion,DateTime GrantedAtUtc,DateTime ExpiresAtUtc,string GrantedBy,string Reason,bool Revoked=false,DateTime? RevokedAtUtc=null,string? RevokedBy=null,string? RevocationReason=null)
{public bool IsActiveAt(DateTime now)=>!Revoked&&GrantedAtUtc<=now&&ExpiresAtUtc>=now;}
public sealed record GovernanceSuspension(string SuspensionId,GovernanceSuspensionScope Scope,string ScopeId,string Reason,string Actor,DateTime SuspendedAtUtc,bool IsActive=true,DateTime? ResumedAtUtc=null,string? ResumedBy=null,string? ResumeReason=null);
public sealed record GovernanceEmergencyStop(string EmergencyStopId,bool IsActive,string Reason,string Actor,DateTime OccurredAtUtc);
public sealed record GovernanceHealthSnapshot(bool FeedHealthy,bool FeedStale,DateTime? LastMarketEventUtc,DateTime? LastHealthCheckUtc,bool AccountHealthy,string AccountHealthReason,DateTime CapturedAtUtc);
public sealed record GovernanceRiskSnapshot(decimal DailyProfitLossDollars,decimal CurrentDrawdownDollars,decimal CurrentOpenRiskDollars,decimal ProposedRiskDollars,int CurrentInstrumentContracts,int ResultingInstrumentContracts,decimal ResultingOpenRiskDollars,decimal CurrentCorrelatedRiskDollars,decimal ResultingCorrelatedRiskDollars,string CorrelationGroup,DateTime CapturedAtUtc);
public sealed record GovernanceActionRequest(string RequestId,string Destination,string AccountId,string InstanceId,string StrategyId,string StrategyVersion,string InstrumentId,string SignalId,DateTime SignalKnownAtUtc,DateTime RequestedAtUtc,string EvidenceReference);
public sealed record GovernanceDecision(string DecisionId,string RequestId,GovernanceDecisionOutcome Outcome,IReadOnlyList<GovernanceVetoReason> VetoReasons,string PolicyId,string PolicyVersion,string AccountId,string InstanceId,string SignalId,DateTime DecidedAtUtc,DateTime? PermitExpiresAtUtc,string EvidenceJson,string ContentHash,bool CanRouteToRealBroker=false);

public sealed class GovernanceEngine
{
    public const string Version="1.0.0";
    public GovernanceDecision Evaluate(GovernanceActionRequest request,GovernancePolicy? policy,IReadOnlyList<GovernanceApproval> approvals,IReadOnlyList<GovernanceSuspension> suspensions,GovernanceEmergencyStop? emergency,GovernanceHealthSnapshot? health,GovernanceRiskSnapshot? risk,DateTime nowUtc)
    {
        var now=Utc(nowUtc);var vetoes=new HashSet<GovernanceVetoReason>();
        if(!string.Equals(request.Destination,"Sandbox",StringComparison.Ordinal))vetoes.Add(GovernanceVetoReason.NonSandboxDestination);
        if(policy is null)vetoes.Add(GovernanceVetoReason.MissingPolicy);else if(!policy.IsValid()||now<policy.EffectiveFromUtc||(policy.EffectiveToUtc.HasValue&&now>policy.EffectiveToUtc))vetoes.Add(GovernanceVetoReason.InvalidPolicy);
        if(emergency?.IsActive==true)vetoes.Add(GovernanceVetoReason.GlobalEmergencyStop);
        if(suspensions.Any(x=>x.IsActive&&Matches(x,request)))vetoes.Add(GovernanceVetoReason.ScopeSuspended);
        if(request.SignalKnownAtUtc>now||request.RequestedAtUtc>now)vetoes.Add(GovernanceVetoReason.FutureDatedEvidence);
        if(health is null)vetoes.Add(GovernanceVetoReason.MissingHealth);else if(policy is not null){if(health.CapturedAtUtc>now||health.LastMarketEventUtc>now||health.LastHealthCheckUtc>now)vetoes.Add(GovernanceVetoReason.FutureDatedEvidence);if(!health.FeedHealthy)vetoes.Add(GovernanceVetoReason.FeedUnhealthy);if(health.FeedStale||!health.LastMarketEventUtc.HasValue||now-health.LastMarketEventUtc.Value>TimeSpan.FromSeconds(policy.MaximumFeedAgeSeconds))vetoes.Add(GovernanceVetoReason.FeedStale);if(!health.LastHealthCheckUtc.HasValue||now-health.LastHealthCheckUtc.Value>TimeSpan.FromSeconds(policy.MaximumHealthCheckAgeSeconds))vetoes.Add(GovernanceVetoReason.HealthCheckStale);if(!health.AccountHealthy)vetoes.Add(GovernanceVetoReason.AccountUnhealthy);if(now-request.SignalKnownAtUtc>TimeSpan.FromSeconds(policy.MaximumDecisionLatencySeconds))vetoes.Add(GovernanceVetoReason.DecisionTooLate);}
        if(policy is not null){if(!policy.AllowedInstrumentIds.Contains(request.InstrumentId))vetoes.Add(GovernanceVetoReason.InstrumentNotAllowed);if(policy.RequireAccountApproval)Approval(approvals,GovernanceApprovalScope.Account,request.AccountId,policy,now,vetoes,GovernanceVetoReason.AccountApprovalMissing);if(policy.RequireStrategyApproval)Approval(approvals,GovernanceApprovalScope.StrategyVersion,$"{request.StrategyId}|{request.StrategyVersion}",policy,now,vetoes,GovernanceVetoReason.StrategyApprovalMissing);}
        if(risk is null)vetoes.Add(GovernanceVetoReason.MissingRiskSnapshot);else if(policy is not null){if(risk.CapturedAtUtc>now)vetoes.Add(GovernanceVetoReason.FutureDatedEvidence);if(risk.DailyProfitLossDollars<=-policy.MaximumDailyLossDollars)vetoes.Add(GovernanceVetoReason.DailyLossLimit);if(risk.CurrentDrawdownDollars>=policy.MaximumDrawdownDollars)vetoes.Add(GovernanceVetoReason.DrawdownLimit);if(risk.ResultingOpenRiskDollars>policy.MaximumOpenRiskDollars)vetoes.Add(GovernanceVetoReason.OpenRiskLimit);if(risk.ResultingInstrumentContracts>policy.MaximumContractsPerInstrument)vetoes.Add(GovernanceVetoReason.PositionLimit);if(risk.ResultingCorrelatedRiskDollars>policy.MaximumCorrelatedRiskDollars)vetoes.Add(GovernanceVetoReason.CorrelatedExposureLimit);}
        var reasons=vetoes.OrderBy(x=>x).ToArray();var outcome=reasons.Length==0?GovernanceDecisionOutcome.Authorized:GovernanceDecisionOutcome.Vetoed;var policyId=policy?.PolicyId??"NONE";var version=policy?.PolicyVersion??"NONE";DateTime? expiry=outcome==GovernanceDecisionOutcome.Authorized?now.AddSeconds(Math.Min(30,policy!.MaximumDecisionLatencySeconds)):null;var evidence=JsonSerializer.Serialize(new{EngineVersion=Version,request,policyHash=policy?.ContentHash(),approvals=approvals.Select(x=>x.ApprovalId).OrderBy(x=>x).ToArray(),suspensions=suspensions.Where(x=>x.IsActive).Select(x=>x.SuspensionId).OrderBy(x=>x).ToArray(),emergency,health,risk});var hash=GovernanceHash.Of(JsonSerializer.Serialize(new{request.RequestId,outcome,reasons,policyId,version,request.AccountId,request.InstanceId,request.SignalId,now,expiry,evidence,CanRouteToRealBroker=false}));return new($"GVD-{hash[..32]}",request.RequestId,outcome,reasons,policyId,version,request.AccountId,request.InstanceId,request.SignalId,now,expiry,evidence,hash,false);
    }
    private static void Approval(IReadOnlyList<GovernanceApproval> approvals,GovernanceApprovalScope scope,string id,GovernancePolicy policy,DateTime now,ISet<GovernanceVetoReason> vetoes,GovernanceVetoReason missing){var relevant=approvals.Where(x=>x.Scope==scope&&x.ScopeId==id&&x.PolicyId==policy.PolicyId&&x.PolicyVersion==policy.PolicyVersion).ToArray();if(relevant.Length==0)vetoes.Add(missing);else if(!relevant.Any(x=>x.IsActiveAt(now)))vetoes.Add(GovernanceVetoReason.ApprovalExpired);}
    private static bool Matches(GovernanceSuspension suspension,GovernanceActionRequest request)=>suspension.Scope switch{GovernanceSuspensionScope.Global=>true,GovernanceSuspensionScope.Account=>suspension.ScopeId==request.AccountId,GovernanceSuspensionScope.StrategyVersion=>suspension.ScopeId==$"{request.StrategyId}|{request.StrategyVersion}",GovernanceSuspensionScope.Instrument=>suspension.ScopeId==request.InstrumentId,_=>true};
    private static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(value,DateTimeKind.Utc):value.ToUniversalTime();
}

internal static class GovernanceHash{internal static string Of(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));}

public sealed record GovernancePermit(string DecisionId,string AccountId,string InstanceId,string SignalId,DateTime ExpiresAtUtc,string Signature);
public interface IGovernancePermitValidator{bool Validate(GovernancePermit permit,string accountId,string instanceId,string signalId,DateTime nowUtc);}
