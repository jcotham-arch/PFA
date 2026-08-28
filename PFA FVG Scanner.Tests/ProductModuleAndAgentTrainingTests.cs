using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Modules;
using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class ProductModuleAndAgentTrainingTests
{
    private static readonly DateTime Now=new(2026,8,28,4,0,0,DateTimeKind.Utc);
    private readonly ProductModuleCatalog _catalog=new();

    [Fact]
    public void PublicCatalogUsesAdvancedStrategiesNameAndEveryPremiumModuleHasSku()
    {
        var modules=_catalog.GetAll();var advanced=Assert.Single(modules,x=>x.ModuleId=="advanced-strategies");
        Assert.Equal("Advanced Strategies",advanced.DisplayName);Assert.Equal("SAM",advanced.PartnerId);
        Assert.DoesNotContain(modules,x=>x.DisplayName.Contains("Sam",StringComparison.OrdinalIgnoreCase));
        Assert.All(modules.Where(x=>x.RequiresPaidEntitlement),x=>Assert.False(string.IsNullOrWhiteSpace(x.SubscriptionSku)));
        Assert.All(modules,x=>Assert.False(x.CanRouteToRealBroker));
    }

    [Fact]
    public void SubscriptionControlsAccessButNeverBypassesSafetyOrConnectorHealth()
    {
        var evaluator=new ModuleEntitlementEvaluator(_catalog);var entitlement=new ModuleEntitlement("E","USER","PFA-LIVE-AGENT",ModuleEntitlementStatus.Active,Now.AddDays(-1),null,"BILLING",AgentTrainingDatasetBuilder.Hash("E"));
        var locked=evaluator.Evaluate(new("USER","live-agent","design-gated-1.0.0",true,true,true,[],Now));Assert.Equal(ModuleActivationState.Locked,locked.State);
        var safety=evaluator.Evaluate(new("USER","live-agent","design-gated-1.0.0",true,false,true,[entitlement],Now));Assert.Equal(ModuleActivationState.SafetyBlocked,safety.State);
        var active=evaluator.Evaluate(new("USER","live-agent","design-gated-1.0.0",true,true,true,[entitlement],Now));Assert.Equal(ModuleActivationState.Active,active.State);Assert.False(active.CanRouteToRealBroker);
        var partnerEntitlement=entitlement with{SubscriptionSku="PFA-ADVANCED-STRATEGIES"};var partner=evaluator.Evaluate(new("USER","advanced-strategies","partner-contract-1.0.0",true,true,false,[partnerEntitlement],Now));Assert.Equal(ModuleActivationState.Suspended,partner.State);
    }

    [Fact]
    public void CancelledExpiredOrDifferentUserEntitlementsRemainLocked()
    {
        var evaluator=new ModuleEntitlementEvaluator(_catalog);var entitlement=new ModuleEntitlement("E","USER","PFA-AGENT-RESEARCH",ModuleEntitlementStatus.Active,Now.AddDays(-2),Now.AddDays(-1),"B",AgentTrainingDatasetBuilder.Hash("E"));
        Assert.Equal(ModuleActivationState.Locked,evaluator.Evaluate(new("USER","agent-research-lab","1.0.0",true,true,true,[entitlement],Now)).State);
        Assert.Equal(ModuleActivationState.Locked,evaluator.Evaluate(new("OTHER","agent-research-lab","1.0.0",true,true,true,[entitlement with{EffectiveToUtc=null}],Now)).State);
    }

    [Fact]
    public void AgentDatasetIsDeterministicPointInTimeAndCannotActivateOrRoute()
    {
        var example=Example();var builder=new AgentTrainingDatasetBuilder();var a=builder.Build("D","REV",Now,[example]);var b=builder.Build("D","REV",Now,[example]);
        Assert.Equal(a.ContentHash,b.ContentHash);Assert.False(a.CanActivateStrategy);Assert.False(a.CanRouteToRealBroker);
    }

    [Fact]
    public void AgentDatasetRejectsFutureLabelsLeakageAndDuplicateIdentity()
    {
        var builder=new AgentTrainingDatasetBuilder();var example=Example();
        Assert.Throws<InvalidOperationException>(()=>builder.Build("D","R",Now,[example with{OutcomeKnownAtUtc=Now.AddMinutes(1)}]));
        Assert.Throws<InvalidOperationException>(()=>builder.Build("D","R",Now,[example with{FeatureKnownAtUtc=example.DecisionTimeUtc.AddSeconds(1)}]));
        Assert.Throws<InvalidOperationException>(()=>builder.Build("D","R",Now,[example,example]));
    }

    [Fact]
    public async Task EmptyCorpusReadinessFailsClosedWithoutActivationOrRouting()
    {using var factory=await TestDatabaseFactory.CreateAsync();var readiness=await new AgentTrainingReadinessService(factory.Database).GetAsync(TestContext.Current.CancellationToken);Assert.Equal(0,readiness.Observations);Assert.False(readiness.SupervisedTrainingReady);Assert.False(readiness.CanActivateStrategy);Assert.False(readiness.CanRouteToRealBroker);}

    [Fact]
    public async Task GenericOutcomeDatasetIsImmutableDeterministicAndChronologicallySplit()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();
        var repository=new UniversalMarketRecordRepository(factory.Database);
        foreach(var instrument in new[]{"MES","6E"})
        for(var index=0;index<10;index++)
        {
            var known=Now.AddDays(-10+index);var observation=new UniversalMarketObservation($"OBS-{instrument}-{index}",1,
                "liquidity-sweep","1.0.0","LiquiditySweep",instrument,instrument=="MES"?"MESU6":"6EU6","5m",
                index%2==0?PatternDirection.Bullish:PatternDirection.Bearish,known.AddMinutes(-5),known,
                PatternLifecycleState.Detected,"test","{\"range\":5}",[],MarketDataQualityFlags.None,$"OH-{instrument}-{index}");
            await repository.SaveObservationAsync(observation,TestContext.Current.CancellationToken);
            var measured=known.AddMinutes(15);var outcome=new UniversalMarketOutcome($"OUT-{instrument}-{index}",observation.ObservationId,
                "generic-forward-1.0.0",measured,1,"test","{}",
                [new("directional-close-change",15,index-5,"ticks",measured),
                 new("maximum-favorable-excursion",15,index+1,"ticks",measured),
                 new("maximum-adverse-excursion",15,2,"ticks",measured)],[],MarketDataQualityFlags.None);
            await repository.SaveOutcomeAsync(outcome,TestContext.Current.CancellationToken);
        }
        var service=new GenericOutcomeDatasetService(factory.Database);var request=new GenericOutcomeDatasetRequest(Now,15,["MES","6E"]);
        var first=await service.BuildAsync(request,TestContext.Current.CancellationToken);
        var repeated=await service.BuildAsync(request,TestContext.Current.CancellationToken);
        Assert.Equal(first.ContentHash,repeated.ContentHash);Assert.Equal(20,first.ExampleCount);
        Assert.Equal(14,first.TrainCount);Assert.Equal(2,first.ValidationCount);Assert.Equal(4,first.TestCount);
        Assert.False(first.CanActivateStrategy);Assert.False(first.CanRouteToRealBroker);
        Assert.Single(await service.GetAllAsync(TestContext.Current.CancellationToken));
        var training=new AgentBaselineTrainingService(factory.Database);
        var baseline=await training.TrainAsync(new(first.DatasetId),TestContext.Current.CancellationToken);
        var baselineAgain=await training.TrainAsync(new(first.DatasetId),TestContext.Current.CancellationToken);
        Assert.Equal(baseline.ContentHash,baselineAgain.ContentHash);Assert.Equal(14,baseline.TrainingSamples);
        Assert.Equal(new[]{"Train","Validation","Test"},baseline.Metrics.Select(x=>x.Split));
        Assert.Equal(4,baseline.SegmentMetrics!.Count);
        Assert.All(baseline.SegmentMetrics,metric=>Assert.True(metric.SampleCount>0));
        Assert.False(baseline.CanActivateStrategy);Assert.False(baseline.CanRouteToRealBroker);
        Assert.Single(await training.GetAllAsync(TestContext.Current.CancellationToken));
    }

    private static AgentTrainingExample Example()=>new("EX","D","MES","MESU6","5m",Now.AddHours(-2),Now.AddHours(-2).AddMinutes(5),Now.AddHours(-1),Now.AddMinutes(-1),new Dictionary<string,decimal>{{"range",5}},["liquidity-sweep"],["initial-observation"],1.2m,"REV",AgentTrainingDatasetBuilder.Hash("EX"));
}
