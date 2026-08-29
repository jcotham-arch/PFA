using PFA_FVG_Scanner.Domain.Context;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Domain.OrderFlow;

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
        Assert.Equal(12,snapshot.Families.Count);
        Assert.DoesNotContain(snapshot.Families.SelectMany(x=>x.SourceReferences),x=>x.Contains("BAR-25")||x.Contains("BAR-29"));
        Assert.Equal(ContextFeatureAvailability.Available,snapshot.Families.Single(x=>x.FamilyId=="seasonality").Availability);
        Assert.Equal(ContextFeatureAvailability.Available,snapshot.Families.Single(x=>x.FamilyId=="volatility-regime").Availability);
        Assert.Equal("bar-proxy-only",snapshot.Families.Single(x=>x.FamilyId=="liquidity-spread").CategoricalFeatures["measurement"]);
        Assert.Equal("Normal",snapshot.Families.Single(x=>x.FamilyId=="volatility-regime").CategoricalFeatures["regime"]);
        Assert.All(snapshot.Families.Where(x=>x.FamilyId is "order-flow" or "level-two" or "options-positioning" or "market-breadth"),
            x=>{Assert.Equal(ContextFeatureAvailability.SourceUnavailable,x.Availability);Assert.Empty(x.NumericFeatures);});
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

    [Fact]
    public void SnapshotActivatesOrderFlowOnlyWhenFreshKnownAndSourceBacked()
    {
        var start=new DateTime(2026,8,27,12,0,0,DateTimeKind.Utc);var decision=start.AddMinutes(30);
        var flow=new OrderFlowFeatureSnapshot("OFS","MES","MESU6",start.AddMinutes(25),decision,decision,"SESSION",
            "assignment-1",OrderFlowFeatureEngine.Version,"REV",100,60,35,5,25,80,101,.2m,[],["EVENT-1"],OrderFlowQualityFlags.None,"HASH");
        var snapshot=new BarDerivedResearchContextEngine(new LegacyUtcTradingSessionService()).Build("MES","MESU6","1m",decision,
            Enumerable.Range(0,30).Select(i=>Bar(i,start.AddMinutes(i),100,10)).ToArray(),flow);
        var family=snapshot.Families.Single(x=>x.FamilyId=="order-flow");Assert.Equal(ContextFeatureAvailability.Available,family.Availability);
        Assert.Equal(.6m,family.NumericFeatures["buyShare"]);Assert.Equal("REV",family.CategoricalFeatures["dataRevision"]);
        var stale=new BarDerivedResearchContextEngine(new LegacyUtcTradingSessionService()).Build("MES","MESU6","1m",decision.AddMinutes(6),
            Enumerable.Range(0,30).Select(i=>Bar(i,start.AddMinutes(i),100,10)).ToArray(),flow);
        Assert.Equal(ContextFeatureAvailability.SourceUnavailable,stale.Families.Single(x=>x.FamilyId=="order-flow").Availability);
    }

    private static CanonicalBar Bar(int i,DateTime open,decimal close,decimal volume)=>new($"BAR-{i}",1,"MES","MESU6","MESU6","1m",open,open.AddMinutes(1),close-.1m,close+.2m,close-.2m,close,volume,true,"S",DateOnly.FromDateTime(open),"1","1",CorrectionState.Original,MarketDataQualityFlags.None,open.AddMinutes(1),$"H{i}");
}
