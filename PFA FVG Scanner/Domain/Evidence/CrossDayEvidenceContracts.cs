using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Evidence;

public enum CrossDayEvidenceClassification
{
    InsufficientEvidence,
    Unstable,
    Watchlist,
    PersistentCandidate,
    PersistentNegative
}

public sealed record CrossDayDailyEvidence(
    DateOnly TradingDate,
    int Samples,
    int IndependentEvents,
    IReadOnlyDictionary<string, decimal> Metrics,
    string DailyStatus,
    IReadOnlySet<string> RegimeIds);

public sealed record CrossDaySignatureEvidence(
    string Signature,
    string FamilyId,
    string DefinitionVersion,
    string DefinitionJson,
    CrossDayEvidenceClassification Classification,
    int TotalTradingDays,
    int ObservedDays,
    IReadOnlyList<DateOnly> MissingTradingDates,
    int PositiveDays,
    int NegativeDays,
    int FlatDays,
    int TotalSamples,
    int IndependentEvents,
    IReadOnlyDictionary<string, decimal> AggregateMetrics,
    IReadOnlySet<string> RegimeIds,
    IReadOnlyDictionary<string, bool> Gates,
    bool CanAdvanceToFrozenValidation,
    IReadOnlyList<CrossDayDailyEvidence> DailyEvidence,
    bool CanActivateStrategy = false);

public sealed record GeneralCrossDayEvidenceReport(
    string ReportId,
    string InstrumentId,
    string EvidenceEngineVersion,
    string SessionAssignmentVersion,
    DateOnly StartTradingDate,
    DateOnly EndTradingDate,
    IReadOnlyList<DateOnly> ExpectedTradingDates,
    IReadOnlyList<CrossDaySignatureEvidence> Signatures,
    string SourceReference,
    DateTime CreatedAtUtc,
    bool CanActivateAnyStrategy = false)
{
    public string ContentHash()
    {
        var canonical = JsonSerializer.Serialize(new
        {
            ReportId, InstrumentId, EvidenceEngineVersion, SessionAssignmentVersion,
            StartTradingDate, EndTradingDate, ExpectedTradingDates,
            Signatures = Signatures.OrderBy(x => x.Signature, StringComparer.Ordinal).ToArray(),
            SourceReference, CanActivateAnyStrategy
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public interface ICrossDayEvidenceRepository
{
    Task SaveAsync(GeneralCrossDayEvidenceReport report, CancellationToken cancellationToken = default);
    Task<GeneralCrossDayEvidenceReport?> FindAsync(string reportId, CancellationToken cancellationToken = default);
}
