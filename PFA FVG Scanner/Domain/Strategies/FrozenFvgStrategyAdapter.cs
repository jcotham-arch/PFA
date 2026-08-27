using System.Text.Json;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Domain.Strategies;

public static class FrozenFvgStrategyAdapter
{
    public static ImmutableStrategyDefinition Map(FrozenFvgCandidate candidate,
        StrategyEngineVersionManifest manifest, string discoveryDatasetId, string validationDatasetId)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var id = $"legacy-fvg-{candidate.CandidateId:N}";
        return new(id, "compatibility-1.0.0", "legacy-fvg-candidate", candidate.CandidateName,
            "Research", candidate.Direction?.ToString() ?? "Either",
            JsonSerializer.Serialize(new { candidate.EntryModel }),
            JsonSerializer.Serialize(new { Model = "LegacyFvgBoundary" }),
            JsonSerializer.Serialize(new { candidate.TargetR }), "{}", "{}",
            JsonSerializer.Serialize(new { NoTradeWhen = "Legacy frozen-candidate filters do not match" }),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MES" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [new("Pattern", "fvg", candidate.SourceEngineVersion, "setup", true)], [], manifest,
            discoveryDatasetId, validationDatasetId, "legacy-compatibility-adapter", candidate.FrozenAtUtc,
            $"FrozenFvgCandidate:{candidate.CandidateId}");
    }
}
