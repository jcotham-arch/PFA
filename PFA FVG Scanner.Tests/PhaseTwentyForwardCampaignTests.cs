using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Forward;
using PFA_FVG_Scanner.Domain.Governance;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Domain.Strategies;
using PFA_FVG_Scanner.Domain.Validation;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseTwentyForwardCampaignTests
{
    [Fact]
    public void ComparatorSeparatesAccumulationDegradationAndOperationalFailure()
    {
        var comparator=new ForwardExpectationComparator();var campaign=Campaign();var stable=comparator.Compare(campaign,[Snapshot(trades:10,wins:6,net:500,drawdown:2,coverage:100)],Now);Assert.Equal(ForwardComparisonStatus.Stable,stable.Status);Assert.False(stable.CanPromoteStrategy);
        Assert.Equal(ForwardComparisonStatus.Accumulating,comparator.Compare(campaign,[Snapshot(trades:2,wins:2,net:100,drawdown:1,coverage:100)],Now).Status);
        var expectancy=comparator.Compare(campaign,[Snapshot(trades:10,wins:6,net:100,drawdown:2,coverage:100)],Now);Assert.Equal(ForwardSuspensionReason.ExpectancyDegradation,expectancy.SuspensionReason);
        var winRate=comparator.Compare(campaign,[Snapshot(trades:10,wins:1,net:500,drawdown:2,coverage:100)],Now);Assert.Equal(ForwardSuspensionReason.WinRateDegradation,winRate.SuspensionReason);
        var drawdown=comparator.Compare(campaign,[Snapshot(trades:10,wins:6,net:500,drawdown:5,coverage:100)],Now);Assert.Equal(ForwardSuspensionReason.DrawdownDegradation,drawdown.SuspensionReason);
        var operational=comparator.Compare(campaign,[Snapshot(trades:10,wins:0,net:-1000,drawdown:20,coverage:20)],Now);Assert.Equal(ForwardComparisonStatus.OperationallyInvalid,operational.Status);Assert.Equal(ForwardSuspensionReason.FeedCoverage,operational.SuspensionReason);Assert.Contains("not classified",operational.Summary);
    }

    [Fact]
    public void ComparatorRejectsFutureKnownOrOpenSessionSnapshots()
    {
        var comparator=new ForwardExpectationComparator();Assert.Throws<InvalidOperationException>(()=>comparator.Compare(Campaign(),[Snapshot() with{KnownAtUtc=Now.AddSeconds(1)}],Now));var result=comparator.Compare(Campaign(),[Snapshot() with{SessionClosed=false}],Now);Assert.Equal(ForwardComparisonStatus.OperationallyInvalid,result.Status);
    }

    [Fact]
    public async Task HealthyClosedDayAccumulatesAndRecoversAcrossRestart()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var setup=await Setup(factory.Database,Now.AddDays(-1),"EXPECT-1",43200);var campaign=await setup.Service.CreateAsync("A","I",Expectation("EXPECT-1",43200),"ops",Token);await setup.Service.StartAsync(campaign.CampaignId,"ops","start",Token);
        await setup.Service.CaptureHealthAsync(campaign.CampaignId,Token);setup.Clock.UtcNow=Now.AddHours(-12);await setup.Service.CaptureHealthAsync(campaign.CampaignId,Token);setup.Clock.UtcNow=Now;var capture=await setup.Service.CaptureClosedDayAsync(campaign.CampaignId,DateOnly.FromDateTime(Now.AddDays(-1)),Token);
        Assert.Equal(100,capture.Snapshot.OperationalCoveragePercent);Assert.Equal(ForwardComparisonStatus.Accumulating,capture.Comparison!.Status);Assert.False(capture.SuspendedAutomatically);var restarted=new ForwardCampaignRepository(factory.Database);var recovered=await restarted.FindAsync(campaign.CampaignId,Token);Assert.Equal(ForwardCampaignStatus.Running,recovered!.Status);Assert.NotNull(recovered.StartedAtUtc);Assert.Single((await restarted.DashboardAsync(campaign.CampaignId,Token))!.Snapshots);
    }

    [Fact]
    public async Task MissingOperationalCoverageCreatesIncidentAndAutomaticGovernanceSuspension()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var setup=await Setup(factory.Database,Now.AddDays(-2),"EXPECT-2",60);var campaign=await setup.Service.CreateAsync("A","I",Expectation("EXPECT-2",60),"ops",Token);await setup.Service.StartAsync(campaign.CampaignId,"ops","start",Token);setup.Clock.UtcNow=Now;var result=await setup.Service.CaptureClosedDayAsync(campaign.CampaignId,DateOnly.FromDateTime(Now.AddDays(-1)),Token);
        Assert.True(result.SuspendedAutomatically);Assert.Equal(ForwardComparisonStatus.OperationallyInvalid,result.Comparison!.Status);var dashboard=await setup.Service.DashboardAsync(campaign.CampaignId,Token);Assert.Equal(ForwardCampaignStatus.Suspended,dashboard!.Campaign.Status);Assert.Single(dashboard.Incidents);Assert.Contains((await setup.Governance.GetSuspensionsAsync(Token)),x=>x.IsActive&&x.Scope==GovernanceSuspensionScope.StrategyVersion);Assert.Single(await setup.Governance.GetIncidentsAsync(10,Token));
    }

    [Fact]
    public async Task CampaignAndSnapshotPersistenceAreIdempotentImmutableAndNeverPromote()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new ForwardCampaignRepository(factory.Database);var campaign=Campaign();await repository.CreateAsync(campaign,Token);await repository.CreateAsync(campaign,Token);Assert.Equal(campaign.CampaignId,(await repository.FindAsync(campaign.CampaignId,Token))!.CampaignId);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repository.CreateAsync(campaign with{CampaignId="UNSAFE",CanPromoteStrategy=true},Token));var snapshot=Snapshot();await repository.SaveSnapshotAsync(snapshot,Token);await repository.SaveSnapshotAsync(snapshot,Token);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repository.SaveSnapshotAsync(snapshot with{SnapshotId="UNSAFE",CanPromoteStrategy=true},Token));
    }

    [Fact]
    public async Task ReconnectTelemetryIsRetainedAndOpenSessionsCannotCloseEarly()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new ForwardCampaignRepository(factory.Database);var campaign=Campaign() with{Status=ForwardCampaignStatus.Created};await repository.CreateAsync(campaign,Token);var sample=new ForwardHealthSample("HEALTH",campaign.CampaignId,Now,true,false,Now,Now,Now.AddMinutes(-1),"reconnected","HEALTH-HASH");await repository.SaveHealthAsync(sample,Token);var stored=await repository.GetHealthAsync(campaign.CampaignId,Now.AddMinutes(-1),Now.AddMinutes(1),Token);Assert.Equal(sample.LastReconnectAttemptUtc,stored.Single().LastReconnectAttemptUtc);
        var setup=await Setup(factory.Database,Now,"EXPECT-OPEN",60,accountId:"B",instanceId:"IB");var running=await setup.Service.CreateAsync("B","IB",Expectation("EXPECT-OPEN",60),"ops",Token);await setup.Service.StartAsync(running.CampaignId,"ops","start",Token);await Assert.ThrowsAsync<InvalidOperationException>(()=>setup.Service.CaptureClosedDayAsync(running.CampaignId,DateOnly.FromDateTime(Now),Token));
    }

    private static readonly DateTime Now=new(2026,8,28,0,0,0,DateTimeKind.Utc);private static CancellationToken Token=>TestContext.Current.CancellationToken;
    private static ForwardExpectation Expectation(string id="EXPECT",int interval=60)=>new(id,"REPORT","REPORT-HASH","S","1",.5m,60,2,100,5,interval,50,20,2,90,"1",Now.AddDays(-2));
    private static ForwardCampaign Campaign()=>new("CAMPAIGN","A","I","S","1",Expectation(),ForwardCampaignStatus.Running,Now.AddDays(-2),Now.AddDays(-1),null,"running","ops",false);
    private static ForwardDailySnapshot Snapshot(int trades=10,int wins=6,decimal net=500,decimal drawdown=2,decimal coverage=100)=>new("SNAP","CAMPAIGN",DateOnly.FromDateTime(Now.AddDays(-1)),Now.AddDays(-1),Now,Now,trades,wins,trades-wins,net,0,net,trades==0?0:net/trades/100,trades==0?0:100m*wins/trades,drawdown,10,10,0,coverage,true,"A|SEQ-1","HASH",false);
    private static async Task<TestSetup> Setup(PfaDatabase database,DateTime start,string expectationId,int interval,string accountId="A",string instanceId="I")
    {
        var clock=new ManualClock(start);var ledger=new SandboxLedgerRepository(database);var permits=new AllowPermits();var sandbox=new SandboxService(ledger,new SandboxStateProjector(),new SandboxBrokerSimulator(),new SandboxPortfolioProjector(),new FakeRegistry(),new InstrumentDefinitionRegistry(),clock,permits);await sandbox.CreateAccountAsync($"account-{accountId}",accountId,accountId,50000,Token);await sandbox.CreateInstanceAsync($"instance-{instanceId}",accountId,instanceId,"S","1","MES","MESU6",Token);await sandbox.StartInstanceAsync($"start-{instanceId}",accountId,instanceId,Token);var governance=new GovernanceRepository(database);await governance.SavePolicyAsync(Policy(start),Token);foreach(var approval in Approvals(start,accountId))await governance.GrantApprovalAsync(approval,Token);var report=Report();var service=new ForwardCampaignService(new ForwardCampaignRepository(database),new FakeValidation(report),sandbox,governance,new ForwardExpectationComparator(),new FakeHealth(),clock);return new(service,governance,clock);
    }
    private static WalkForwardValidationReport Report(){var folds=new[]{new WalkForwardFoldResult("F",WalkForwardFoldStatus.Passed,10,10,.5m,60,1.5m,2,"H",false,false)};return new("REPORT","1","PLAN","SIG","PARAM",WalkForwardAggregateStatus.Stable,folds,1,0,.5m,.5m,0,false,"DATA","REV","REPORT-HASH",Now.AddDays(-3),false);}
    private static GovernancePolicy Policy(DateTime at)=>new("POLICY","1","Forward",500,500,1000,3,1500,300,30,30,30,true,true,new HashSet<string>{"MES"},new Dictionary<string,string>{{"MES","EQUITY"}},at.AddDays(-1),null,"ops",at.AddDays(-1));
    private static GovernanceApproval[] Approvals(DateTime at,string account="A")=>[new($"AA-{account}",GovernanceApprovalScope.Account,account,"POLICY","1",at.AddDays(-1),Now.AddDays(10),"ops","ok"),new($"AS-{account}",GovernanceApprovalScope.StrategyVersion,"S|1","POLICY","1",at.AddDays(-1),Now.AddDays(10),"ops","ok")];
    private sealed record TestSetup(ForwardCampaignService Service,GovernanceRepository Governance,ManualClock Clock);
    private sealed class ManualClock(DateTime now):ISandboxClock{public DateTime UtcNow{get;set;}=now;}
    private sealed class AllowPermits:IGovernancePermitValidator{public bool Validate(GovernancePermit p,string a,string i,string s,DateTime n)=>true;}
    private sealed class FakeHealth:IForwardHealthProvider{public ForwardHealthSample Capture(string campaign,DateTime at,int interval){var id=$"H-{at:O}";return new(id,campaign,at,true,false,at,at,null,"healthy",id);}}
    private sealed class FakeValidation(WalkForwardValidationReport report):IWalkForwardValidationRepository{public Task<WalkForwardValidationReport?> FindReportAsync(string id,CancellationToken token=default)=>Task.FromResult<WalkForwardValidationReport?>(id==report.ReportId?report:null);public Task<WalkForwardPlan?> FindPlanAsync(string id,CancellationToken token=default)=>Task.FromResult<WalkForwardPlan?>(null);public Task SaveAsync(WalkForwardPlan plan,WalkForwardValidationReport value,CancellationToken token=default)=>throw new NotSupportedException();}
    private sealed class FakeRegistry:IStrategyRegistry{public Task<StrategyRegistryEntry?> FindAsync(string id,string version,CancellationToken token=default)=>Task.FromResult<StrategyRegistryEntry?>(new(Definition(id,version),"H",StrategyRegistryStatus.ValidationComplete,Now,"test"));public Task<IReadOnlyList<StrategyRegistryEntry>> GetAllAsync(CancellationToken token=default)=>Task.FromResult<IReadOnlyList<StrategyRegistryEntry>>([]);public Task<StrategyRegistryEntry> RegisterAsync(ImmutableStrategyDefinition d,CancellationToken token=default)=>throw new NotSupportedException();public Task<StrategyRegistryEntry> TransitionAsync(string a,string b,StrategyRegistryStatus c,string d,string e,CancellationToken token=default)=>throw new NotSupportedException();private static ImmutableStrategyDefinition Definition(string id,string version)=>new(id,version,"F","Test","Research","Both","{}","{}","{}","{}","{}","{}",["MES"],[],[],[],new("D","F","P","Q","S","E","R","SESSION","CONTRACT"),"D","V","test",Now);}
}
