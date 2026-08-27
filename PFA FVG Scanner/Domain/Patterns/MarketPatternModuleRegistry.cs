namespace PFA_FVG_Scanner.Domain.Patterns;

public sealed class MarketPatternModuleRegistry : IMarketPatternModuleRegistry
{
    private static readonly IReadOnlyList<PatternModuleDefinition> Definitions =
    [
        new("fvg", "Fair Value Gaps", "legacy-1.0.0",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "5m" },
            "Universal adapter active · legacy algorithms preserved",
            "Pattern Module #1 maps canonical bars through the unchanged legacy FVG detector.")
    ];

    public IReadOnlyList<PatternModuleDefinition> GetAll() => Definitions;

    public PatternModuleDefinition? Find(string moduleId) => Definitions.FirstOrDefault(x =>
        x.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
}
