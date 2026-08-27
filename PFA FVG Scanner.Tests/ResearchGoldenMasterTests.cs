namespace PFA_FVG_Scanner.Tests;

public sealed class ResearchGoldenMasterTests
{
    [Fact]
    public void DiscoveryExcludesIneligibleRecordsAndPreservesFixedRiskMath()
    {
        var records = Enumerable.Range(0, 5)
            .Select(i => TestData.Feature(0, i, i < 3 ? 1m : -1m)).ToList();
        records.Add(TestData.Feature(0, 99, 100m, included: false));

        var report = new FvgCandidateRuleDiscoveryService().Discover(records);
        var baseline = report.RankedCandidates.Single(x =>
            x.EntryModel == MesEntryModel.BoundaryTouch && x.TargetR == 1m &&
            x.Direction is null && x.SessionBucket is null &&
            x.MinimumGapSizePoints is null && x.MinimumMinutesToEntry is null &&
            x.MinimumRiskTicks is null);

        Assert.Equal(5, report.LearningRecordsEvaluated);
        Assert.Equal((5, 3, 2, 1m, .2m),
            (baseline.Trades, baseline.Wins, baseline.Losses, baseline.NetR, baseline.ExpectancyR));
        Assert.Equal((25m, 5m, 50m, 10m),
            (baseline.FixedRisk25NetProfitLoss, baseline.FixedRisk25AverageProfitLoss,
             baseline.FixedRisk50NetProfitLoss, baseline.FixedRisk50AverageProfitLoss));
        Assert.True(baseline.RequiresOutOfSampleValidation);
    }

    [Fact]
    public void DiscoveryIsDeterministicAfterRemovingGeneratedRuleIds()
    {
        var records = Enumerable.Range(0, 8)
            .Select(i => TestData.Feature(0, i, i % 3 == 0 ? -1m : 1m)).ToArray();
        var service = new FvgCandidateRuleDiscoveryService();
        var first = service.Discover(records).RankedCandidates
            .Select(SignatureAndMetrics).ToArray();
        var second = service.Discover(records).RankedCandidates
            .Select(SignatureAndMetrics).ToArray();
        Assert.Equal(first, second);
    }

    [Fact]
    public void CrossDayMatchesRuleSignaturesAcrossDatesAndNeverActivates()
    {
        var inputs = Enumerable.Range(0, 3).Select(day => new FvgCrossDayEvidenceInput
        {
            TradingDateUtc = TestData.BaseTime.Date.AddDays(day),
            Symbol = "MES",
            CandidateDiscovery = Report(Rule(8, 6, 2, 4m, .5m))
        }).ToArray();
        var report = new FvgCrossDayEvidenceService().Analyze(inputs);

        Assert.Equal(1, report.UniqueRulesObserved);
        var evidence = Assert.Single(report.AllRules);
        Assert.Equal(3, evidence.DaysObserved);
        Assert.Equal(24, evidence.TotalDistinctFvgs);
        Assert.Equal(FvgCrossDayEvidenceStatus.PersistentCandidate, evidence.Status);
        Assert.True(evidence.CanAdvanceToFrozenValidation);
        Assert.False(evidence.CanActivateStrategy);
        Assert.False(report.CanActivateAnyStrategy);
    }

    [Fact]
    public void PersistentNegativeRulesAreRetained()
    {
        var inputs = Enumerable.Range(0, 3).Select(day => new FvgCrossDayEvidenceInput
        {
            TradingDateUtc = TestData.BaseTime.Date.AddDays(day),
            Symbol = "MES",
            CandidateDiscovery = Report(Rule(8, 1, 7, -6m, -.75m))
        }).ToArray();
        var report = new FvgCrossDayEvidenceService().Analyze(inputs);
        var negative = Assert.Single(report.PersistentNegativeRules);
        Assert.Equal(FvgCrossDayEvidenceStatus.PersistentNegative, negative.Status);
        Assert.Equal(3, negative.NegativeDays);
        Assert.Equal(24, negative.TotalTrades);
    }

    [Fact]
    public void CandidateFreezeCopiesDefinitionAndDiscoveryEvidence()
    {
        var original = Rule(12, 8, 4, 4m, .333m);
        original.RuleName = "frozen-rule";
        original.Direction = FvgDirection.Bearish;
        original.MinimumGapSizePoints = 1m;
        original.MaximumGapSizePoints = 2m;
        original.EngineVersion = "1.0.0";
        var frozen = new FvgOutOfSampleValidationService().FreezeCandidate(original);
        original.RuleName = "mutated";
        original.MinimumGapSizePoints = 99m;

        Assert.Equal("frozen-rule", frozen.CandidateName);
        Assert.Equal(FvgDirection.Bearish, frozen.Direction);
        Assert.Equal(1m, frozen.MinimumGapSizePoints);
        Assert.Equal("1.0.0", frozen.SourceEngineVersion);
    }

