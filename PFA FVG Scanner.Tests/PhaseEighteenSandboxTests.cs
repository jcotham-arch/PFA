using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Domain.Strategies;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Domain.Governance;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseEighteenSandboxTests
{
    [Fact]
    public void BrokerRejectsFutureSignalsAndFutureMarketKnowledge()
    {
        var clock=new ManualClock(Now);var broker=new SandboxBrokerSimulator();var account=new SandboxAccount("A","A","USD",50000,Now);var instance=new SandboxInstance("I","A","S","1","MES","MESU6",SandboxInstanceStatus.Running,Now,Now,null);var model=Model();
        Assert.Throws<InvalidOperationException>(()=>broker.Submit(account,instance,Signal("I","X",Now.AddSeconds(1)),model,clock));var order=broker.Submit(account,instance,Signal("I","Y",Now),model,clock);var market=Market("M",100,101,99,100,1,Now.AddMinutes(1));Assert.Throws<InvalidOperationException>(()=>broker.Process(order,market,Definition(),model,clock));
    }

    [Fact]
    public void BrokerFreezesFillModelAndCanonicalAdaptersPreservePointInTimeLineage()
    {
        var clock=new ManualClock(Now);var broker=new SandboxBrokerSimulator();var account=new SandboxAccount("A","A","USD",50000,Now);var instance=new SandboxInstance("I","A","S","1","MES","MESU6",SandboxInstanceStatus.Running,Now,Now,null);var order=broker.Submit(account,instance,Signal("I","X",Now),Model(),clock);clock.UtcNow=Now.AddMinutes(1);
        Assert.Throws<InvalidOperationException>(()=>broker.Process(order,Market("M",100,101,99,100,1,clock.UtcNow),Definition(),Model(commission:1),clock));
        var bar=new CanonicalBar("BAR",2,"MES","MESU6","MESU6","1m",Now,Now.AddMinutes(1),100,101,99,100,10,true,"SESSION",DateOnly.FromDateTime(Now),"1","1",CorrectionState.CorrectedRevision,MarketDataQualityFlags.Corrected,Now.AddMinutes(1),"REVISION");var slice=SandboxCanonicalBarAdapter.From(bar,4);Assert.Equal("BAR|R2",slice.SourceId);Assert.Equal(bar.RevisionEffectiveUtc,slice.KnownAtUtc);Assert.Equal("REVISION",slice.DataRevision);
        Assert.Throws<InvalidOperationException>(()=>SandboxCanonicalBarAdapter.From(bar with{IsComplete=false},4));
    }

    [Fact]
    public void OnlyTradeProposalsAdaptIntoSandboxSignals()
    {
        var proposal=new StrategyDecision(StrategyDecisionType.TradeProposal,"S","1",Now,"reason","{}");var signal=SandboxStrategyDecisionAdapter.ToSignal(proposal,"I",SandboxOrderSide.Buy,SandboxOrderType.Market,1,null,null,"E");Assert.Equal(Now,signal.KnownAtUtc);Assert.Equal("I",signal.InstanceId);
        Assert.Throws<InvalidOperationException>(()=>SandboxStrategyDecisionAdapter.ToSignal(proposal with{Decision=StrategyDecisionType.NoTrade},"I",SandboxOrderSide.Buy,SandboxOrderType.Market,1,null,null,"E"));
    }

    [Fact]
    public async Task OrderLifecycleRetainsMissedAndPartialFills()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var clock=new ManualClock(Now);var service=Service(factory.Database,clock);await Ready(service,"A","I");var model=Model(slippage:0,commission:0);var signal=Signal("I","L",Now,SandboxOrderType.Limit,3,100);
        var submitted=await service.SubmitSignalAsync("signal","A",signal,model,Permit(signal),Token);clock.UtcNow=Now.AddMinutes(1);var missed=await service.ProcessMarketAsync("miss","A",Market("M1",102,103,101,102,3,clock.UtcNow),model,Token);Assert.Equal(SandboxOrderStatus.Working,missed.Orders.Values.Single().Status);Assert.Empty(missed.Fills);
        clock.UtcNow=Now.AddMinutes(2);var partial=await service.ProcessMarketAsync("partial","A",Market("M2",100.5m,101,99,100,1,clock.UtcNow),model,Token);Assert.Equal(SandboxOrderStatus.PartiallyFilled,partial.Orders.Values.Single().Status);Assert.Equal(1,partial.Fills.Single().Quantity);
        clock.UtcNow=Now.AddMinutes(3);var filled=await service.ProcessMarketAsync("finish","A",Market("M3",100,101,99,100,2,clock.UtcNow),model,Token);Assert.Equal(SandboxOrderStatus.Filled,filled.Orders.Values.Single().Status);Assert.Equal(3,filled.Positions.Values.Single().SignedQuantity);Assert.Equal(2,filled.Fills.Count);
    }

    [Fact]
    public async Task InstrumentEconomicsSlippageAndCommissionsFlowToPerformance()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var clock=new ManualClock(Now);var service=Service(factory.Database,clock);await Ready(service,"A","I");var model=Model(slippage:1,commission:1);
        var buy=Signal("I","BUY",Now,quantity:2);await service.SubmitSignalAsync("buy","A",buy,model,Permit(buy),Token);clock.UtcNow=Now.AddMinutes(1);await service.ProcessMarketAsync("buy-fill","A",Market("B",100,101,99,100,2,clock.UtcNow),model,Token);
        var sell=Signal("I","SELL",clock.UtcNow,quantity:2,side:SandboxOrderSide.Sell);await service.SubmitSignalAsync("sell","A",sell,model,Permit(sell),Token);clock.UtcNow=Now.AddMinutes(2);var state=await service.ProcessMarketAsync("sell-fill","A",Market("S",102,103,101,102,2,clock.UtcNow),model,Token);
        Assert.Equal(15m,state.Performance.RealizedProfitLoss);Assert.Equal(4m,state.Performance.Commissions);Assert.Equal(50011m,state.Performance.CashBalance);Assert.Equal(0,state.Positions.Values.Single().SignedQuantity);Assert.Single(state.Trades);
    }

    [Fact]
    public async Task RestartRecoveryAndCommandIdempotencyRebuildExactState()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var clock=new ManualClock(Now);var first=Service(factory.Database,clock);await Ready(first,"A","I");var signal=Signal("I","X",Now);var original=await first.SubmitSignalAsync("signal","A",signal,Model(),Permit(signal),Token);clock.UtcNow=Now.AddHours(1);var retry=await first.SubmitSignalAsync("signal","A",signal,Model(),Permit(signal),Token);Assert.Equal(original.LastSequence,retry.LastSequence);
        var restarted=Service(factory.Database,clock);var recovered=await restarted.GetAccountAsync("A",Token);Assert.Equal(original.Orders.Keys,recovered.Orders.Keys);Assert.Equal(original.Instances["I"].StrategyVersion,recovered.Instances["I"].StrategyVersion);Assert.Equal(6,await Count(factory.Database,"SandboxLedgerEvents"));
    }

    [Fact]
    public async Task MultipleAccountsAndFrozenVersionsRemainIsolated()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var clock=new ManualClock(Now);var service=Service(factory.Database,clock);await Task.WhenAll(service.CreateAccountAsync("a","A","Alpha",50000,Token),service.CreateAccountAsync("b","B","Beta",25000,Token));await service.CreateInstanceAsync("ai","A","IA","S","1","MES","MESU6",Token);await service.CreateInstanceAsync("bi","B","IB","S","2","MES","MESU6",Token);
        var a=await service.GetAccountAsync("A",Token);var b=await service.GetAccountAsync("B",Token);Assert.Single(a.Instances);Assert.Single(b.Instances);Assert.Equal("1",a.Instances["IA"].StrategyVersion);Assert.Equal("2",b.Instances["IB"].StrategyVersion);Assert.Equal(50000,a.Performance.CashBalance);Assert.Equal(25000,b.Performance.CashBalance);
    }

    [Fact]
    public async Task UnvalidatedStrategyAndMutableLedgerOperationsAreDenied()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var clock=new ManualClock(Now);var service=Service(factory.Database,clock,StrategyRegistryStatus.FrozenResearch);await service.CreateAccountAsync("a","A","A",50000,Token);await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>service.CreateInstanceAsync("i","A","I","S","1","MES","MESU6",Token));
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(Token);await using var command=connection.CreateCommand();command.CommandText="DELETE FROM SandboxLedgerEvents";await Assert.ThrowsAsync<SqliteException>(()=>command.ExecuteNonQueryAsync(Token));
    }

    [Fact]
    public void SandboxControlTokenIsRuntimeOnlyAndDenyByDefault()
    {
        var absent=new SandboxControlAuthorizer(new ConfigurationBuilder().Build());Assert.False(absent.IsConfigured);Assert.False(absent.Authorize("anything"));var configured=new SandboxControlAuthorizer(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Sandbox:ControlToken","secret"}}).Build());Assert.True(configured.IsConfigured);Assert.True(configured.Authorize("secret"));Assert.False(configured.Authorize("wrong"));
    }

    private static readonly DateTime Now=new(2026,8,27,14,0,0,DateTimeKind.Utc);private static CancellationToken Token=>TestContext.Current.CancellationToken;
    private static async Task Ready(SandboxService service,string account,string instance){await service.CreateAccountAsync("account",account,account,50000,Token);await service.CreateInstanceAsync("instance",account,instance,"S","1","MES","MESU6",Token);await service.StartInstanceAsync("start",account,instance,Token);}
    private static SandboxService Service(PfaDatabase database,ManualClock clock,StrategyRegistryStatus status=StrategyRegistryStatus.ValidationComplete){var repository=new SandboxLedgerRepository(database);return new(repository,new SandboxStateProjector(),new SandboxBrokerSimulator(),new SandboxPortfolioProjector(),new FakeRegistry(status),new InstrumentDefinitionRegistry(),clock,new AllowPermits());}
    private static GovernancePermit Permit(SandboxSignal signal)=>new("TEST","A",signal.InstanceId,signal.SignalId,Now.AddDays(1),"TEST");
    private static SandboxSignal Signal(string instance,string id,DateTime known,SandboxOrderType type=SandboxOrderType.Market,int quantity=1,decimal? limit=null,SandboxOrderSide side=SandboxOrderSide.Buy)=>new(id,instance,side,type,quantity,limit,null,known,"test",["EVIDENCE"]);
    private static SandboxMarketSlice Market(string id,decimal open,decimal high,decimal low,decimal close,int available,DateTime known)=>new(id,"MES","MESU6",known.AddMinutes(-1),known,known,open,high,low,close,available,"REV-1");
    private static SandboxFillModel Model(decimal slippage=0,decimal commission=0)=>new("FM-1",0,slippage,commission,true);private static InstrumentDefinition Definition()=>new InstrumentDefinitionRegistry().GetAll().Single(x=>x.InstrumentId=="MES");
    private static async Task<int> Count(PfaDatabase db,string table){await using var c=db.CreateConnection();await c.OpenAsync(Token);await using var q=c.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt32(await q.ExecuteScalarAsync(Token));}
    private sealed class ManualClock(DateTime now):ISandboxClock{public DateTime UtcNow{get;set;}=now;}
    private sealed class AllowPermits:IGovernancePermitValidator{public bool Validate(GovernancePermit permit,string accountId,string instanceId,string signalId,DateTime nowUtc)=>permit.AccountId==accountId&&permit.InstanceId==instanceId&&permit.SignalId==signalId&&permit.ExpiresAtUtc>=nowUtc;}
    private sealed class FakeRegistry(StrategyRegistryStatus status):IStrategyRegistry
    {
        public Task<StrategyRegistryEntry?> FindAsync(string id,string version,CancellationToken token=default)=>Task.FromResult<StrategyRegistryEntry?>(new(Definition(id,version),"HASH",status,Now,"test"));public Task<IReadOnlyList<StrategyRegistryEntry>> GetAllAsync(CancellationToken token=default)=>Task.FromResult<IReadOnlyList<StrategyRegistryEntry>>([]);public Task<StrategyRegistryEntry> RegisterAsync(ImmutableStrategyDefinition definition,CancellationToken token=default)=>throw new NotSupportedException();public Task<StrategyRegistryEntry> TransitionAsync(string id,string version,StrategyRegistryStatus target,string reason,string actor,CancellationToken token=default)=>throw new NotSupportedException();
        private static ImmutableStrategyDefinition Definition(string id,string version)=>new(id,version,"FAMILY","Test","Research","Both","{}","{}","{}","{}","{}","{}",["MES"],[],[],[],new("D","F","P","Q","S","E","R","SESSION","CONTRACT"),"DISCOVERY","VALIDATION","test",Now);
    }
}
