using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Governance;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.MarketData;

namespace PFA_FVG_Scanner.Services;

public sealed class GovernancePermitAuthority:IGovernancePermitValidator
{
    private readonly byte[] _key=RandomNumberGenerator.GetBytes(32);
    public GovernancePermit Issue(GovernanceDecision decision)
    {if(decision.Outcome!=GovernanceDecisionOutcome.Authorized||!decision.PermitExpiresAtUtc.HasValue||decision.CanRouteToRealBroker)throw new UnauthorizedAccessException("Only an authorized sandbox decision can issue a permit.");var signature=Sign(decision.DecisionId,decision.AccountId,decision.InstanceId,decision.SignalId,decision.PermitExpiresAtUtc.Value);return new(decision.DecisionId,decision.AccountId,decision.InstanceId,decision.SignalId,decision.PermitExpiresAtUtc.Value,signature);}
    public bool Validate(GovernancePermit permit,string accountId,string instanceId,string signalId,DateTime nowUtc)
    {if(permit.AccountId!=accountId||permit.InstanceId!=instanceId||permit.SignalId!=signalId||permit.ExpiresAtUtc<nowUtc)return false;try{var expected=Sign(permit.DecisionId,permit.AccountId,permit.InstanceId,permit.SignalId,permit.ExpiresAtUtc);return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected),Convert.FromHexString(permit.Signature));}catch(FormatException){return false;}}
    private string Sign(string decision,string account,string instance,string signal,DateTime expires)=>Convert.ToHexString(HMACSHA256.HashData(_key,Encoding.UTF8.GetBytes($"{decision}|{account}|{instance}|{signal}|{expires:O}")));
}

public sealed record GovernanceAuthorizationResult(GovernanceDecision Decision,GovernancePermit? Permit);
public sealed record GovernedSandboxSubmission(GovernanceDecision Decision,SandboxAccountState? State);

public interface IGovernanceHealthProvider{GovernanceHealthSnapshot Capture(bool accountHealthy,string accountHealthReason,DateTime nowUtc);}
public sealed class WatchdogGovernanceHealthProvider:IGovernanceHealthProvider
{
    private readonly MarketDataWatchdogService _watchdog;private readonly IMarketDataProvider _provider;public WatchdogGovernanceHealthProvider(MarketDataWatchdogService watchdog,IMarketDataProvider provider){_watchdog=watchdog;_provider=provider;}
    public GovernanceHealthSnapshot Capture(bool accountHealthy,string accountHealthReason,DateTime nowUtc)=>new(_watchdog.IsFeedHealthy,_watchdog.IsFeedStale,_provider.ConnectionState.LastCandleReceivedUtc,_watchdog.LastHealthCheckUtc,accountHealthy,accountHealthReason,nowUtc);
}

