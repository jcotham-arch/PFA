using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Research;

public enum ResearchRunStatus { Pending, Running, Completed, Failed }
public enum ResearchHypothesisStatus { InsufficientEvidence, Candidate, Positive, Negative, Unstable }

public sealed record ResearchDatasetManifest(
    string DatasetId,
    DateTime StartUtc,
    DateTime EndUtc,
    string ContentHash,
    string DataRevision,
    IReadOnlyList<string> InstrumentIds,
    IReadOnlyList<string> TradingDates);

public sealed record ResearchSearchSpace(
    string SearchSpaceId,
    string Version,
    string DefinitionJson,
    int DeclaredCandidateCount,
    string MultipleComparisonMethod,
    int? RandomSeed);

public sealed record ResearchPopulation(
    int RecordsAvailable,
    int RecordsIncluded,
    int IndependentEvents,
    IReadOnlyDictionary<string, int> ExclusionsByReason,
    string IndependentEventKey);

public sealed record ResearchMetric(string Name, decimal Value, string Unit);

public sealed record ResearchHypothesis(
    string HypothesisId,
    string Signature,
    string FamilyId,
    string DefinitionJson,
    ResearchHypothesisStatus Status,
    int SampleSize,
    int IndependentEvents,
    IReadOnlyList<ResearchMetric> Metrics,
    string SourceReference,
    bool CanActivateStrategy = false);

public sealed record GeneralResearchRun(
    string ResearchRunId,
    string ResearchEngineVersion,
    ResearchRunStatus Status,
    ResearchDatasetManifest Dataset,
    ResearchSearchSpace SearchSpace,
    ResearchPopulation Population,
    IReadOnlyList<ResearchHypothesis> Hypotheses,
    string InputManifestJson,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? FailureReason = null,
    bool CanActivateStrategy = false)
{
    public string ContentHash()
    {
        var canonical = JsonSerializer.Serialize(new
        {
            ResearchRunId, ResearchEngineVersion, Status, Dataset, SearchSpace, Population,
            Hypotheses = Hypotheses.OrderBy(x => x.Signature, StringComparer.Ordinal).ToArray(),
            InputManifestJson, FailureReason, CanActivateStrategy
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public interface IGeneralResearchRepository
{
    Task SaveAsync(GeneralResearchRun run, CancellationToken cancellationToken = default);
    Task<GeneralResearchRun?> FindAsync(string researchRunId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GeneralResearchRun>> GetRecentAsync(int limit = 50,
        CancellationToken cancellationToken = default);
}
