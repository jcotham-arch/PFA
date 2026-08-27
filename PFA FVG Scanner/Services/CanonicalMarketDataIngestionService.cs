using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services;

public sealed class CanonicalMarketDataIngestionService
{
    private readonly ICanonicalBarCanonicalizer _canonicalizer;
    private readonly CanonicalTimelineRepository _repository;
    public CanonicalMarketDataIngestionService(ICanonicalBarCanonicalizer canonicalizer,
        CanonicalTimelineRepository repository)
    {
        _canonicalizer = canonicalizer;
        _repository = repository;
    }

    public Task<CanonicalBarWriteResult> IngestAsync(Candle candle, string provider,
        string sourceEventType, DateTime receivedUtc, string ingestionRunId,
        string? rawReference = null, CancellationToken cancellationToken = default)
    {
        var request = new CanonicalizationRequest(candle, provider, candle.Symbol,
            sourceEventType, candle.OpenTimeUtc, receivedUtc, "legacy-1.0.0", ingestionRunId, rawReference);
        return _repository.WriteAsync(_canonicalizer.Canonicalize(request), cancellationToken);
    }

    public async Task<CanonicalIngestionAttempt> TryIngestAsync(Candle candle, string provider,
        string sourceEventType, DateTime receivedUtc, string ingestionRunId,
        string? rawReference = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await IngestAsync(candle, provider, sourceEventType, receivedUtc,
                ingestionRunId, rawReference, cancellationToken);
            return new(true, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Canonical storage is additive during migration. A failure must not
            // change the already-successful legacy write or existing API behavior.
            return new(false, null, exception.Message);
        }
    }
}

public sealed record CanonicalIngestionAttempt(
    bool Succeeded,
    CanonicalBarWriteResult? Result,
    string? Error);
