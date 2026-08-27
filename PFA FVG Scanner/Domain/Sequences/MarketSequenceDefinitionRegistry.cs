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
            TimeSpan.FromMinutes(60))
    ];
    public IReadOnlyList<MarketSequenceDefinition> GetAll() => Definitions;
    public MarketSequenceDefinition? Find(string id, string? version = null) => Definitions.FirstOrDefault(x =>
        x.SequenceDefinitionId.Equals(id, StringComparison.OrdinalIgnoreCase) &&
        (version is null || x.Version.Equals(version, StringComparison.OrdinalIgnoreCase)));
    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
}
