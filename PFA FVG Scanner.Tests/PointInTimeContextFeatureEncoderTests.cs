using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PointInTimeContextFeatureEncoderTests
{
    [Fact]
    public void AvailableCanonicalHistoryEmitsRegimesInteractionsAndExplicitExternalGates()
    {
        var features=new Dictionary<string,decimal>{{"direction",1}};
        PointInTimeContextFeatureEncoder.Add(features,
            "{\"close\":\"102\",\"high\":\"103\",\"low\":\"100\",\"volume\":\"160\"}","100",
            "{\"barCount\":20,\"meanRange5\":1.4,\"meanRange20\":2,\"meanVolume5\":130,\"meanVolume20\":100,\"meanBody20\":1.3,\"high20\":105,\"low20\":95}",
            "{\"TotalVolume\":100,\"BuyVolume\":65,\"SellVolume\":30,\"UnknownVolume\":5,\"Delta\":35,\"CumulativeDelta\":120,\"PointOfControlPrice\":101.5,\"LastBidAskImbalance\":0.2}",
            "{\"sampleCount\":20,\"meanRange\":2,\"meanVolume\":80,\"meanReturnFraction\":0.001,\"meanAbsoluteReturnFraction\":0.003,\"positiveCloseRate\":0.6}",
            "[{\"instrumentId\":\"6E\",\"return5Fraction\":0.002},{\"instrumentId\":\"6J\",\"return5Fraction\":-0.001},{\"instrumentId\":\"MES\",\"return5Fraction\":0.003}]");

        Assert.Equal(1,features["context.availability.canonical.latestBar"]);
        Assert.Equal(1,features["context.availability.canonical.context20"]);
        Assert.Equal(1,features["context.availability.canonical.seasonalityHistory"]);
        Assert.Equal(1,features["context.availability.external.orderFlow"]);
        Assert.Equal(0,features["context.availability.external.levelTwo"]);
        Assert.Equal(1,features["context.regime.volatility.expansion"]);
        Assert.Equal(1,features["context.regime.volume.high"]);
        Assert.Equal(1,features["context.regime.auction.directional"]);
        Assert.Equal(1,features["context.interaction.highVolumeExpansion"]);
        Assert.True(features["context.interaction.directionAlignedMomentum5"]>0);
        Assert.Equal(.65m,features["context.orderFlow.buyShare"]);
        Assert.Equal(.35m,features["context.orderFlow.deltaFraction"]);
        Assert.Equal(.2m,features["context.orderFlow.lastBidAskImbalance"]);
        Assert.Equal(2m,features["context.seasonality.volumeVsClockBaseline"]);
        Assert.Equal(.2m,features["context.seasonality.directionalBiasAtClock"]);
        Assert.Equal(1,features["context.availability.canonical.crossMarket"]);
        Assert.Equal(1m/3m,features["context.crossMarket.directionalBreadth"]);
        Assert.Equal(1m/3m,features["context.interaction.directionAlignedCrossMarketBreadth"]);
        Assert.Equal(.002m,features["context.crossMarket.peerReturn5.6E"]);
    }

    [Fact]
    public void MissingHistoryEmitsAvailabilityOnlyAndNeverInventsMeasurements()
    {
        var features=new Dictionary<string,decimal>();
        PointInTimeContextFeatureEncoder.Add(features,null,null,
            "{\"barCount\":3,\"meanRange5\":2,\"meanRange20\":2,\"meanVolume5\":100,\"meanVolume20\":100,\"meanBody20\":1,\"high20\":103,\"low20\":99}");

        Assert.Equal(0,features["context.availability.canonical.latestBar"]);
        Assert.Equal(0,features["context.availability.canonical.context20"]);
        Assert.Equal(0,features["context.availability.canonical.seasonalityHistory"]);
        Assert.Equal(0,features["context.availability.canonical.crossMarket"]);
        Assert.DoesNotContain("context.volatility.meanRange20",features.Keys);
        Assert.DoesNotContain(features.Keys,x=>x.StartsWith("context.regime.",StringComparison.Ordinal));
    }
}