public sealed class GovernanceService
{
    private readonly GovernanceRepository _repository;private readonly GovernanceEngine _engine;private readonly GovernancePermitAuthority _permits;private readonly SandboxService _sandbox;private readonly IInstrumentDefinitionRegistry _instruments;private readonly IGovernanceHealthProvider _health;private readonly ISandboxClock _clock;
    public GovernanceService(GovernanceRepository repository,GovernanceEngine engine,GovernancePermitAuthority permits,SandboxService sandbox,IInstrumentDefinitionRegistry instruments,IGovernanceHealthProvider health,ISandboxClock clock){_repository=repository;_engine=engine;_permits=permits;_sandbox=sandbox;_instruments=instruments;_health=health;_clock=clock;}
    public async Task<GovernanceAuthorizationResult> AuthorizeAsync(string accountId,SandboxSignal signal,CancellationToken token=default)
    {
        var state=await _sandbox.GetAccountAsync(accountId,token);if(!state.Instances.TryGetValue(signal.InstanceId,out var instance))throw new KeyNotFoundException("Sandbox instance was not found.");var now=_clock.UtcNow;var policy=await _repository.GetEffectivePolicyAsync(now,token);var approvals=await _repository.GetApprovalsAsync(token);var suspensions=await _repository.GetSuspensionsAsync(token);var emergency=await _repository.GetEmergencyStopAsync(token);GovernanceRiskSnapshot? risk=null;
        if(policy is not null){var definition=_instruments.GetAll().Single(x=>x.InstrumentId==instance.InstrumentId);var currentSigned=state.Positions.Values.Where(x=>x.InstrumentId==instance.InstrumentId).Sum(x=>x.SignedQuantity);var currentInstrument=Math.Abs(currentSigned);var signedProposal=signal.Side==SandboxOrderSide.Buy?signal.Quantity:-signal.Quantity;var resultingInstrument=Math.Abs(currentSigned+signedProposal);var openRisk=state.Positions.Values.Sum(x=>Math.Abs(x.SignedQuantity)*policy.FallbackRiskPerContractDollars);var resultingOpen=openRisk-currentInstrument*policy.FallbackRiskPerContractDollars+resultingInstrument*policy.FallbackRiskPerContractDollars;var group=policy.CorrelationGroups.GetValueOrDefault(instance.InstrumentId,"UNMAPPED");var correlated=state.Positions.Values.Where(x=>policy.CorrelationGroups.GetValueOrDefault(x.InstrumentId,"UNMAPPED")==group).Sum(x=>Math.Abs(x.SignedQuantity)*policy.FallbackRiskPerContractDollars);var resultingCorrelated=correlated-currentInstrument*policy.FallbackRiskPerContractDollars+resultingInstrument*policy.FallbackRiskPerContractDollars;var proposed=signal.LimitPrice.HasValue&&signal.StopPrice.HasValue?Math.Abs(signal.LimitPrice.Value-signal.StopPrice.Value)*definition.PointValue*signal.Quantity:policy.FallbackRiskPerContractDollars*signal.Quantity;var today=DateOnly.FromDateTime(now);var dailyGross=state.Trades.Where(x=>DateOnly.FromDateTime(x.ClosedAtUtc)==today).Sum(x=>x.GrossProfitLoss);var dailyCommission=state.Fills.Where(x=>DateOnly.FromDateTime(x.FilledAtUtc)==today).Sum(x=>x.Commission);risk=new(dailyGross-dailyCommission,state.Performance.MaximumDrawdown,openRisk,proposed,currentInstrument,resultingInstrument,resultingOpen,correlated,resultingCorrelated,group,now);}
        var healthy=state.Performance.CashBalance>0;var health=_health.Capture(healthy,healthy?"Account cash is positive.":"Account cash is depleted.",now);var request=new GovernanceActionRequest($"GVR-{Guid.NewGuid():N}","Sandbox",accountId,instance.InstanceId,instance.StrategyId,instance.StrategyVersion,instance.InstrumentId,signal.SignalId,signal.KnownAtUtc,now,signal.EvidenceReferences.FirstOrDefault()??"NONE");var decision=_engine.Evaluate(request,policy,approvals,suspensions,emergency,health,risk,now);await _repository.SaveDecisionAsync(decision,token);return new(decision,decision.Outcome==GovernanceDecisionOutcome.Authorized?_permits.Issue(decision):null);
    }
}

public sealed class GovernedSandboxService
{
    private readonly GovernanceService _governance;private readonly SandboxService _sandbox;public GovernedSandboxService(GovernanceService governance,SandboxService sandbox){_governance=governance;_sandbox=sandbox;}
    public async Task<GovernedSandboxSubmission> SubmitSignalAsync(string commandId,string accountId,SandboxSignal signal,SandboxFillModel model,CancellationToken token=default)
    {var authorization=await _governance.AuthorizeAsync(accountId,signal,token);if(authorization.Permit is null)return new(authorization.Decision,null);var state=await _sandbox.SubmitSignalAsync(commandId,accountId,signal,model,authorization.Permit,token);return new(authorization.Decision,state);}
}
