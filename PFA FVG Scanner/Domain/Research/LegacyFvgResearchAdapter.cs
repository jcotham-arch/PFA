using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Domain.Research;

public static class LegacyFvgResearchAdapter
{
    public static GeneralResearchRun Map(FvgCandidateDiscoveryReport report, ResearchDatasetManifest dataset,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(report);
        var hypotheses = report.RankedCandidates.Select(MapCandidate).ToArray();
        var search = new ResearchSearchSpace("legacy-fvg-candidate-matrix", "1.0.0",
            JsonSerializer.Serialize(new { EntryModels = Enum.GetNames<MesEntryModel>(),
                TargetR = new[] { 1m, 1.5m, 2m, 3m, 4m }, LegacyCandidateCount = report.CandidateRulesTested }),
            report.CandidateRulesTested, "legacy-none-recorded", null);
        var population = new ResearchPopulation(report.LearningRecordsEvaluated,
            report.LearningRecordsEvaluated, report.DistinctFvgsEvaluated,
            new Dictionary<string, int> { ["legacy-exclusions-not-itemized"] = 0 }, "FvgId");
        var idMaterial = string.Join('|', dataset.DatasetId, dataset.ContentHash, search.SearchSpaceId,
            report.CandidateRulesTested, report.LearningRecordsEvaluated);
        var id = "RESEARCH-" + Hash(idMaterial);
        return new(id, "legacy-fvg-discovery-1.0.0", ResearchRunStatus.Completed, dataset, search,
            population, hypotheses, JsonSerializer.Serialize(new { report.MinimumSampleRequired,
                report.DatasetWarning }), createdAtUtc, createdAtUtc, CanActivateStrategy: false);
    }

    private static ResearchHypothesis MapCandidate(FvgCandidateRule rule)
    {
        var definition = new
        {
            rule.EntryModel, rule.TargetR, rule.Direction, rule.SessionBucket,
            rule.MinimumGapSizePoints, rule.MaximumGapSizePoints, rule.MinimumMinutesToEntry,
            rule.MaximumMinutesToEntry, rule.MinimumRiskTicks, rule.MaximumRiskTicks
        };
        var json = JsonSerializer.Serialize(definition);
        var signature = Hash(json);
        var status = rule.Status switch
        {
            CandidateRuleStatus.NegativeExpectancy => ResearchHypothesisStatus.Negative,
            CandidateRuleStatus.PromisingCandidate => ResearchHypothesisStatus.Positive,
            CandidateRuleStatus.ResearchCandidate or CandidateRuleStatus.RequiresValidation => ResearchHypothesisStatus.Candidate,
            _ => ResearchHypothesisStatus.InsufficientEvidence
        };
        return new("HYP-" + signature, signature, "legacy-fvg-candidate", json, status,
            rule.Trades, rule.DistinctFvgs,
            [new("expectancy-r", rule.ExpectancyR, "R"), new("net-r", rule.NetR, "R"),
             new("profit-factor-r", rule.ProfitFactorR, "ratio"),
             new("maximum-drawdown-r", rule.MaximumDrawdownR, "R")],
            $"FvgCandidateRule:{rule.RuleId}", false);
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
