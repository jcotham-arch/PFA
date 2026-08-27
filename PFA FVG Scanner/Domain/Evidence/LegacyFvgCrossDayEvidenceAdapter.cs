using System.Text.Json;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Domain.Evidence;

public static class LegacyFvgCrossDayEvidenceAdapter
{
    public static GeneralCrossDayEvidenceReport Map(FvgCrossDayEvidenceReport legacy,
        IReadOnlyList<DateOnly> expectedTradingDates, string sessionAssignmentVersion, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        var start = DateOnly.FromDateTime(legacy.StartDateUtc);
        var end = DateOnly.FromDateTime(legacy.EndDateUtc);
        var expected = expectedTradingDates.Distinct().Order().ToArray();
        var signatures = legacy.AllRules.Select(x => MapRule(x, expected)).ToArray();
        var id = $"LEGACY-CROSS-DAY|{legacy.Symbol}|{start:yyyy-MM-dd}|{end:yyyy-MM-dd}|{legacy.EngineVersion}";
        return new(id, legacy.Symbol, legacy.EngineVersion, sessionAssignmentVersion, start, end,
            expected, signatures, "FvgCrossDayEvidenceReport", createdAtUtc, false);
    }

    private static CrossDaySignatureEvidence MapRule(FvgCrossDayRuleEvidence rule,
        IReadOnlyList<DateOnly> expected)
    {
        var daily = rule.DailyResults.Select(x => new CrossDayDailyEvidence(
            DateOnly.FromDateTime(x.TradingDateUtc), x.Trades, x.DistinctFvgs,
            new Dictionary<string, decimal>
            {
                ["net-r"] = x.NetR, ["expectancy-r"] = x.ExpectancyR,
                ["profit-factor-r"] = x.ProfitFactorR, ["maximum-drawdown-r"] = x.MaximumDrawdownR,
                ["win-rate"] = x.WinRate
            }, x.OriginalDailyStatus.ToString(), new HashSet<string> { "legacy-regime-unclassified" })).ToArray();
        var observed = daily.Select(x => x.TradingDate).ToHashSet();
        return new(rule.RuleSignature, "legacy-fvg-candidate", "1.0.0",
            JsonSerializer.Serialize(new { rule.EntryModel, rule.TargetR, rule.Direction, rule.SessionBucket,
                rule.MinimumGapSizePoints, rule.MaximumGapSizePoints, rule.MinimumMinutesToEntry,
                rule.MaximumMinutesToEntry, rule.MinimumRiskTicks, rule.MaximumRiskTicks }),
            Enum.Parse<CrossDayEvidenceClassification>(rule.Status.ToString()), rule.TotalDaysInDataset,
            rule.DaysObserved, expected.Where(x => !observed.Contains(x)).ToArray(), rule.PositiveDays,
            rule.NegativeDays, rule.FlatDays, rule.TotalTrades, rule.TotalDistinctFvgs,
            new Dictionary<string, decimal>
            {
                ["net-r"] = rule.NetR, ["expectancy-r"] = rule.ExpectancyR,
                ["average-daily-expectancy-r"] = rule.AverageDailyExpectancyR,
                ["profit-factor-r"] = rule.ProfitFactorR, ["cross-day-maximum-drawdown-r"] = rule.CrossDayMaximumDrawdownR,
                ["expectancy-standard-deviation"] = rule.ExpectancyStandardDeviation,
                ["persistence-score"] = rule.PersistenceScore
            }, new HashSet<string> { "legacy-regime-unclassified" },
            new Dictionary<string, bool>
            {
                ["day-coverage"] = rule.PassedDayCoverageGate, ["sample"] = rule.PassedSampleGate,
                ["positive-days"] = rule.PassedPositiveDaysGate, ["expectancy"] = rule.PassedExpectancyGate,
                ["profit-factor"] = rule.PassedProfitFactorGate, ["persistence"] = rule.PassedPersistenceGates
            }, rule.CanAdvanceToFrozenValidation, daily, false);
    }
}
