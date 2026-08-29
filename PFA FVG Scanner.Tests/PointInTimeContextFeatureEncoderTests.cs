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
            "{\"barCount\":20,\"meanRange5\":1.4,\"meanRange20\":2,\"meanVolume5\":130,\"meanVolume20\":100,\"meanBody20\":1.3,\"high20\":105,\"low20\":95}");

        Assert.Equal(1,features["context.availability.canonical.latestBar"]);
        Assert.Equal(1,features["context.availability.canonical.context20"]);
        Assert.Equal(0,features["context.availability.external.orderFlow"]);
        Assert.Equal(0,features["context.availability.external.levelTwo"]);
        Assert.Equal(1,features["context.regime.volatility.expansion"]);
        Assert.Equal(1,features["context.regime.volume.high"]);
        Assert.Equal(1,features["context.regime.auction.directional"]);
        Assert.Equal(1,features["context.interaction.highVolumeExpansion"]);
        Assert.True(features["context.interaction.directionAlignedMomentum5"]>0);
    }

    [Fact]
    public void MissingHistoryEmitsAvailabilityOnlyAndNeverInventsMeasurements()
    {
        var features=new Dictionary<string,decimal>();
        PointInTimeContextFeatureEncoder.Add(features,null,null,
            "{\"barCount\":3,\"meanRange5\":2,\"meanRange20\":2,\"meanVolume5\":100,\"meanVolume20\":100,\"meanBody20\":1,\"high20\":103,\"low20\":99}");

        Assert.Equal(0,features["context.availability.canonical.latestBar"]);
        Assert.Equal(0,features["context.availability.canonical.context20"]);
        Assert.DoesNotContain("context.volatility.meanRange20",features.Keys);
        Assert.DoesNotContain(features.Keys,x=>x.StartsWith("context.regime.",StringComparison.Ordinal));
    }
}
