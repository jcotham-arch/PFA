namespace PFA_FVG_Scanner.Domain.Patterns;

public sealed class MarketPatternModuleRegistry : IMarketPatternModuleRegistry
{
    private static readonly IReadOnlyList<PatternModuleDefinition> Definitions =
    [
        new("fvg", "Fair Value Gaps", "legacy-compatibility",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "5m" },
            "Legacy operational · universal adapter scheduled for Phase 5",
            "Existing FVG detection, lifecycle, replay, scenario and evidence behavior is preserved unchanged.")
    ];

    public IReadOnlyList<PatternModuleDefinition> GetAll() => Definitions;

    public PatternModuleDefinition? Find(string moduleId) => Definitions.FirstOrDefault(x =>
        x.ModuleId.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
}
