using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class MesOrderFlowResearchTests
{
    [Fact]
    public async Task BarProxyStudyCannotMasqueradeAsTrueOrderFlowWithoutEvents()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var candles=new CandleRepository(factory.Database);var token=TestContext.Current.CancellationToken;
        for(var i=0;i<420;i++)
        {
            var cycle=i%30;var center=5000m+(decimal)Math.Sin(i/6d)*2m;
            var low=cycle==20?center-4m:center-1m;var high=cycle==5?center+4m:center+1m;
            var close=cycle==20?low+2m:cycle==5?high-2m:center+.25m;
            await candles.SaveAsync(TestData.Candle(i,center,high,low,close,volume:cycle is 5 or 20?180:100),"TEST",token);
        }
        var service=new MesOrderFlowResearchService(factory.Database,new OrderFlowRepository(factory.Database));
        var report=await service.RunAsync(30,token);

        Assert.Equal("BarResponseProxy",report.DataTier);Assert.False(report.TrueOrderFlowTestingActive);
        Assert.False(report.EligibleForAgentTraining);Assert.False(report.CanRouteToRealBroker);
        Assert.Equal(0,report.TrueOrderFlowCoverage.Events);Assert.NotEmpty(report.Metrics);
        Assert.NotNull(await service.LatestAsync(token));
    }
}
