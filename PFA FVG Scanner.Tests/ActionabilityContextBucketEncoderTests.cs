using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class ActionabilityContextBucketEncoderTests
{
    [Fact]
    public void EncodesExplicitAndJointRegimesWithoutUsingOutcomes()
    {
        var values=ActionabilityContextBucketEncoder.Encode(new Dictionary<string,decimal>
        {
            ["market.closeLocation"]=.8m,
            ["context.regime.volatility.expansion"]=1,
            ["context.regime.volume.high"]=1,
            ["context.regime.auction.directional"]=1,
            ["context.momentum.direction.positive"]=1,
            ["context.regime.trendBalance.trend"]=1,
            ["context.regime.trendDirection.up"]=1,
            ["context.trend.signedEfficiency20"]=.5m,
            ["context.interaction.highVolumeExpansion"]=1,
            ["context.interaction.lowVolumeCompression"]=0,
            ["context.availability.canonical.crossMarket"]=1,
            ["context.crossMarket.directionalBreadth"]=.5m,
            ["direction"]=1
        });
        Assert.Contains("close-location:upper",values);
        Assert.Contains("volatility-volume:expansion+high",values);
        Assert.Contains("auction-momentum:directional+positive",values);
        Assert.Contains("trend-state:trend+up",values);
        Assert.Contains("trend-pattern:aligned",values);
        Assert.Contains("active-interaction:highVolumeExpansion",values);
        Assert.DoesNotContain("active-interaction:lowVolumeCompression",values);
        Assert.Contains("cross-market-breadth:positive",values);
        Assert.Contains("cross-market-pattern:aligned",values);
    }

    [Fact]
    public void MissingRegimeFeaturesAreOmittedRatherThanCalledNeutral()
    {
        var values=ActionabilityContextBucketEncoder.Encode(new Dictionary<string,decimal>());
        Assert.DoesNotContain(values,x=>x.StartsWith("volatility-regime:",StringComparison.Ordinal));
        Assert.DoesNotContain(values,x=>x.StartsWith("volume-regime:",StringComparison.Ordinal));
        Assert.DoesNotContain(values,x=>x.StartsWith("cross-market-",StringComparison.Ordinal));
        Assert.DoesNotContain(values,x=>x.StartsWith("trend-",StringComparison.Ordinal));
    }
}
