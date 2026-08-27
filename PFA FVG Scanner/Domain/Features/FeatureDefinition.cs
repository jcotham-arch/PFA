using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Features;

public enum FeatureValueType { Decimal, Integer, Boolean, Text, Timestamp }
public enum FeatureRole { MarketFact, Predictor, ExecutionFact, OutcomeLabel, Diagnostic }

public sealed record FeatureDefinition(
    string FeatureDefinitionId,
    string Name,
    string Version,
    FeatureValueType ValueType,
    string Unit,
    FeatureRole Role,
    string InputRequirement,
    TimeSpan Lookback,
    string Description);

public sealed record FeatureValue(
    string FeatureValueId,
    string FeatureDefinitionId,
    string FeatureDefinitionVersion,
    string SubjectId,
    string InstrumentId,
    DateTime AsOfUtc,
    DateTime KnownAtUtc,
    string Value,
    string EngineVersion,
    string DataRevision,
    MarketDataQualityFlags QualityFlags,
    IReadOnlyList<string> SourceReferences)
{
    public bool IsAvailableAt(DateTime decisionTimeUtc) => KnownAtUtc <= EnsureUtc(decisionTimeUtc);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };
}

public interface IFeatureDefinitionRegistry
{
    IReadOnlyList<FeatureDefinition> GetAll();
    FeatureDefinition? Find(string id, string? version = null);
}
