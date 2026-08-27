using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Domain.Instruments;

namespace PFA_FVG_Scanner.Domain.Historical;

public enum HistoricalJobStatus { Draft, Queued, Running, PartiallyCompleted, Completed, Failed, Cancelled }
public enum HistoricalWorkStatus { Pending, Running, Completed, Failed }

public sealed record HistoricalInstrumentRequest(string InstrumentId, string ProviderSymbol);

public sealed record HistoricalDatasetRequest(
    string Name,
    string Provider,
    DateTime StartUtc,
    DateTime EndUtc,
    IReadOnlyList<HistoricalInstrumentRequest> Instruments,
    int WindowDays = 7,
    int MaxConcurrency = 2,
    string SourceResolution = "1m",
    string RebuildResolution = "5m");

public sealed record HistoricalWorkWindow(
    string WorkId,
    string InstrumentId,
    string ProviderSymbol,
    string InstrumentDefinitionVersion,
    DateTime StartUtc,
    DateTime EndUtc,
    string StartTradingSessionId,
    string EndTradingSessionId,
    int Ordinal);

public sealed record HistoricalDatasetPlan(
    string PlanId,
    string PlanVersion,
    string Name,
    string Provider,
    DateTime StartUtc,
    DateTime EndUtc,
    string SourceResolution,
    string RebuildResolution,
    int MaxConcurrency,
    string SessionAssignmentVersion,
    IReadOnlyList<HistoricalWorkWindow> Windows,
    DateTime CreatedAtUtc);

public sealed record HistoricalWindowResult(int BarsReturned, int BarsSaved, int RebuiltCandles, int QualityIssueCount);

public sealed record HistoricalWorkCheckpoint(
    string JobId,
    HistoricalWorkWindow Window,
    HistoricalWorkStatus Status,
    int AttemptCount,
    HistoricalWindowResult? Result,
    string? LastError,
    DateTime UpdatedAtUtc);

public sealed record HistoricalDatasetManifest(
    string ManifestId,
    string JobId,
    string PlanId,
    string Status,
    int TotalWindows,
    int CompletedWindows,
    int FailedWindows,
    int BarsSaved,
    int RebuiltCandles,
    int QualityIssueCount,
    IReadOnlyList<string> InstrumentIds,
    string SessionAssignmentVersion,
    string ContentHash,
    DateTime CreatedAtUtc);

public sealed record HistoricalJobSnapshot(
    string JobId,
    HistoricalJobStatus Status,
    HistoricalDatasetPlan Plan,
    IReadOnlyList<HistoricalWorkCheckpoint> Checkpoints,
    HistoricalDatasetManifest? Manifest,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public interface IHistoricalWindowProcessor
{
    Task<HistoricalWindowResult> ProcessAsync(HistoricalWorkWindow window, CancellationToken cancellationToken);
}

public sealed class HistoricalUniversePlanner
{
    public const string Version = "1.0.0";
    private readonly ITradingSessionService _sessions;
    private readonly IInstrumentDefinitionRegistry _instruments;
    public HistoricalUniversePlanner(ITradingSessionService sessions,IInstrumentDefinitionRegistry instruments)
    { _sessions=sessions;_instruments=instruments; }

    public HistoricalDatasetPlan Create(HistoricalDatasetRequest request, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        var start = Utc(request.StartUtc); var end = Utc(request.EndUtc);
        if (end <= start) throw new ArgumentException("EndUtc must be after StartUtc.");
        if (request.WindowDays is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(request.WindowDays));
        if (request.MaxConcurrency is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(request.MaxConcurrency));
        if (request.Instruments.Count == 0) throw new ArgumentException("At least one instrument is required.");
        var instruments = request.Instruments.Select(x => new HistoricalInstrumentRequest(
                Required(x.InstrumentId, "InstrumentId").ToUpperInvariant(), Required(x.ProviderSymbol, "ProviderSymbol").ToUpperInvariant()))
            .OrderBy(x => x.InstrumentId, StringComparer.Ordinal).ThenBy(x => x.ProviderSymbol, StringComparer.Ordinal).ToArray();
        if (instruments.Select(x => x.InstrumentId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != instruments.Length)
            throw new ArgumentException("Each instrument may appear only once; use an explicit continuous-contract plan version for rollover changes.");
        var windows = new List<HistoricalWorkWindow>();
        foreach (var instrument in instruments)
        {
            var definition=_instruments.GetAll().FirstOrDefault(x=>string.Equals(x.InstrumentId,instrument.InstrumentId,StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Instrument '{instrument.InstrumentId}' is not in the versioned research universe.");
            if(!definition.ApprovedResolutions.Contains(request.SourceResolution)||!definition.ApprovedResolutions.Contains(request.RebuildResolution))
                throw new ArgumentException($"The requested resolutions are not approved for instrument '{instrument.InstrumentId}'.");
            var cursor = start; var ordinal = 0;
            while (cursor < end)
            {
                var windowEnd = cursor.AddDays(request.WindowDays); if (windowEnd > end) windowEnd = end;
                var startSession = _sessions.Assign(instrument.InstrumentId, cursor);
                var endSession = _sessions.Assign(instrument.InstrumentId, windowEnd.AddTicks(-1));
                var workSeed = $"{instrument.InstrumentId}|{instrument.ProviderSymbol}|{cursor:O}|{windowEnd:O}";
                windows.Add(new(Hex(workSeed)[..24], instrument.InstrumentId, instrument.ProviderSymbol, definition.DefinitionVersion, cursor, windowEnd,
                    startSession.Session.TradingSessionId, endSession.Session.TradingSessionId, ++ordinal));
                cursor = windowEnd;
            }
        }
        var sessionVersion = _sessions.Assign(instruments[0].InstrumentId, start).AssignmentVersion;
        var identity = JsonSerializer.Serialize(new { PlannerVersion=Version,request.Name,request.Provider,Start=start,End=end,
            request.SourceResolution,request.RebuildResolution,request.WindowDays,request.MaxConcurrency,Instruments=instruments,
            SessionAssignmentVersion=sessionVersion,Definitions=windows.Select(x=>new{x.InstrumentId,x.InstrumentDefinitionVersion}).Distinct().ToArray() });
        return new(Hex(identity)[..32], Version, Required(request.Name, "Name"), Required(request.Provider, "Provider"), start, end,
            request.SourceResolution, request.RebuildResolution, request.MaxConcurrency, sessionVersion, windows, Utc(createdAtUtc));
    }

    internal static string Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.") : value.Trim();
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}
