namespace PFA_FVG_Scanner.Domain.Sequences;

public interface IMarketSequenceDefinitionRegistry
{
    IReadOnlyList<MarketSequenceDefinition> GetAll();
    MarketSequenceDefinition? Find(string id, string? version = null);
}

public sealed class MarketSequenceDefinitionRegistry : IMarketSequenceDefinitionRegistry
{
    private static readonly IReadOnlyList<MarketSequenceDefinition> Definitions =
    [
        new("intraday-pattern-progression", "capture-1.0.0", "Intraday pattern progression",
            [new("initial-observation", Set("*")), new("follow-on-observation", Set("*"))],
            TimeSpan.FromMinutes(60)),
        new("liquidity-sweep-to-imbalance", "research-1.0.0", "Liquidity sweep to imbalance",
            [new("liquidity-event", Set("LiquiditySweep")), new("imbalance", Set("FairValueGap"))],
            TimeSpan.FromMinutes(60), true),
        new("liquidity-sweep-to-breakout", "research-1.0.0", "Liquidity sweep to range breakout",
            [new("liquidity-event", Set("LiquiditySweep")), new("range-expansion", Set("RangeBreakout"))],
            TimeSpan.FromMinutes(60), true),
        new("breakout-continuation", "research-1.0.0", "Breakout continuation",
            [new("initial-breakout", Set("RangeBreakout")), new("continuation-breakout", Set("RangeBreakout"))],
            TimeSpan.FromMinutes(45), true),
        new("breakout-failure", "research-1.0.0", "Breakout followed by failure",
            [new("initial-breakout", Set("RangeBreakout")), new("failed-auction", Set("FailedBreakout"))],
            TimeSpan.FromMinutes(45), false),
        new("failed-breakout-reversal", "research-1.0.0", "Failed breakout to opposing breakout",
            [new("failed-auction", Set("FailedBreakout")), new("opposing-expansion", Set("RangeBreakout"))],
            TimeSpan.FromMinutes(60), false)
    ];
    public IReadOnlyList<MarketSequenceDefinition> GetAll() => Definitions;
    public MarketSequenceDefinition? Find(string id, string? version = null) => Definitions.FirstOrDefault(x =>
        x.SequenceDefinitionId.Equals(id, StringComparison.OrdinalIgnoreCase) &&
        (version is null || x.Version.Equals(version, StringComparison.OrdinalIgnoreCase)));
    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
