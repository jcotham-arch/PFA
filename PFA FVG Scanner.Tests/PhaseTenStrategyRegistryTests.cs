using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Strategies;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseTenStrategyRegistryTests
{
    [Fact]
    public async Task RegistrationIsIdempotentAndDefinitionIsImmutable()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        IStrategyRegistry registry = new StrategyRegistryRepository(factory.Database);
        var definition = Definition();
        await registry.RegisterAsync(definition, TestContext.Current.CancellationToken);
        await registry.RegisterAsync(definition, TestContext.Current.CancellationToken);
        Assert.Equal(1, await Count(factory.Database, "StrategyDefinitions"));
        Assert.Equal(1, await Count(factory.Database, "StrategyLifecycleEvents"));

        var changed = definition with { TargetDefinitionJson = "{\"targetR\":3}" };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registry.RegisterAsync(changed, TestContext.Current.CancellationToken));
        Assert.Contains("new StrategyVersion", error.Message);
    }

    [Fact]
    public void ContentHashIsStableAcrossSetAndRequirementOrdering()
    {
        var first = Definition();
        var second = first with
        {
            SupportedInstrumentIds = new HashSet<string> { "MNQ", "MES" },
            Requirements = first.Requirements.Reverse().ToArray()
        };
        Assert.Equal(first.ContentHash(), second.ContentHash());
    }

    [Fact]
    public void EngineManifestMustBeComplete()
    {
        var invalid = Definition() with { EngineManifest = Manifest() with { SequenceEngineVersion = "" } };
        Assert.Throws<ArgumentException>(() => invalid.ContentHash());
    }

    [Fact]
    public async Task ResearchLifecycleCannotActivateSandboxOrLiveTrading()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        IStrategyRegistry registry = new StrategyRegistryRepository(factory.Database);
        var definition = Definition();
        await registry.RegisterAsync(definition, TestContext.Current.CancellationToken);
        await registry.TransitionAsync(definition.StrategyId, definition.StrategyVersion,
            StrategyRegistryStatus.FrozenResearch, "definition frozen", "test",
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.TransitionAsync(
            definition.StrategyId, definition.StrategyVersion, StrategyRegistryStatus.SandboxActive,
            "try activate", "test", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.TransitionAsync(
            definition.StrategyId, definition.StrategyVersion, StrategyRegistryStatus.LivePilotActive,
            "try activate", "test", TestContext.Current.CancellationToken));
        Assert.Equal(StrategyRegistryStatus.FrozenResearch,
            (await registry.FindAsync(definition.StrategyId, definition.StrategyVersion,
                TestContext.Current.CancellationToken))!.Status);
    }

    [Fact]
    public async Task AllowedResearchTransitionsAreAppendOnlyAndAuditable()
    {
        using var factory = await TestDatabaseFactory.CreateAsync();
        IStrategyRegistry registry = new StrategyRegistryRepository(factory.Database);
        var token = TestContext.Current.CancellationToken;
        var definition = Definition(); await registry.RegisterAsync(definition, token);
        await registry.TransitionAsync(definition.StrategyId, definition.StrategyVersion,
            StrategyRegistryStatus.FrozenResearch, "freeze", "researcher", token);
        await registry.TransitionAsync(definition.StrategyId, definition.StrategyVersion,
            StrategyRegistryStatus.ValidationPending, "queue validation", "researcher", token);
        await registry.TransitionAsync(definition.StrategyId, definition.StrategyVersion,
            StrategyRegistryStatus.ValidationComplete, "evidence captured", "validator", token);
        Assert.Equal(4, await Count(factory.Database, "StrategyLifecycleEvents"));
        Assert.Equal(StrategyRegistryStatus.ValidationComplete,
            (await registry.FindAsync(definition.StrategyId, definition.StrategyVersion, token))!.Status);
    }

    [Fact]
    public void NoTradeIsAFirstClassStrategyDecision()
    {
        var decision = new StrategyDecision(StrategyDecisionType.NoTrade, "S", "1", TestData.BaseTime,
            "Required sequence is absent", "{}");
        Assert.Equal(StrategyDecisionType.NoTrade, decision.Decision);
        Assert.NotEmpty(decision.Reason);
    }

    [Fact]
    public void FrozenFvgCandidateMapsAsCompatibilityInputNotPreferredCoreModel()
    {
        var frozen = new FrozenFvgCandidate
        {
            CandidateId = Guid.Parse("11111111-1111-1111-1111-111111111111"), CandidateName = "Legacy test",
            EntryModel = MesEntryModel.FiftyPercent, TargetR = 2, FrozenAtUtc = TestData.BaseTime,
            SourceEngineVersion = "1.0.0"
        };
        var mapped = FrozenFvgStrategyAdapter.Map(frozen, Manifest(), "DISCOVERY-1", "VALIDATION-1");
        Assert.Equal("legacy-fvg-candidate", mapped.FamilyId);
        Assert.Equal("FrozenFvgCandidate:11111111-1111-1111-1111-111111111111", mapped.CompatibilitySource);
        Assert.Contains("NoTradeWhen", mapped.AbstentionDefinitionJson);
        Assert.Contains(mapped.Requirements, x => x.ReferenceId == "fvg");
    }

    private static ImmutableStrategyDefinition Definition() => new("strategy-1", "1.0.0", "family-1",
        "Pattern progression research", "Research", "Either", "{}", "{}", "{\"targetR\":2}",
        "{}", "{}", "{\"noTradeWhen\":\"requirements absent\"}",
        new HashSet<string> { "MES", "MNQ" }, new HashSet<string>(),
        [new("Pattern", "liquidity-sweep", "capture-1.0.0", "setup", true),
         new("Sequence", "intraday-pattern-progression", "capture-1.0.0", "context", true)],
        [new("Research", "evidence-1", "dataset-1", TestData.BaseTime)], Manifest(),
        "dataset-discovery", "dataset-validation", "test", TestData.BaseTime);

    private static StrategyEngineVersionManifest Manifest() => new("canonical-1", "features-1",
        "patterns-1", "sequences-1", "strategies-1", "execution-unresolved", "research-legacy-1",
        "legacy-utc-1.0.0", "contracts-1");
    private static async Task<int> Count(PfaDatabase database, string table)
    {
        await using SqliteConnection connection = database.CreateConnection(); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
