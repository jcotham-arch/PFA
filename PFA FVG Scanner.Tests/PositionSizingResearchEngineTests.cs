using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Tests;

public sealed class PositionSizingResearchEngineTests
{
    [Fact]
    public void EvaluatesOneThroughFiveWithoutChangingUnderlyingR()
    {
        var result=new PositionSizingResearchEngine().Evaluate(new(1.5m,100m,2m,25000m,1000m,2000m));
        Assert.Equal([1,2,3,4,5],result.Variants.Select(x=>x.Contracts));
        Assert.Equal(148m,result.Variants[0].NetProfitLossDollars);
        Assert.Equal(740m,result.Variants[4].NetProfitLossDollars);
        Assert.Equal(5,result.LargestEligibleQuantity);
        Assert.False(result.CanActivateStrategy);Assert.False(result.CanRouteToRealBroker);
    }

    [Fact]
    public void RejectsQuantityThatExceedsCapitalRiskEvenWhenScenarioWins()
    {
        var result=new PositionSizingResearchEngine().Evaluate(new(2m,200m,2m,10000m,2000m,3000m));
        Assert.True(result.Variants[1].CapitalAtRiskPercent>2m);
        Assert.False(result.Variants[1].EligibleForSandboxResearch);
        Assert.Equal("ExcessiveCapitalRisk",result.Variants[1].Classification);
    }
}
