using PFA_FVG_Scanner.Domain.Context;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Tests;

public sealed class BarDerivedResearchContextEngineTests
{
    [Fact]
    public void SnapshotUsesOnlyBarsKnownByDecisionClockAndEmitsSupportedFamilies()
    {
        var start=new DateTime(2026,8,27,12,0,0,DateTimeKind.Utc);var bars=Enumerable.Range(0,30)
            .Select(i=>Bar(i,start.AddMinutes(i),100+i*.1m,100+i)).ToArray();
        var snapshot=new BarDerivedResearchContextEngine(new LegacyUtcTradingSessionService())
            .Build("MES","MESU6","1m",start.AddMinutes(25),bars);
        Assert.Equal(8,snapshot.Families.Count);
        Assert.DoesNotContain(snapshot.Families.SelectMany(x=>x.SourceReferences),x=>x.Contains("BAR-25")||x.Contains("BAR-29"));
        Assert.Equal(ContextFeatureAvailability.Available,snapshot.Families.Single(x=>x.FamilyId=="seasonality").Availability);
        Assert.Equal(ContextFeatureAvailability.Available,snapshot.Families.Single(x=>x.FamilyId=="volatility-regime").Availability);
        Assert.Equal("bar-proxy-only",snapshot.Families.Single(x=>x.FamilyId=="liquidity-spread").CategoricalFeatures["measurement"]);
        Assert.False(snapshot.CanActivateStrategy);Assert.False(snapshot.CanRouteToRealBroker);
    }

    [Fact]
    public void SnapshotReportsInsufficientHistoryRatherThanInventingZeros()
    {
        var start=new DateTime(2026,8,27,12,0,0,DateTimeKind.Utc);var snapshot=new BarDerivedResearchContextEngine(new LegacyUtcTradingSessionService())
            .Build("MES","MESU6","1m",start.AddMinutes(3),Enumerable.Range(0,3).Select(i=>Bar(i,start.AddMinutes(i),100,10)).ToArray());
        var volatility=snapshot.Families.Single(x=>x.FamilyId=="volatility-regime");
        Assert.Equal(ContextFeatureAvailability.InsufficientHistory,volatility.Availability);Assert.Empty(volatility.NumericFeatures);
    }

    private static CanonicalBar Bar(int i,DateTime open,decimal close,decimal volume)=>new($"BAR-{i}",1,"MES","MESU6","MESU6","1m",open,open.AddMinutes(1),close-.1m,close+.2m,close-.2m,close,volume,true,"S",DateOnly.FromDateTime(open),"1","1",CorrectionState.Original,MarketDataQualityFlags.None,open.AddMinutes(1),$"H{i}");
}
