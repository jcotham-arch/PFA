using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Governance;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Domain.Strategies;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseNineteenGovernanceTests
{
    [Fact]
    public void CompleteHealthyApprovedEvidenceAuthorizesSandboxOnly()
    {
        var decision=Evaluate();Assert.Equal(GovernanceDecisionOutcome.Authorized,decision.Outcome);Assert.Empty(decision.VetoReasons);Assert.False(decision.CanRouteToRealBroker);Assert.NotNull(decision.PermitExpiresAtUtc);
        AssertVeto(GovernanceVetoReason.NonSandboxDestination,request:Request() with{Destination="Live"});
    }

    [Fact]
    public void MissingOrInvalidInputsDenyByDefault()
    {
        var engine=new GovernanceEngine();Assert.Contains(GovernanceVetoReason.MissingPolicy,engine.Evaluate(Request(),null,Approvals(),[],null,Health(),Risk(),Now).VetoReasons);AssertVeto(GovernanceVetoReason.InvalidPolicy,policy:Policy() with{MaximumOpenRiskDollars=0});Assert.Contains(GovernanceVetoReason.MissingHealth,engine.Evaluate(Request(),Policy(),Approvals(),[],null,null,Risk(),Now).VetoReasons);Assert.Contains(GovernanceVetoReason.MissingRiskSnapshot,engine.Evaluate(Request(),Policy(),Approvals(),[],null,Health(),null,Now).VetoReasons);AssertVeto(GovernanceVetoReason.InstrumentNotAllowed,request:Request() with{InstrumentId="CL"});
        AssertVeto(GovernanceVetoReason.FutureDatedEvidence,request:Request() with{SignalKnownAtUtc=Now.AddSeconds(1)});AssertVeto(GovernanceVetoReason.FutureDatedEvidence,health:Health() with{CapturedAtUtc=Now.AddSeconds(1)});AssertVeto(GovernanceVetoReason.FutureDatedEvidence,risk:Risk() with{CapturedAtUtc=Now.AddSeconds(1)});
    }

    [Fact]
    public void FeedLatencyAccountAndApprovalVetoesAreAllEnforced()
    {
        AssertVeto(GovernanceVetoReason.FeedUnhealthy,health:Health() with{FeedHealthy=false});AssertVeto(GovernanceVetoReason.FeedStale,health:Health() with{FeedStale=true});AssertVeto(GovernanceVetoReason.FeedStale,health:Health() with{LastMarketEventUtc=Now.AddMinutes(-10)});AssertVeto(GovernanceVetoReason.HealthCheckStale,health:Health() with{LastHealthCheckUtc=Now.AddMinutes(-10)});AssertVeto(GovernanceVetoReason.AccountUnhealthy,health:Health() with{AccountHealthy=false});AssertVeto(GovernanceVetoReason.DecisionTooLate,request:Request() with{SignalKnownAtUtc=Now.AddMinutes(-5)});
        AssertVeto(GovernanceVetoReason.AccountApprovalMissing,approvals:Approvals().Where(x=>x.Scope!=GovernanceApprovalScope.Account).ToArray());AssertVeto(GovernanceVetoReason.StrategyApprovalMissing,approvals:Approvals().Where(x=>x.Scope!=GovernanceApprovalScope.StrategyVersion).ToArray());AssertVeto(GovernanceVetoReason.ApprovalExpired,approvals:Approvals().Select(x=>x with{ExpiresAtUtc=Now.AddSeconds(-1)}).ToArray());
    }

    [Fact]
    public void LossDrawdownOpenRiskPositionAndCorrelationLimitsAllVeto()
    {
        AssertVeto(GovernanceVetoReason.DailyLossLimit,risk:Risk() with{DailyProfitLossDollars=-500});AssertVeto(GovernanceVetoReason.DrawdownLimit,risk:Risk() with{CurrentDrawdownDollars=500});AssertVeto(GovernanceVetoReason.OpenRiskLimit,risk:Risk() with{ResultingOpenRiskDollars=1001});AssertVeto(GovernanceVetoReason.PositionLimit,risk:Risk() with{ResultingInstrumentContracts=4});AssertVeto(GovernanceVetoReason.CorrelatedExposureLimit,risk:Risk() with{ResultingCorrelatedRiskDollars=1501});
        var reducing=Risk() with{CurrentInstrumentContracts=3,ResultingInstrumentContracts=2,CurrentOpenRiskDollars=900,ResultingOpenRiskDollars=600,CurrentCorrelatedRiskDollars=900,ResultingCorrelatedRiskDollars=600};Assert.Equal(GovernanceDecisionOutcome.Authorized,Evaluate(risk:reducing).Outcome);
    }

    [Fact]
    public void EmergencyStopAndEverySuspensionScopeVeto()
    {
        AssertVeto(GovernanceVetoReason.GlobalEmergencyStop,emergency:new("E",true,"stop","ops",Now));
        foreach(var suspension in new[]{new GovernanceSuspension("G",GovernanceSuspensionScope.Global,"*","x","ops",Now),new("A",GovernanceSuspensionScope.Account,"A","x","ops",Now),new("S",GovernanceSuspensionScope.StrategyVersion,"S|1","x","ops",Now),new("I",GovernanceSuspensionScope.Instrument,"MES","x","ops",Now)})AssertVeto(GovernanceVetoReason.ScopeSuspended,suspensions:[suspension]);
    }

    [Fact]
    public void PermitsAreBoundShortLivedAndCannotBeForged()
    {
        var authority=new GovernancePermitAuthority();var decision=Evaluate();var permit=authority.Issue(decision);Assert.True(authority.Validate(permit,"A","I","SIG",Now));Assert.False(authority.Validate(permit,"B","I","SIG",Now));Assert.False(authority.Validate(permit with{Signature="NOT-HEX"},"A","I","SIG",Now));Assert.False(authority.Validate(permit,"A","I","SIG",permit.ExpiresAtUtc.AddTicks(1)));Assert.Throws<UnauthorizedAccessException>(()=>authority.Issue(decision with{Outcome=GovernanceDecisionOutcome.Vetoed}));
    }

    [Fact]
    public async Task GovernanceRecordsAreDurableIdempotentImmutableAndReconstructState()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new GovernanceRepository(factory.Database);var policy=Policy();await repository.SavePolicyAsync(policy,Token);await repository.SavePolicyAsync(policy,Token);Assert.Equal(policy.ContentHash(),(await repository.GetEffectivePolicyAsync(Now,Token))!.ContentHash());await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.SavePolicyAsync(policy with{Name="changed"},Token));
        var approval=Approvals()[0];await repository.GrantApprovalAsync(approval,Token);await repository.RevokeApprovalAsync(new(approval.ApprovalId,Now.AddMinutes(1),"ops","revoke"),Token);Assert.True((await repository.GetApprovalsAsync(Token)).Single().Revoked);
        var suspension=new GovernanceSuspension("SUSP",GovernanceSuspensionScope.Account,"A","pause","ops",Now);await repository.SuspendAsync(suspension,Token);await repository.ResumeAsync(new("SUSP",Now.AddMinutes(1),"ops","clear"),Token);Assert.False((await repository.GetSuspensionsAsync(Token)).Single().IsActive);
        await repository.SaveEmergencyStopAsync(new("E1",true,"stop","ops",Now),Token);await repository.SaveEmergencyStopAsync(new("E2",false,"clear","ops",Now.AddMinutes(1)),Token);Assert.False((await repository.GetEmergencyStopAsync(Token))!.IsActive);
        var decision=Evaluate();await repository.SaveDecisionAsync(decision,Token);await repository.SaveDecisionAsync(decision,Token);Assert.Single(await repository.GetDecisionsAsync("A",100,Token));await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repository.SaveDecisionAsync(decision with{DecisionId="LIVE",CanRouteToRealBroker=true},Token));
        var evidence="{\"source\":\"test\"}";var incident=new GovernanceIncident("INC","High","Feed","A","I",Now,"stale feed",evidence,"HASH");await repository.SaveIncidentAsync(incident,Token);Assert.Equal("INC",(await repository.GetIncidentsAsync(10,Token)).Single().IncidentId);
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(Token);await using var command=connection.CreateCommand();command.CommandText="UPDATE GovernanceDecisions SET Outcome='Authorized'";await Assert.ThrowsAsync<SqliteException>(()=>command.ExecuteNonQueryAsync(Token));
    }

    [Fact]
    public async Task GovernedSandboxPersistsDecisionAndCannotBypassEmergencyStop()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var clock=new ManualClock(Now);var authority=new GovernancePermitAuthority();var ledger=new SandboxLedgerRepository(factory.Database);var sandbox=new SandboxService(ledger,new SandboxStateProjector(),new SandboxBrokerSimulator(),new SandboxPortfolioProjector(),new FakeRegistry(),new InstrumentDefinitionRegistry(),clock,authority);await sandbox.CreateAccountAsync("account","A","A",50000,Token);await sandbox.CreateInstanceAsync("instance","A","I","S","1","MES","MESU6",Token);await sandbox.StartInstanceAsync("start","A","I",Token);
        var repository=new GovernanceRepository(factory.Database);await repository.SavePolicyAsync(Policy(),Token);foreach(var approval in Approvals())await repository.GrantApprovalAsync(approval,Token);var governance=new GovernanceService(repository,new GovernanceEngine(),authority,sandbox,new InstrumentDefinitionRegistry(),new GoodHealth(),clock);var governed=new GovernedSandboxService(governance,sandbox);var first=await governed.SubmitSignalAsync("signal","A",Signal("SIG"),Model(),Token);Assert.Equal(GovernanceDecisionOutcome.Authorized,first.Decision.Outcome);Assert.Single(first.State!.Orders);
        await repository.SaveEmergencyStopAsync(new("STOP",true,"operator stop","ops",Now),Token);var blocked=await governed.SubmitSignalAsync("blocked","A",Signal("SIG2"),Model(),Token);Assert.Equal(GovernanceDecisionOutcome.Vetoed,blocked.Decision.Outcome);Assert.Null(blocked.State);Assert.Contains(GovernanceVetoReason.GlobalEmergencyStop,blocked.Decision.VetoReasons);Assert.Equal(2,(await repository.GetDecisionsAsync("A",100,Token)).Count);Assert.Single((await sandbox.GetAccountAsync("A",Token)).Orders);
    }

    private static readonly DateTime Now=new(2026,8,27,14,0,0,DateTimeKind.Utc);private static CancellationToken Token=>TestContext.Current.CancellationToken;
    private static GovernanceDecision Evaluate(GovernanceActionRequest? request=null,GovernancePolicy? policy=default,IReadOnlyList<GovernanceApproval>? approvals=null,IReadOnlyList<GovernanceSuspension>? suspensions=null,GovernanceEmergencyStop? emergency=null,GovernanceHealthSnapshot? health=default,GovernanceRiskSnapshot? risk=default)
    {policy=policy==default?Policy():policy;health=health==default?Health():health;risk=risk==default?Risk():risk;return new GovernanceEngine().Evaluate(request??Request(),policy,approvals??Approvals(),suspensions??[],emergency,health,risk,Now);}
    private static void AssertVeto(GovernanceVetoReason reason,GovernanceActionRequest? request=null,GovernancePolicy? policy=default,IReadOnlyList<GovernanceApproval>? approvals=null,IReadOnlyList<GovernanceSuspension>? suspensions=null,GovernanceEmergencyStop? emergency=null,GovernanceHealthSnapshot? health=default,GovernanceRiskSnapshot? risk=default)=>Assert.Contains(reason,Evaluate(request,policy,approvals,suspensions,emergency,health,risk).VetoReasons);
    private static GovernancePolicy Policy()=>new("POLICY","1","Conservative",500,500,1000,3,1500,300,30,30,30,true,true,new HashSet<string>{"MES"},new Dictionary<string,string>{{"MES","EQUITY-INDEX"}},Now.AddDays(-1),null,"ops",Now.AddDays(-1));
    private static GovernanceActionRequest Request()=>new("REQ","Sandbox","A","I","S","1","MES","SIG",Now,Now,"EVIDENCE");
    private static GovernanceHealthSnapshot Health()=>new(true,false,Now,Now,true,"healthy",Now);
    private static GovernanceRiskSnapshot Risk()=>new(0,0,0,300,0,1,300,0,300,"EQUITY-INDEX",Now);
    private static GovernanceApproval[] Approvals()=>[new("AA",GovernanceApprovalScope.Account,"A","POLICY","1",Now.AddMinutes(-1),Now.AddDays(1),"ops","approved"),new("AS",GovernanceApprovalScope.StrategyVersion,"S|1","POLICY","1",Now.AddMinutes(-1),Now.AddDays(1),"ops","approved")];
    private static SandboxSignal Signal(string id)=>new(id,"I",SandboxOrderSide.Buy,SandboxOrderType.Market,1,null,null,Now,"test",["E"]);private static SandboxFillModel Model()=>new("FM",0,0,0,true);
    private sealed class ManualClock(DateTime now):ISandboxClock{public DateTime UtcNow{get;set;}=now;}
    private sealed class GoodHealth:IGovernanceHealthProvider{public GovernanceHealthSnapshot Capture(bool accountHealthy,string reason,DateTime now)=>new(true,false,now,now,accountHealthy,reason,now);}
    private sealed class FakeRegistry:IStrategyRegistry
    {public Task<StrategyRegistryEntry?> FindAsync(string id,string version,CancellationToken token=default)=>Task.FromResult<StrategyRegistryEntry?>(new(Definition(id,version),"HASH",StrategyRegistryStatus.ValidationComplete,Now,"test"));public Task<IReadOnlyList<StrategyRegistryEntry>> GetAllAsync(CancellationToken token=default)=>Task.FromResult<IReadOnlyList<StrategyRegistryEntry>>([]);public Task<StrategyRegistryEntry> RegisterAsync(ImmutableStrategyDefinition definition,CancellationToken token=default)=>throw new NotSupportedException();public Task<StrategyRegistryEntry> TransitionAsync(string id,string version,StrategyRegistryStatus target,string reason,string actor,CancellationToken token=default)=>throw new NotSupportedException();private static ImmutableStrategyDefinition Definition(string id,string version)=>new(id,version,"F","Test","Research","Both","{}","{}","{}","{}","{}","{}",["MES"],[],[],[],new("D","F","P","Q","S","E","R","SESSION","CONTRACT"),"DISC","VALID","test",Now);}
}
