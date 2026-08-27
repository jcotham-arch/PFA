using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Discovery;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseTwentyOneMachineDiscoveryTests
{
    [Fact]
    public void TemporalPartitionsRequireEmbargoAndNeverFitEvaluationRows()
    {
        Assert.Throws<ArgumentException>(()=>Engine.Discover(Manifest() with{Split=new(Start,Start.AddDays(5),Start.AddDays(5),Start.AddDays(10),TimeSpan.FromDays(1))},Rows()));
        var result=Engine.Discover(Manifest(),Rows());
        Assert.Equal(12,result.Clusters.Sum(x=>x.TrainingSamples));Assert.Equal(12,result.Clusters.Sum(x=>x.EvaluationSamples));
    }

    [Fact]
    public void SeedAndSortedInputsAreReproducible()
    {
        var rows=Rows();var a=Engine.Discover(Manifest(),rows);var b=Engine.Discover(Manifest(),Enumerable.Reverse(rows).ToArray());
        Assert.Equal(a.ContentHash(),b.ContentHash());Assert.Equal(a.ModelId,b.ModelId);Assert.Equal(a.Clusters.Select(x=>x.ContentHash),b.Clusters.Select(x=>x.ContentHash));
        Assert.NotEqual(a.ModelId,Engine.Discover(Manifest() with{RandomSeed=99},rows).ModelId);
    }

    [Fact]
    public void PointInTimeDatasetAndCorrectionLeakageAreRejected()
    {
        var rows=Rows();rows[0]=rows[0] with{FeatureKnownAtUtc=rows[0].EventTimeUtc.AddMinutes(1)};Assert.Throws<InvalidOperationException>(()=>Engine.Discover(Manifest(),rows));
        rows=Rows();rows[0]=rows[0] with{OutcomeKnownAtUtc=Manifest().AsOfUtc.AddMinutes(1)};Assert.Throws<InvalidOperationException>(()=>Engine.Discover(Manifest(),rows));
        rows=Rows();rows[0]=rows[0] with{DataRevision="CORRECTED"};Assert.Throws<InvalidOperationException>(()=>Engine.Discover(Manifest(),rows));
    }

    [Fact]
    public void EveryDeclaredClusterRetainsCorrectionAndExplainabilityMetadata()
    {
        var result=Engine.Discover(Manifest(),Rows());Assert.Equal(3,result.Clusters.Count);Assert.Equal(3,result.ResearchRun.Hypotheses.Count);
        Assert.All(result.Clusters,x=>{Assert.Equal(2,x.Explanations.Count);Assert.Equal([1,2],x.Explanations.Select(e=>e.Rank));Assert.True(x.AdjustedPValue>=x.RawPValue);});
        Assert.All(result.ResearchRun.Hypotheses,x=>{Assert.StartsWith("FeatureCluster_",x.Signature);Assert.Equal("FeatureCluster",x.FamilyId);Assert.False(x.CanActivateStrategy);Assert.Contains(x.Metrics,m=>m.Name=="BonferroniAdjustedPValue");});
    }

    [Fact]
    public void MachineHypothesesEnterTheOrdinaryResearchStageWithoutPrivilege()
    {
        var result=Engine.Discover(Manifest(),Rows());Assert.False(result.CanActivateStrategy);Assert.False(result.ResearchRun.CanActivateStrategy);
        Assert.DoesNotContain(result.ResearchRun.Hypotheses,x=>x.CanActivateStrategy);Assert.All(result.ResearchRun.Hypotheses,x=>Assert.Contains(x.Status,Enum.GetValues<ResearchHypothesisStatus>()));
    }

    [Fact]
    public async Task PersistenceIsImmutableIdempotentAndDatabaseEnforcesNonActivation()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var general=new GeneralResearchRepository(factory.Database);var repository=new MachineDiscoveryRepository(factory.Database,general);var result=Engine.Discover(Manifest(),Rows());
        await repository.SaveAsync(result,TestContext.Current.CancellationToken);await repository.SaveAsync(result,TestContext.Current.CancellationToken);var stored=await repository.FindAsync(result.Manifest.RunId,TestContext.Current.CancellationToken);Assert.Equal(result.ContentHash(),stored!.ContentHash());Assert.Equal(1,await Count(factory.Database,"MachineDiscoveryRuns"));Assert.Equal(3,await Count(factory.Database,"MachineFeatureClusters"));Assert.Equal(3,(await general.FindAsync(result.Manifest.RunId,TestContext.Current.CancellationToken))!.Hypotheses.Count);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repository.SaveAsync(result with{CanActivateStrategy=true},TestContext.Current.CancellationToken));
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);await using var command=connection.CreateCommand();command.CommandText="UPDATE MachineDiscoveryRuns SET CanActivateStrategy=1";var error=await Assert.ThrowsAsync<SqliteException>(()=>command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));Assert.Contains("immutable",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    private static readonly DateTime Start=new(2026,1,1,0,0,0,DateTimeKind.Utc);private static readonly MachineBehaviorDiscoveryEngine Engine=new();
    private static MachineDiscoveryManifest Manifest()=>new("MDR-001","deterministic-feature-cluster-1.0.0","feature-cluster-model-1.0.0",new("DATASET",Start,Start.AddDays(30),"DATAHASH","REV-1",["MES"],["2026-01-01"]),new(Start,Start.AddDays(10),Start.AddDays(12),Start.AddDays(22),TimeSpan.FromDays(2)),["momentum","range"],3,42,"1.0.0","Bonferroni",.05m,Start.AddDays(30));
    private static List<MachineDiscoveryObservation> Rows(){var rows=new List<MachineDiscoveryObservation>();for(var i=0;i<12;i++)rows.Add(Row($"T{i}",Start.AddDays(i/2d),i));for(var i=0;i<12;i++)rows.Add(Row($"E{i}",Start.AddDays(12+i/2d),i+20));return rows;}
    private static MachineDiscoveryObservation Row(string id,DateTime eventTime,int value)=>new(id,"MES",eventTime,eventTime.AddMinutes(-1),eventTime.AddHours(1),"DATASET","REV-1",new Dictionary<string,decimal>{{"momentum",value%5-2},{"range",value%7+1}},value%3==0?-1m:.5m);
    private static async Task<int> Count(PfaDatabase db,string table){await using var c=db.CreateConnection();await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt32(await q.ExecuteScalarAsync());}
}
