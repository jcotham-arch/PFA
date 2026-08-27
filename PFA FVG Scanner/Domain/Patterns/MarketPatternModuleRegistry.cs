namespace PFA_FVG_Scanner.Domain.Patterns;

public sealed class MarketPatternModuleRegistry : IMarketPatternModuleRegistry
{
    private static readonly IReadOnlyList<PatternModuleDefinition> Definitions =
    [
        new("fvg", "Fair Value Gaps", "legacy-1.0.0",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "5m" },
            "Universal adapter active · legacy algorithms preserved",
            "Pattern Module #1 maps canonical bars through the unchanged legacy FVG detector."),
        new("liquidity-sweep", "Liquidity Sweeps", "capture-1.0.0",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1m", "5m", "15m", "1h" },
            "Capture/Research active · no strategy judgment",
            "Point-in-time prior-level penetration with explicit reclaim, continuation, depth and equal-level facts."),
        Planned("market-structure", "Market Structure", "Swing points, trend state, structure breaks and directional transitions."),
        Planned("displacement", "Displacement", "Range expansion and directional impulse with point-in-time confirmation."),
        Planned("range-breakout", "Range Breakouts", "Versioned range formation, acceptance, rejection and continuation."),
        Planned("failed-breakout", "Failed Breakouts", "Break, failure and reclaim behavior without optimistic hindsight."),
        Planned("session-reference", "Session References", "Prior session levels, opening ranges and session-transition events."),
        Planned("volume-volatility", "Volume & Volatility", "Volume anomalies, compression, expansion and regime context.")
    ];

    public IReadOnlyList<PatternModuleDefinition> GetAll() => Definitions;

    public PatternModuleDefinition? Find(string moduleId) => Definitions.FirstOrDefault(x =>
        x.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));

    private static PatternModuleDefinition Planned(string id, string name, string description) =>
        new(id, name, "definition-pending",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1m", "5m", "15m", "1h" },
            "Planned · detector not active · no evidence emitted", description);
}
