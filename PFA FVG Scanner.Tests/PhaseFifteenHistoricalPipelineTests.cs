using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Historical;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseFifteenHistoricalPipelineTests
{
    [Fact]
    public void PlannerPaginatesEveryInstrumentDeterministicallyWithoutGaps()
    {
        var request=Request(days:15,windowDays:7,instruments:[new("MES","MESU6"),new("MNQ","MNQU6")]);var planner=Planner();
        var first=planner.Create(request,TestData.BaseTime);var second=planner.Create(request,TestData.BaseTime.AddHours(1));
        Assert.Equal(first.PlanId,second.PlanId);Assert.Equal(6,first.Windows.Count);Assert.Equal(2,first.Windows.Count(x=>x.Ordinal==1));
        foreach(var group in first.Windows.GroupBy(x=>x.InstrumentId)){var ordered=group.OrderBy(x=>x.Ordinal).ToArray();Assert.Equal(first.StartUtc,ordered[0].StartUtc);Assert.Equal(first.EndUtc,ordered[^1].EndUtc);for(var i=1;i<ordered.Length;i++)Assert.Equal(ordered[i-1].EndUtc,ordered[i].StartUtc);}
    }

    [Fact]
    public void PlannerRequiresDatedProviderSymbolsAndRejectsImplicitRolloverDuplicates()
    {
        var planner=Planner();Assert.Throws<ArgumentException>(()=>planner.Create(Request(instruments:[new("MES","")]),TestData.BaseTime));
        Assert.Throws<ArgumentException>(()=>planner.Create(Request(instruments:[new("MES","MESU6"),new("mes","MESZ6")]),TestData.BaseTime));
    }

    [Fact]
    public async Task SubmissionIsDurableAndIdempotentWithoutExecutingProviderWork()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new HistoricalPipelineRepository(factory.Database);var processor=new RecordingProcessor();var pipeline=new HistoricalPipelineService(repository,processor);
        var plan=Planner().Create(Request(days:8),TestData.BaseTime);var equivalentPlan=Planner().Create(Request(days:8),TestData.BaseTime.AddHours(1));var first=await pipeline.SubmitAsync(plan,TestData.BaseTime,TestContext.Current.CancellationToken);var second=await pipeline.SubmitAsync(equivalentPlan,TestData.BaseTime.AddHours(1),TestContext.Current.CancellationToken);
        Assert.Equal(first.JobId,second.JobId);Assert.Equal(HistoricalJobStatus.Draft,second.Status);Assert.Empty(processor.Calls);Assert.All(second.Checkpoints,x=>Assert.Equal(HistoricalWorkStatus.Pending,x.Status));
        Assert.Equal(1,await Count(factory.Database,"HistoricalPipelineJobs"));Assert.Equal(2,await Count(factory.Database,"HistoricalPipelineCheckpoints"));
    }

    [Fact]
    public async Task FailureIsIsolatedAndResumeRetriesOnlyIncompleteWindow()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new HistoricalPipelineRepository(factory.Database);var processor=new RecordingProcessor(failFirstOrdinal:2);var pipeline=new HistoricalPipelineService(repository,processor);
        var submitted=await pipeline.SubmitAsync(Planner().Create(Request(days:15),TestData.BaseTime),TestData.BaseTime,TestContext.Current.CancellationToken);
        var partial=await pipeline.RunAsync(submitted.JobId,TestContext.Current.CancellationToken);Assert.Equal(HistoricalJobStatus.PartiallyCompleted,partial.Status);Assert.Equal(2,partial.Manifest!.CompletedWindows);Assert.Equal(1,partial.Manifest.FailedWindows);
        var resumed=await pipeline.RunAsync(submitted.JobId,TestContext.Current.CancellationToken);Assert.Equal(HistoricalJobStatus.Completed,resumed.Status);Assert.Equal(3,resumed.Manifest!.CompletedWindows);Assert.Equal(4,processor.Calls.Count);
        Assert.Equal(1,resumed.Checkpoints.Single(x=>x.Window.Ordinal==1).AttemptCount);Assert.Equal(2,resumed.Checkpoints.Single(x=>x.Window.Ordinal==2).AttemptCount);
    }

    [Fact]
    public async Task MultiInstrumentExecutionHonorsConcurrencyBoundAndProducesQualityManifest()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new HistoricalPipelineRepository(factory.Database);var processor=new RecordingProcessor(delayMs:30);var pipeline=new HistoricalPipelineService(repository,processor);
        var plan=Planner().Create(Request(days:8,maxConcurrency:2,instruments:[new("MES","MESU6"),new("MNQ","MNQU6")]),TestData.BaseTime);
        var submitted=await pipeline.SubmitAsync(plan,TestData.BaseTime,TestContext.Current.CancellationToken);var completed=await pipeline.RunAsync(submitted.JobId,TestContext.Current.CancellationToken);
        Assert.Equal(HistoricalJobStatus.Completed,completed.Status);Assert.InRange(processor.MaxObservedConcurrency,2,2);Assert.Equal(4,completed.Manifest!.TotalWindows);Assert.Equal(40,completed.Manifest.BarsSaved);Assert.Equal(4,completed.Manifest.QualityIssueCount);
        Assert.Equal(["MES","MNQ"],completed.Manifest.InstrumentIds);Assert.Equal(LegacyUtcTradingSessionService.AssignmentVersion,completed.Manifest.SessionAssignmentVersion);Assert.Equal(1,await Count(factory.Database,"HistoricalDatasetManifests"));
        Assert.Equal(1,await Count(factory.Database,"HistoricalPipelineRuns"));Assert.Equal(4,await Count(factory.Database,"HistoricalCoverageRecords"));
    }

    [Fact]
    public void CoverageWindowsRetainSessionAssignmentsAtEveryBoundary()
    {
        var plan=Planner().Create(Request(days:2,windowDays:1),TestData.BaseTime);Assert.All(plan.Windows,x=>{Assert.StartsWith("MES|",x.StartTradingSessionId);Assert.EndsWith("|LEGACY-UTC",x.EndTradingSessionId);Assert.True(x.EndUtc>x.StartUtc);});
    }

    private static HistoricalUniversePlanner Planner()=>new(new LegacyUtcTradingSessionService(),new InstrumentDefinitionRegistry());
    private static HistoricalDatasetRequest Request(int days=8,int windowDays=7,int maxConcurrency=2,IReadOnlyList<HistoricalInstrumentRequest>? instruments=null)=>new(
        "Twelve month research campaign","Massive",TestData.BaseTime,TestData.BaseTime.AddDays(days),instruments??[new("MES","MESU6")],windowDays,maxConcurrency);
    private static async Task<int> Count(PfaDatabase db,string table){await using SqliteConnection c=db.CreateConnection();await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt32(await q.ExecuteScalarAsync());}

    private sealed class RecordingProcessor(int? failFirstOrdinal=null,int delayMs=0):IHistoricalWindowProcessor
    {
        private readonly object _sync=new();private readonly HashSet<string> _failed=[];private int _active;public List<string> Calls{get;}=[];public int MaxObservedConcurrency{get;private set;}
        public async Task<HistoricalWindowResult> ProcessAsync(HistoricalWorkWindow window,CancellationToken token)
        {lock(_sync){Calls.Add(window.WorkId);_active++;MaxObservedConcurrency=Math.Max(MaxObservedConcurrency,_active);}try{if(delayMs>0)await Task.Delay(delayMs,token);lock(_sync){if(window.Ordinal==failFirstOrdinal&&_failed.Add(window.WorkId))throw new InvalidOperationException("transient provider failure");}return new(12,10,2,1);}finally{lock(_sync)_active--;}}
    }
}
