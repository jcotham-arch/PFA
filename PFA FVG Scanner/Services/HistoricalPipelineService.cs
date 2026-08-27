using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Historical;

namespace PFA_FVG_Scanner.Services;

public sealed class LegacyHistoricalWindowProcessor : IHistoricalWindowProcessor
{
    private readonly MassiveBackfillService _backfill;
    private readonly HistoricalCandleRebuildService _rebuild;
    public LegacyHistoricalWindowProcessor(MassiveBackfillService backfill, HistoricalCandleRebuildService rebuild)
    { _backfill = backfill; _rebuild = rebuild; }
    public async Task<HistoricalWindowResult> ProcessAsync(HistoricalWorkWindow window, CancellationToken token)
    {
        var captured = await _backfill.BackfillOneMinuteBarsAsync(window.ProviderSymbol, window.StartUtc, window.EndUtc, token);
        var rebuilt = await _rebuild.RebuildFiveMinuteCandlesAsync(window.ProviderSymbol, window.StartUtc, window.EndUtc, token);
        return new(captured.BarsReturned, captured.BarsSaved, rebuilt.FiveMinuteCandlesBuilt, 0);
    }
}

public sealed class HistoricalPipelineService
{
    private readonly HistoricalPipelineRepository _repository;
    private readonly IHistoricalWindowProcessor _processor;
    public HistoricalPipelineService(HistoricalPipelineRepository repository, IHistoricalWindowProcessor processor)
    { _repository = repository; _processor = processor; }

    public Task<HistoricalJobSnapshot> SubmitAsync(HistoricalDatasetPlan plan, DateTime nowUtc, CancellationToken token = default) =>
        _repository.CreateAsync(plan, nowUtc, token);

    public async Task<HistoricalJobSnapshot> RunAsync(string jobId, CancellationToken token = default)
    {
        var job = await _repository.FindAsync(jobId, token) ?? throw new KeyNotFoundException($"Historical job '{jobId}' was not found.");
        if (job.Status == HistoricalJobStatus.Completed) return job;
        var runId = await _repository.BeginRunAsync(jobId, DateTime.UtcNow, token);
        await _repository.SetJobStatusAsync(jobId, HistoricalJobStatus.Running, DateTime.UtcNow, token);
        using var gate = new SemaphoreSlim(job.Plan.MaxConcurrency);
        var pending = job.Checkpoints.Where(x => x.Status != HistoricalWorkStatus.Completed).ToArray();
        await Task.WhenAll(pending.Select(async checkpoint =>
        {
            await gate.WaitAsync(token);
            try
            {
                await _repository.MarkRunningAsync(jobId, checkpoint.Window.WorkId, DateTime.UtcNow, token);
                try
                {
                    var result = await _processor.ProcessAsync(checkpoint.Window, token);
                    await _repository.CompleteAsync(jobId,checkpoint.Window,job.Plan.SourceResolution,job.Plan.RebuildResolution,result,DateTime.UtcNow,token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { await _repository.FailAsync(jobId, checkpoint.Window.WorkId, ex.Message, DateTime.UtcNow, token); }
            }
            finally { gate.Release(); }
        }));
        var finalized=await _repository.FinalizeAsync(jobId,DateTime.UtcNow,token);
        await _repository.EndRunAsync(runId,finalized.Status,null,DateTime.UtcNow,token);
        return finalized;
    }
}