    [Fact]
    public void ValidationEnforcesDateSeparationGatesAndNonActivation()
    {
        var candidate = new FrozenFvgCandidate
        {
            EntryModel = MesEntryModel.BoundaryTouch,
            TargetR = 1m,
            DiscoveryExpectancyR = .5m,
            DiscoveryWinRate = 70m
        };
        var records = Enumerable.Range(0, 5)
            .SelectMany(day => Enumerable.Range(0, 4)
                .Select(i => TestData.Feature(day, i, i < 3 ? 1m : -1m))).ToList();
        records.Add(TestData.Feature(-1, 99, -100m));
        records.Add(TestData.Feature(6, 99, -100m));

        var report = new FvgOutOfSampleValidationService().Validate(candidate, records,
            TestData.BaseTime.Date, TestData.BaseTime.Date.AddDays(4).AddHours(23));

        Assert.Equal(20, report.MatchingTrades);
        Assert.Equal(20, report.DistinctFvgs);
        Assert.True(report.PassedSampleGate);
        Assert.True(report.PassedDayCountGate);
        Assert.True(report.PassedExpectancyGate);
        Assert.True(report.PassedProfitFactorGate);
        Assert.True(report.PassedPositiveDaysGate);
        Assert.True(report.PassedDrawdownGate);
        Assert.True(report.PassedAllPromotionGates);
        Assert.Equal(ValidationDecision.PassedValidation, report.Decision);
        Assert.False(report.CanActivateStrategy);
        Assert.Equal("SandboxPromotionReview", report.NextRequiredStage);
    }

    [Fact]
    public void InsufficientValidationCannotAdvanceOrActivate()
    {
        var candidate = new FrozenFvgCandidate
        {
            EntryModel = MesEntryModel.BoundaryTouch,
            TargetR = 1m,
            DiscoveryExpectancyR = .5m
        };
        var report = new FvgOutOfSampleValidationService().Validate(candidate,
            new[] { TestData.Feature(0, 0, 1m) },
            TestData.BaseTime.Date, TestData.BaseTime.Date.AddDays(1));
        Assert.False(report.PassedSampleGate);
        Assert.False(report.PassedDayCountGate);
        Assert.False(report.PassedAllPromotionGates);
        Assert.Equal(ValidationDecision.InsufficientEvidence, report.Decision);
        Assert.False(report.CanActivateStrategy);
        Assert.Equal("OutOfSampleValidation", report.NextRequiredStage);
    }

    private static string SignatureAndMetrics(FvgCandidateRule x) => string.Join('|',
        x.EntryModel, x.TargetR, x.Direction, x.SessionBucket,
        x.MinimumGapSizePoints, x.MaximumGapSizePoints,
        x.MinimumMinutesToEntry, x.MaximumMinutesToEntry,
        x.MinimumRiskTicks, x.MaximumRiskTicks,
        x.Trades, x.NetR, x.ExpectancyR, x.Status, x.ResearchScore);

    private static FvgCandidateDiscoveryReport Report(FvgCandidateRule rule) => new()
    {
        RankedCandidates = new[] { rule }
    };

    private static FvgCandidateRule Rule(int trades, int wins, int losses, decimal netR, decimal expectancy) => new()
    {
        RuleName = "same semantic rule",
        EntryModel = MesEntryModel.BoundaryTouch,
        TargetR = 1m,
        Trades = trades,
        DistinctFvgs = trades,
        Wins = wins,
        Losses = losses,
        NetR = netR,
        ExpectancyR = expectancy,
        AverageWinnerR = 1m,
        AverageLoserR = -1m,
        ProfitFactorR = wins == 0 ? 0m : (decimal)wins / Math.Max(1, losses),
        MaximumDrawdownR = losses,
        RawNetProfitLoss = netR * 10m,
        FixedRisk25NetProfitLoss = netR * 25m,
        FixedRisk50NetProfitLoss = netR * 50m,
        Status = expectancy > 0 ? CandidateRuleStatus.PromisingCandidate : CandidateRuleStatus.NegativeExpectancy
    };
}
