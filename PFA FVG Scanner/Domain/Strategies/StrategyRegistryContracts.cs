using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Strategies;

public enum StrategyRegistryStatus
{
    Draft,
    FrozenResearch,
    ValidationPending,
    ValidationComplete,
    Rejected,
    SandboxEligible,
    SandboxActive,
    Suspended,
    LivePilotEligible,
    LivePilotActive
}

public enum StrategyDecisionType { NoTrade, TradeProposal }

public sealed record StrategyDecision(
    StrategyDecisionType Decision,
    string StrategyId,
    string StrategyVersion,
    DateTime AsOfUtc,
    string Reason,
    string EvidenceJson);

public sealed record StrategyEngineVersionManifest(
    string DataVersion,
    string FeatureEngineVersion,
    string PatternDetectorVersion,
    string SequenceEngineVersion,
    string StrategyEngineVersion,
    string ExecutionModelVersion,
    string ResearchEngineVersion,
    string SessionModelVersion,
    string ContractResolverVersion)
{
    public void Validate()
    {
        if (GetType().GetProperties().Any(x => string.IsNullOrWhiteSpace((string?)x.GetValue(this))))
            throw new ArgumentException("Every engine manifest version is required.");
    }
}

public sealed record StrategyRequirement(
    string RequirementType,
    string ReferenceId,
    string ReferenceVersion,
    string Role,
    bool IsRequired);

public sealed record StrategyEvidenceLink(
    string EvidenceType,
    string EvidenceId,
    string DatasetId,
    DateTime KnownAtUtc);

public sealed record ImmutableStrategyDefinition(
    string StrategyId,
    string StrategyVersion,
    string FamilyId,
    string DisplayName,
    string Environment,
    string DirectionPolicy,
    string EntryDefinitionJson,
    string StopDefinitionJson,
    string TargetDefinitionJson,
    string ManagementDefinitionJson,
    string RiskDefinitionJson,
    string AbstentionDefinitionJson,
    HashSet<string> SupportedInstrumentIds,
    HashSet<string> SupportedSessionIds,
    IReadOnlyList<StrategyRequirement> Requirements,
    IReadOnlyList<StrategyEvidenceLink> EvidenceLinks,
    StrategyEngineVersionManifest EngineManifest,
    string DiscoveryDatasetId,
    string ValidationDatasetId,
    string Author,
    DateTime CreatedAtUtc,
    string? CompatibilitySource = null)
{
    public string ContentHash()
    {
        EngineManifest.Validate();
        if (string.IsNullOrWhiteSpace(StrategyId) || string.IsNullOrWhiteSpace(StrategyVersion) ||
            string.IsNullOrWhiteSpace(FamilyId) || string.IsNullOrWhiteSpace(DisplayName) ||
            string.IsNullOrWhiteSpace(AbstentionDefinitionJson))
            throw new ArgumentException("Strategy identity, display name, and abstention definition are required.");
        var canonical = JsonSerializer.Serialize(new
        {
            StrategyId, StrategyVersion, FamilyId, DisplayName, Environment, DirectionPolicy,
            EntryDefinitionJson, StopDefinitionJson, TargetDefinitionJson, ManagementDefinitionJson,
            RiskDefinitionJson, AbstentionDefinitionJson,
            SupportedInstrumentIds = SupportedInstrumentIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            SupportedSessionIds = SupportedSessionIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            Requirements = Requirements.OrderBy(x => x.RequirementType, StringComparer.Ordinal)
                .ThenBy(x => x.ReferenceId, StringComparer.Ordinal).ThenBy(x => x.Role, StringComparer.Ordinal).ToArray(),
            EvidenceLinks = EvidenceLinks.OrderBy(x => x.EvidenceType, StringComparer.Ordinal)
                .ThenBy(x => x.EvidenceId, StringComparer.Ordinal).ToArray(),
            EngineManifest, DiscoveryDatasetId, ValidationDatasetId, Author, CompatibilitySource
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed record StrategyRegistryEntry(
    ImmutableStrategyDefinition Definition,
    string ContentHash,
    StrategyRegistryStatus Status,
    DateTime StatusChangedAtUtc,
    string StatusReason);

public interface IStrategyRegistry
{
    Task<StrategyRegistryEntry> RegisterAsync(ImmutableStrategyDefinition definition,
        CancellationToken cancellationToken = default);
    Task<StrategyRegistryEntry?> FindAsync(string strategyId, string strategyVersion,
        CancellationToken cancellationToken = default);
    Task<StrategyRegistryEntry> TransitionAsync(string strategyId, string strategyVersion,
        StrategyRegistryStatus target, string reason, string actor,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StrategyRegistryEntry>> GetAllAsync(CancellationToken cancellationToken = default);
}
