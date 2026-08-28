using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Modules;
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

    private static AgentTrainingExample Example()=>new("EX","D","MES","MESU6","5m",Now.AddHours(-2),Now.AddHours(-2).AddMinutes(5),Now.AddHours(-1),Now.AddMinutes(-1),new Dictionary<string,decimal>{{"range",5}},["liquidity-sweep"],["initial-observation"],1.2m,"REV",AgentTrainingDatasetBuilder.Hash("EX"));
}
