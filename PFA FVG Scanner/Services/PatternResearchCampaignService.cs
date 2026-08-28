namespace PFA_FVG_Scanner.Services;

public sealed record PatternResearchCampaignRequest(string InstrumentId, string ContractId,
    IReadOnlyList<string>? Timeframes = null);

public sealed record PatternResearchCampaignResult(string InstrumentId, string ContractId,
    IReadOnlyList<PatternReplaySummary> DetectionReplays,
    IReadOnlyList<GenericOutcomeReplaySummary> OutcomeReplays,
    DateTime CompletedAtUtc, bool StrategyActivationAuthorized = false, bool LiveRoutingAuthorized = false);

public sealed class PatternResearchCampaignService(
    PatternSequenceReplayService patterns,
    GenericPatternOutcomeReplayService outcomes)
{
    private static readonly string[] DefaultTimeframes = ["1m", "5m", "15m", "1h"];

    public async Task<PatternResearchCampaignResult> RunAsync(PatternResearchCampaignRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instrument = Required(request.InstrumentId, nameof(request.InstrumentId)).ToUpperInvariant();
        var contract = Required(request.ContractId, nameof(request.ContractId)).ToUpperInvariant();
        var timeframes = (request.Timeframes is { Count: > 0 } ? request.Timeframes : DefaultTimeframes)
            .Select(x => Required(x, "Timeframe").ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (timeframes.Any(x => !DefaultTimeframes.Contains(x, StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("Supported research timeframes are 1m, 5m, 15m and 1h.");
        var detectionResults = new List<PatternReplaySummary>();
        var outcomeResults = new List<GenericOutcomeReplaySummary>();
        foreach (var timeframe in timeframes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            detectionResults.Add(await patterns.ReplayAsync(instrument, contract, timeframe,
                cancellationToken: cancellationToken));
            outcomeResults.Add(await outcomes.ReplayAsync(instrument, contract, timeframe, cancellationToken));
        }
        return new(instrument, contract, detectionResults, outcomeResults, DateTime.UtcNow);
    }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.") : value.Trim();
}
