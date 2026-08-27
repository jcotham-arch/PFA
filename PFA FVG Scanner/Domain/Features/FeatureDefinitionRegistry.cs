namespace PFA_FVG_Scanner.Domain.Features;

public sealed class FeatureDefinitionRegistry : IFeatureDefinitionRegistry
{
    public const string Version = "1.0.0";
    private static readonly IReadOnlyList<FeatureDefinition> Definitions =
    [
        Define("market.close", "Close", FeatureValueType.Decimal, "price", FeatureRole.MarketFact, "canonical bar", TimeSpan.Zero, "Latest closed price known at the snapshot."),
        Define("market.range", "Bar Range", FeatureValueType.Decimal, "points", FeatureRole.MarketFact, "canonical bar", TimeSpan.Zero, "High minus low for the latest bar."),
        Define("market.bar_count", "Available Bar Count", FeatureValueType.Integer, "bars", FeatureRole.MarketFact, "canonical bars", TimeSpan.Zero, "Bars available without looking past AsOfUtc."),
        Define("fvg.direction", "FVG Direction", FeatureValueType.Text, "enum", FeatureRole.Predictor, "legacy FVG", TimeSpan.Zero, "Direction known when the FVG confirms."),
        Define("fvg.gap_size_points", "FVG Gap Size", FeatureValueType.Decimal, "points", FeatureRole.Predictor, "legacy FVG", TimeSpan.Zero, "Three-candle gap size known at confirmation."),
        Define("fvg.session_bucket", "Legacy UTC Session Bucket", FeatureValueType.Text, "enum", FeatureRole.Predictor, "legacy UTC compatibility session", TimeSpan.Zero, "Legacy UTC bucket, not an exchange session."),
        Define("scenario.entry_model", "Entry Model", FeatureValueType.Text, "enum", FeatureRole.Predictor, "frozen scenario definition", TimeSpan.Zero, "Scenario entry depth selected before execution."),
        Define("scenario.target_r", "Requested Target R", FeatureValueType.Decimal, "R", FeatureRole.Predictor, "frozen scenario definition", TimeSpan.Zero, "Requested target multiple."),
        Define("execution.minutes_to_entry", "Minutes To Entry", FeatureValueType.Integer, "minutes", FeatureRole.ExecutionFact, "entry fill", TimeSpan.Zero, "Known only when entry becomes executable."),
        Define("execution.risk_ticks", "Risk Ticks", FeatureValueType.Decimal, "ticks", FeatureRole.ExecutionFact, "entry and stop", TimeSpan.Zero, "Known at executable entry."),
        Define("outcome.realized_r", "Realized R", FeatureValueType.Decimal, "R", FeatureRole.OutcomeLabel, "resolved scenario outcome", TimeSpan.Zero, "Post-entry result; never a predictor."),
        Define("diagnostic.maximum_favorable_r", "Maximum Favorable R", FeatureValueType.Decimal, "R", FeatureRole.Diagnostic, "post-entry candles", TimeSpan.Zero, "Post-entry diagnostic; never a predictor.")
    ];

    public IReadOnlyList<FeatureDefinition> GetAll() => Definitions;
    public FeatureDefinition? Find(string id, string? version = null) => Definitions.FirstOrDefault(x =>
        string.Equals(x.FeatureDefinitionId, id, StringComparison.OrdinalIgnoreCase)
        && (version is null || string.Equals(x.Version, version, StringComparison.OrdinalIgnoreCase)));

    private static FeatureDefinition Define(string id, string name, FeatureValueType type,
        string unit, FeatureRole role, string input, TimeSpan lookback, string description) =>
        new(id, name, Version, type, unit, role, input, lookback, description);
}
