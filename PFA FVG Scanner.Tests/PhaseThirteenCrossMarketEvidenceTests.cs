using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Evidence;
using PFA_FVG_Scanner.Domain.Instruments;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseThirteenCrossMarketEvidenceTests
{
    [Fact]
    public void NormalizesPointsToInstrumentSpecificTicksAndDollars()
    {
        var result=Service().Evaluate(Plan("MES","MNQ"),[Input("MES",1,1),Input("MNQ",1,1)],AsOf,TestData.BaseTime);
        var mes=result.Markets.Single(x=>x.InstrumentId=="MES");var mnq=result.Markets.Single(x=>x.InstrumentId=="MNQ");
        Assert.Equal(4,mes.AverageMoveTicks);Assert.Equal(5,mes.AverageMoveDollarsPerContract);
        Assert.Equal(4,mnq.AverageMoveTicks);Assert.Equal(2,mnq.AverageMoveDollarsPerContract);
    }

    [Fact]
    public void SessionDifferenceIsPartialWhileMissingFeatureIsNonComparable()
    {
        var session=Input("MES",.2m,1) with{SessionVersion="different"};
        var missing=Input("MNQ",.2m,1) with{AvailableFeatureIds=new HashSet<string>()};
        var result=Service().Evaluate(Plan("MES","MNQ"),[session,missing],AsOf,TestData.BaseTime);
        Assert.Equal(MarketComparability.PartiallyComparable,result.Markets[0].Comparability);
        Assert.Contains("session-version-difference",result.Markets[0].ComparabilityNotes);
        Assert.Equal(MarketComparability.NonComparable,result.Markets[1].Comparability);
        Assert.Contains(result.Markets[1].ComparabilityNotes,x=>x.StartsWith("missing-features:"));
    }

    [Fact]
    public void ClassificationDistinguishesRobustAndMarketSpecificWithoutInvalidation()
    {
        var robust=Service().Evaluate(Plan("MES","MNQ"),[Input("MES",.2m,1),Input("MNQ",.1m,2)],AsOf,TestData.BaseTime);
        Assert.Equal(CrossMarketClassification.Robust,robust.Classification);
        var specific=Service().Evaluate(Plan("MES","MNQ"),[Input("MES",.2m,1),Input("MNQ",-.1m,2)],AsOf,TestData.BaseTime);
        Assert.Equal(CrossMarketClassification.MarketSpecific,specific.Classification);
        Assert.False(specific.InvalidatesSourceHypothesis);Assert.False(specific.CanActivateStrategy);
    }

    [Fact]
    public void DefinitionMismatchAndUnavailableMarketStayAuditable()
    {
        var mismatched=Input("MES",.2m,1) with{DefinitionVersion="2"};
        var result=Service().Evaluate(Plan("MES","UNKNOWN"),[mismatched],AsOf,TestData.BaseTime);
        Assert.All(result.Markets,x=>Assert.Equal(MarketComparability.NonComparable,x.Comparability));
        Assert.Equal(CrossMarketClassification.Inconclusive,result.Classification);
    }

    [Fact]
    public async Task PersistenceIsImmutableIdempotentAndPreservesComparabilityNotes()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();
        var result=Service().Evaluate(Plan("MES","MNQ"),[Input("MES",.2m,1),Input("MNQ",-.1m,2) with{SessionVersion="other"}],AsOf,TestData.BaseTime);
        var repository=new CrossMarketEvidenceRepository(factory.Database);
        await repository.SaveAsync(result,TestContext.Current.CancellationToken);await repository.SaveAsync(result,TestContext.Current.CancellationToken);
        var stored=await repository.FindAsync(result.ResultId,TestContext.Current.CancellationToken);
        Assert.Equal(result.Classification,stored!.Classification);Assert.Contains("session-version-difference",stored.Markets[1].ComparabilityNotes);
        Assert.Equal(1,await Count(factory.Database,"CrossMarketEvidenceResults"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.SaveAsync(result with{Summary="changed"},TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repository.SaveAsync(result with{ResultId="ACTIVE",CanActivateStrategy=true},TestContext.Current.CancellationToken));
    }

    private static readonly DateOnly AsOf=new(2026,8,27);
    private static CrossMarketEvidenceService Service()=>new(new InstrumentDefinitionRegistry());
    private static CrossMarketEvidencePlan Plan(params string[] ids)=>new("PLAN","1","SIG","1","MES",ids,
        new HashSet<string>{"feature-a"},"session-1","DATASET",TestData.BaseTime);
    private static MarketEvidenceInput Input(string id,decimal expectancy,decimal points)=>new(id,"1","session-1",
        new HashSet<string>{"feature-a"},20,15,expectancy,expectancy*20,points,$"EVIDENCE-{id}");
    private static async Task<int> Count(PfaDatabase db,string table){await using SqliteConnection c=db.CreateConnection();await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt32(await q.ExecuteScalarAsync());}
}
