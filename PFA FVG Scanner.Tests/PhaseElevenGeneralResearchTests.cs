using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseElevenGeneralResearchTests
{
    [Fact]
    public void LegacyAdapterPreservesCountRankingStatusesAndIndependentEvents()
    {
        var negative = Candidate("negative", CandidateRuleStatus.NegativeExpectancy, -0.2m, 10, 8);
        var positive = Candidate("positive", CandidateRuleStatus.PromisingCandidate, 0.4m, 20, 15);
        var insufficient = Candidate("small", CandidateRuleStatus.InsufficientEvidence, 0, 2, 2);
        var report = Report([positive, negative, insufficient]);
        var run = LegacyFvgResearchAdapter.Map(report, Dataset(), TestData.BaseTime);

        Assert.Equal(report.CandidateRulesTested, run.SearchSpace.DeclaredCandidateCount);
        Assert.Equal(report.RankedCandidates.Select(x => $"FvgCandidateRule:{x.RuleId}"),
            run.Hypotheses.Select(x => x.SourceReference));
        Assert.Equal(new[] { ResearchHypothesisStatus.Positive, ResearchHypothesisStatus.Negative,
            ResearchHypothesisStatus.InsufficientEvidence }, run.Hypotheses.Select(x => x.Status));
        Assert.Equal(report.DistinctFvgsEvaluated, run.Population.IndependentEvents);
        Assert.False(run.CanActivateStrategy);
    }

    [Fact]
    public void AdapterIsDeterministicForSameDatasetAndSearchSpace()
    {
        var report = Report([Candidate("one", CandidateRuleStatus.ResearchCandidate, 0.1m, 10, 9)]);
        var first = LegacyFvgResearchAdapter.Map(report, Dataset(), TestData.BaseTime);
        var second = LegacyFvgResearchAdapter.Map(report, Dataset(), TestData.BaseTime.AddHours(1));
        Assert.Equal(first.ResearchRunId, second.ResearchRunId);
        Assert.Equal(first.Hypotheses[0].Signature, second.Hypotheses[0].Signature);
        Assert.Equal(first.ContentHash(), second.ContentHash());
    }

    [Fact]
    public async Task RepositoryRetainsNegativeAndEmptyCompletedRunsIdempotently()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        IGeneralResearchRepository repository = new GeneralResearchRepository(factory.Database);
        var run = LegacyFvgResearchAdapter.Map(
            Report([Candidate("negative", CandidateRuleStatus.NegativeExpectancy, -0.5m, 12, 10)]),
            Dataset(), TestData.BaseTime);
        await repository.SaveAsync(run, TestContext.Current.CancellationToken);
        await repository.SaveAsync(run, TestContext.Current.CancellationToken);
        var stored = await repository.FindAsync(run.ResearchRunId, TestContext.Current.CancellationToken);
        Assert.Equal(ResearchHypothesisStatus.Negative, Assert.Single(stored!.Hypotheses).Status);
        Assert.Equal(1, await Count(factory.Database, "GeneralResearchRuns"));

        var empty = LegacyFvgResearchAdapter.Map(Report([]), Dataset() with { DatasetId = "EMPTY", ContentHash = "EMPTY-HASH" }, TestData.BaseTime);
        await repository.SaveAsync(empty, TestContext.Current.CancellationToken);
        Assert.Empty((await repository.FindAsync(empty.ResearchRunId, TestContext.Current.CancellationToken))!.Hypotheses);
    }

    [Fact]
    public async Task RepositoryRejectsSearchSpaceOmissionAndActivationFlags()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new GeneralResearchRepository(factory.Database);
        var run = LegacyFvgResearchAdapter.Map(
            Report([Candidate("one", CandidateRuleStatus.ResearchCandidate, 0.1m, 10, 9)]), Dataset(), TestData.BaseTime);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(
            run with { Hypotheses = [] }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.SaveAsync(
            run with { CanActivateStrategy = true }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CompletedRunIsImmutableAndMultipleComparisonMetadataSurvivesRead()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var repository = new GeneralResearchRepository(factory.Database);
        var run = LegacyFvgResearchAdapter.Map(
            Report([Candidate("one", CandidateRuleStatus.ResearchCandidate, 0.1m, 10, 9)]), Dataset(), TestData.BaseTime);
        await repository.SaveAsync(run, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(
            run with { InputManifestJson = "{\"changed\":true}" }, TestContext.Current.CancellationToken));
        var stored = await repository.FindAsync(run.ResearchRunId, TestContext.Current.CancellationToken);
        Assert.Equal("legacy-none-recorded", stored!.SearchSpace.MultipleComparisonMethod);
    }

    private static FvgCandidateDiscoveryReport Report(IReadOnlyList<FvgCandidateRule> candidates) => new()
    {
        LearningRecordsEvaluated = candidates.Sum(x => x.Trades), DistinctFvgsEvaluated = candidates.Sum(x => x.DistinctFvgs),
        CandidateRulesTested = candidates.Count, MinimumSampleRequired = 5, RankedCandidates = candidates
    };
    private static FvgCandidateRule Candidate(string name, CandidateRuleStatus status, decimal expectancy,
        int trades, int distinct) => new()
    {
        RuleId = Guid.NewGuid(), RuleName = name, EntryModel = MesEntryModel.BoundaryTouch,
        TargetR = name.Length, Status = status, ExpectancyR = expectancy, NetR = expectancy * trades,
        ProfitFactorR = expectancy > 0 ? 1.5m : 0.7m, MaximumDrawdownR = 2, Trades = trades,
        DistinctFvgs = distinct
    };
    private static ResearchDatasetManifest Dataset() => new("DATASET-1", TestData.BaseTime,
        TestData.BaseTime.AddDays(30), "HASH-1", "REV-1", ["MES"], ["2025-01-06"]);
    private static async Task<int> Count(PfaDatabase database, string table)
    {
        await using SqliteConnection connection = database.CreateConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
