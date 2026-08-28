using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Sequences;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseSevenSequenceIntelligenceTests
{
    private static readonly MarketSequenceDefinition Definition = new("test-sequence", "1.0.0", "Test",
        [new("formation", Set("Formation")), new("sweep", Set("Sweep")), new("reclaim", Set("Reclaim"))],
        TimeSpan.FromMinutes(30));

    [Fact]
    public void ReplayIsOrderedDeterministicAndPointInTimeSafe()
    {
        var engine = Engine();
        var observations = new[] { Observation("C", "Reclaim", 20), Observation("A", "Formation", 0),
            Observation("B", "Sweep", 10) };
        var beforeReclaim = engine.Replay(Definition, observations, TestData.BaseTime.AddMinutes(15));
        var complete = engine.Replay(Definition, observations, TestData.BaseTime.AddMinutes(25));
        var repeated = engine.Replay(Definition, observations.Reverse().ToArray(), TestData.BaseTime.AddMinutes(25));

        Assert.DoesNotContain(beforeReclaim, x => x.State == MarketSequenceState.Successful);
        var successful = Assert.Single(complete, x => x.State == MarketSequenceState.Successful);
        Assert.Equal(new[] { "A", "B", "C" }, successful.Members.Select(x => x.ObservationId));
        Assert.Equal(successful.SequenceInstanceId,
            Assert.Single(repeated, x => x.State == MarketSequenceState.Successful).SequenceInstanceId);
    }

    [Fact]
    public void OverlappingStartsAreRetainedIndependently()
    {
        var definition = new MarketSequenceDefinition("overlap", "1", "Overlap",
            [new("start", Set("Formation")), new("finish", Set("Sweep"))], TimeSpan.FromMinutes(30));
        var results = Engine().Replay(definition,
            [Observation("A1", "Formation", 0), Observation("A2", "Formation", 2), Observation("B", "Sweep", 5)],
            TestData.BaseTime.AddMinutes(6));
        Assert.Equal(2, results.Count(x => x.State == MarketSequenceState.Successful));
        Assert.Equal(2, results.Select(x => x.SequenceInstanceId).Distinct().Count());
    }

    [Fact]
    public void TimeoutFailurePartialAndExplicitTerminationRemainVisible()
    {
        var timeout = Engine().Replay(Definition, [Observation("A", "Formation", 0)],
            TestData.BaseTime.AddMinutes(31));
        Assert.Equal(MarketSequenceState.Failed, Assert.Single(timeout).State);
        Assert.Equal("transition-timeout", Assert.Single(timeout).TerminationReason);

        var partial = Engine().Replay(Definition, [Observation("A", "Formation", 0)],
            TestData.BaseTime.AddMinutes(10));
        Assert.Equal(MarketSequenceState.Partial, Assert.Single(partial).State);

        var terminating = Definition with { TerminationPatternTypes = Set("Invalidation") };
        var terminated = Engine().Replay(terminating,
            [Observation("A", "Formation", 0), Observation("X", "Invalidation", 5)],
            TestData.BaseTime.AddMinutes(6));
        Assert.Equal(MarketSequenceState.Terminated, Assert.Single(terminated).State);
    }

    [Fact]
    public void SessionBoundaryTerminatesRatherThanJoiningAcrossDays()
    {
        var results = Engine().Replay(Definition,
            [Observation("A", "Formation", 0), Observation("B", "Sweep", 24 * 60 + 1)],
            TestData.BaseTime.AddDays(1).AddMinutes(2));
        var first = Assert.Single(results);
        Assert.Equal(MarketSequenceState.Terminated, first.State);
        Assert.Equal("session-ended", first.TerminationReason);
        Assert.Single(first.Members);
    }

    [Fact]
    public async Task PersistenceIsIdempotentAndPreservesMembersAndTransitions()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        var instance = Assert.Single(Engine().Replay(Definition,
            [Observation("A", "Formation", 0), Observation("B", "Sweep", 5), Observation("C", "Reclaim", 10)],
            TestData.BaseTime.AddMinutes(11)), x => x.State == MarketSequenceState.Successful);
        var repository = new MarketSequenceRepository(factory.Database);
        await repository.SaveManyAsync([(Definition, instance), (Definition, instance)],
            TestContext.Current.CancellationToken);
        await repository.SaveManyAsync([(Definition, instance)], TestContext.Current.CancellationToken);
        Assert.Equal(1, await Count(factory.Database, "MarketSequenceInstances"));
        Assert.Equal(3, await Count(factory.Database, "MarketSequenceMembers"));
        Assert.Equal(2, await Count(factory.Database, "MarketSequenceTransitions"));
    }

    [Fact]
    public void BuiltInDefinitionTreatsEveryPatternTypeEqually()
    {
        var registry = new MarketSequenceDefinitionRegistry();
        var definition = registry.Find("intraday-pattern-progression");
        Assert.NotNull(definition);
        Assert.All(definition.Stages, x => Assert.Contains("*", x.AcceptedPatternTypes));
        Assert.DoesNotContain("fvg", definition.SequenceDefinitionId, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(registry.GetAll(), x => x.SequenceDefinitionId == "liquidity-sweep-to-breakout");
        Assert.Contains(registry.GetAll(), x => x.SequenceDefinitionId == "breakout-failure");
        Assert.All(registry.GetAll(), x => Assert.True(x.Stages.Count >= 2));
    }

    [Fact]
    public void NamedSweepToBreakoutSequenceRequiresMatchingDirection()
    {
        var definition = new MarketSequenceDefinitionRegistry().Find("liquidity-sweep-to-breakout");
        Assert.NotNull(definition);
        var matching = Engine().Replay(definition,
            [Observation("SWEEP", "LiquiditySweep", 0), Observation("BREAK", "RangeBreakout", 5)],
            TestData.BaseTime.AddMinutes(6));
        Assert.Contains(matching, x => x.State == MarketSequenceState.Successful);

        var opposing = Engine().Replay(definition,
            [Observation("SWEEP-2", "LiquiditySweep", 0),
             Observation("BREAK-2", "RangeBreakout", 5, PatternDirection.Bearish)],
            TestData.BaseTime.AddMinutes(6));
        Assert.DoesNotContain(opposing, x => x.State == MarketSequenceState.Successful);
    }

    private static IMarketSequenceEngine Engine() => new MarketSequenceEngine(new LegacyUtcTradingSessionService());
    private static IReadOnlySet<string> Set(params string[] values) =>
        new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    private static UniversalMarketObservation Observation(string id, string type, int minute,
        PatternDirection direction = PatternDirection.Bullish) =>
        new(id, 1, type.ToLowerInvariant(), "1.0.0", type, "MES", null, "5m", direction,
            TestData.BaseTime.AddMinutes(minute), TestData.BaseTime.AddMinutes(minute),
            PatternLifecycleState.Detected, "test/1", "{}", [], MarketDataQualityFlags.None, $"HASH-{id}");
    private static async Task<int> Count(PfaDatabase database, string table)
    {
        await using SqliteConnection connection = database.CreateConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
