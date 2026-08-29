namespace PFA_FVG_Scanner.Services;

public static class ActionabilityContextBucketEncoder
{
    public const string Version="actionability-context-buckets-1.0.0";

    public static IReadOnlyList<string> Encode(IReadOnlyDictionary<string,decimal> features)
    {
        var buckets=new HashSet<string>(StringComparer.Ordinal);
        var close=features.GetValueOrDefault("market.closeLocation");
        buckets.Add($"close-location:{(close<.25m?"lower":close>.75m?"upper":"middle")}");

        var volatility=State(features,"context.regime.volatility.");
        var volume=State(features,"context.regime.volume.");
        var auction=State(features,"context.regime.auction.");
        var momentum=State(features,"context.momentum.direction.");
        Add(buckets,"volatility-regime",volatility);
        Add(buckets,"volume-regime",volume);
        Add(buckets,"auction-regime",auction);
        Add(buckets,"momentum",momentum);
        if(volatility is not null&&volume is not null)buckets.Add($"volatility-volume:{volatility}+{volume}");
        if(auction is not null&&momentum is not null)buckets.Add($"auction-momentum:{auction}+{momentum}");

        foreach(var feature in features.Where(x=>x.Key.StartsWith("context.interaction.",StringComparison.Ordinal)&&x.Value==1))
            buckets.Add($"active-interaction:{feature.Key["context.interaction.".Length..]}");
        return buckets.Order(StringComparer.Ordinal).ToArray();
    }

    private static string? State(IReadOnlyDictionary<string,decimal> features,string prefix)=>features
        .Where(x=>x.Key.StartsWith(prefix,StringComparison.Ordinal)&&x.Value==1)
        .Select(x=>x.Key[prefix.Length..]).Order(StringComparer.Ordinal).FirstOrDefault();
    private static void Add(HashSet<string> values,string family,string? state)
    {if(state is not null)values.Add($"{family}:{state}");}
}
