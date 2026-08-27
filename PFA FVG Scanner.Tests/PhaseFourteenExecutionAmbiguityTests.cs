using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Execution;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseFourteenExecutionAmbiguityTests
{
    [Fact]
    public void OneSecondEvidenceResolvesStopBeforeTarget()
    {
        var result=Resolver().Resolve(Request(),[Slice(ExecutionResolution.OneSecond,Event(1,99,97,"SEC-1"))],TestData.BaseTime);
        Assert.Equal(ExecutionChronology.StopBeforeTarget,result.Chronology);Assert.Equal(ExecutionResolution.OneSecond,result.ResolvedAtResolution);
        Assert.Equal("SEC-1",Assert.Single(result.Attempts).SourceReferences.Single());Assert.False(result.UsedOptimisticFallback);
    }

    [Fact]
    public void AmbiguousSecondEscalatesToTickAndResolvesTargetFirst()
    {
        var evidence=new[]{Slice(ExecutionResolution.OneSecond,Event(1,103,97,"SEC-BOTH")),
            Slice(ExecutionResolution.Tick,Event(2,102,102,"TICK-TARGET"),Event(3,98,98,"TICK-STOP"))};
        var result=Resolver().Resolve(Request(),evidence,TestData.BaseTime);
        Assert.Equal(ExecutionChronology.TargetBeforeStop,result.Chronology);Assert.Equal(ExecutionResolution.Tick,result.ResolvedAtResolution);
        Assert.Equal(2,result.Attempts.Count);Assert.Equal(ExecutionChronology.StillAmbiguous,result.Attempts[0].Result);
        Assert.Equal("TICK-TARGET",result.Attempts[1].SourceReferences.Single());
    }

    [Fact]
    public void BothLevelsAtTickRemainAmbiguousWithoutOptimisticFallback()
    {
        var result=Resolver().Resolve(Request(),[Slice(ExecutionResolution.Tick,Event(1,103,97,"TICK-BOTH"))],TestData.BaseTime);
        Assert.Equal(ExecutionChronology.StillAmbiguous,result.Chronology);Assert.Null(result.ResolvedAtResolution);
        Assert.False(result.UsedOptimisticFallback);
    }

    [Fact]
    public void MissingOrRejectedEvidenceCannotResolveChronology()
    {
        var none=Resolver().Resolve(Request(),[],TestData.BaseTime);Assert.Equal(ExecutionChronology.NoEvidence,none.Chronology);
        var rejected=Resolver().Resolve(Request(),[Slice(ExecutionResolution.OneSecond,
            Event(1,99,97,"CONFLICT",MarketDataQualityFlags.ProviderConflict))],TestData.BaseTime);
        Assert.Equal(ExecutionChronology.NoEvidence,rejected.Chronology);
    }

    [Fact]
    public void LegacyAdapterOnlyRequestsEvidenceForActuallyAmbiguousScenarios()
    {
        var scenario=new MesTradeScenario{ScenarioId=Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Direction=FvgDirection.Bullish,StopPrice=98,TargetPrice=102,StopHitTimeUtc=TestData.BaseTime,
            TargetHitTimeUtc=TestData.BaseTime,IntrabarSequenceUnknown=true,EngineVersion="1.0.0"};
        var request=LegacyMesAmbiguityAdapter.CreateRequest(scenario,"MES","REV-1");
        Assert.NotNull(request);Assert.Equal(ExecutionResolution.OneMinute,request!.OriginalResolution);
        scenario.IntrabarSequenceUnknown=false;Assert.Null(LegacyMesAmbiguityAdapter.CreateRequest(scenario,"MES","REV-1"));
    }

    [Fact]
    public async Task PersistenceIsImmutableIdempotentAndRetainsAttempts()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var request=Request();
        var result=Resolver().Resolve(request,[Slice(ExecutionResolution.OneSecond,Event(1,103,97,"BOTH")),
            Slice(ExecutionResolution.Tick,Event(2,102,102,"TARGET"))],TestData.BaseTime);
        var repository=new ExecutionAmbiguityRepository(factory.Database);
        await repository.SaveAsync(request,result,TestContext.Current.CancellationToken);await repository.SaveAsync(request,result,TestContext.Current.CancellationToken);
        var stored=await repository.FindResultAsync(result.ResultId,TestContext.Current.CancellationToken);
        Assert.Equal(result.Chronology,stored!.Chronology);Assert.Equal(2,stored.Attempts.Count);
        Assert.Equal(1,await Count(factory.Database,"ExecutionAmbiguityResults"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repository.SaveAsync(request,result with{ResultId="OPTIMISTIC",UsedOptimisticFallback=true},TestContext.Current.CancellationToken));
    }

    private static ExecutionAmbiguityResolver Resolver()=>new();
    private static ExecutionEvidenceRequest Request()=>new("REQ-1","SUBJECT","MES",PatternDirection.Bullish,
        TestData.BaseTime,TestData.BaseTime.AddMinutes(1),98,102,ExecutionResolution.OneMinute,"1","REV-1");
    private static ExecutionEvidenceSlice Slice(ExecutionResolution resolution,params ExecutionPriceInterval[] events)=>new(resolution,"test","1",events);
    private static ExecutionPriceInterval Event(int second,decimal high,decimal low,string source,MarketDataQualityFlags flags=MarketDataQualityFlags.None)=>new(TestData.BaseTime.AddSeconds(second),high,low,source,flags);
    private static async Task<int> Count(PfaDatabase db,string table){await using SqliteConnection c=db.CreateConnection();await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt32(await q.ExecuteScalarAsync());}
}
