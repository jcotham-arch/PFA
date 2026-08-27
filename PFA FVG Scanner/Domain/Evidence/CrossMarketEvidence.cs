using PFA_FVG_Scanner.Domain.Instruments;

namespace PFA_FVG_Scanner.Domain.Evidence;

public enum MarketComparability { Comparable, PartiallyComparable, NonComparable }
public enum CrossMarketClassification { Robust, MarketSpecific, Mixed, Inconclusive }

public sealed record CrossMarketEvidencePlan(
    string PlanId,
    string PlanVersion,
    string FrozenSignature,
    string DefinitionVersion,
    string SourceInstrumentId,
    IReadOnlyList<string> InstrumentIds,
    HashSet<string> RequiredFeatureIds,
    string ExpectedSessionVersion,
    string DatasetManifestId,
    DateTime FrozenAtUtc);

public sealed record MarketEvidenceInput(
    string InstrumentId,
    string DefinitionVersion,
    string SessionVersion,
    HashSet<string> AvailableFeatureIds,
    int Samples,
    int IndependentEvents,
    decimal ExpectancyR,
    decimal NetR,
    decimal AverageMovePoints,
    string EvidenceReference);

public sealed record NormalizedMarketEvidence(
    string InstrumentId,
    MarketComparability Comparability,
    IReadOnlyList<string> ComparabilityNotes,
    int Samples,
    int IndependentEvents,
    decimal ExpectancyR,
    decimal NetR,
    decimal AverageMovePoints,
    decimal? AverageMoveTicks,
    decimal? AverageMoveDollarsPerContract,
    string InstrumentDefinitionVersion,
    string EvidenceReference);

public sealed record CrossMarketEvidenceResult(
    string ResultId,
    CrossMarketEvidencePlan Plan,
    CrossMarketClassification Classification,
    IReadOnlyList<NormalizedMarketEvidence> Markets,
    int ComparableMarkets,
    int PositiveComparableMarkets,
    int NegativeComparableMarkets,
    string Summary,
    DateTime CreatedAtUtc,
    bool InvalidatesSourceHypothesis = false,
    bool CanActivateStrategy = false);

public interface ICrossMarketEvidenceService
{
    CrossMarketEvidenceResult Evaluate(CrossMarketEvidencePlan plan,
        IReadOnlyList<MarketEvidenceInput> evidence, DateOnly instrumentAsOfDate, DateTime createdAtUtc);
}

public interface ICrossMarketEvidenceRepository
{
    Task SaveAsync(CrossMarketEvidenceResult result, CancellationToken cancellationToken = default);
    Task<CrossMarketEvidenceResult?> FindAsync(string resultId, CancellationToken cancellationToken = default);
}

public sealed class CrossMarketEvidenceService : ICrossMarketEvidenceService
{
    private readonly IInstrumentDefinitionRegistry _instruments;
    public CrossMarketEvidenceService(IInstrumentDefinitionRegistry instruments) => _instruments = instruments;

    public CrossMarketEvidenceResult Evaluate(CrossMarketEvidencePlan plan,
        IReadOnlyList<MarketEvidenceInput> evidence, DateOnly instrumentAsOfDate, DateTime createdAtUtc)
    {
        var normalized = plan.InstrumentIds.Select(id => Normalize(plan,
            evidence.FirstOrDefault(x => x.InstrumentId.Equals(id, StringComparison.OrdinalIgnoreCase)),
            id, instrumentAsOfDate)).ToArray();
        var comparable = normalized.Where(x => x.Comparability != MarketComparability.NonComparable).ToArray();
        var positive = comparable.Count(x => x.ExpectancyR > 0);
        var negative = comparable.Count(x => x.ExpectancyR < 0);
        var source = normalized.FirstOrDefault(x => x.InstrumentId.Equals(plan.SourceInstrumentId,
            StringComparison.OrdinalIgnoreCase));
        var classification = comparable.Length < 2 ? CrossMarketClassification.Inconclusive
            : positive == comparable.Length ? CrossMarketClassification.Robust
            : source?.ExpectancyR > 0 && negative > 0 ? CrossMarketClassification.MarketSpecific
            : CrossMarketClassification.Mixed;
        var summary = $"{comparable.Length}/{normalized.Length} markets comparable; {positive} positive, {negative} negative. " +
            "Cross-market results are evidence and do not invalidate or activate the source hypothesis.";
        return new($"{plan.PlanId}|{plan.PlanVersion}|{plan.DatasetManifestId}", plan, classification,
            normalized, comparable.Length, positive, negative, summary, createdAtUtc, false, false);
    }

    private NormalizedMarketEvidence Normalize(CrossMarketEvidencePlan plan, MarketEvidenceInput? input,
        string instrumentId, DateOnly asOf)
    {
        var notes = new List<string>();
        var instrument = _instruments.Find(instrumentId, asOf);
        if (instrument is null) notes.Add("instrument-definition-unavailable");
        if (input is null) notes.Add("market-evidence-unavailable");
        if (input is not null && input.DefinitionVersion != plan.DefinitionVersion)
            notes.Add("definition-version-mismatch");
        if (input is not null)
        {
            var missing = plan.RequiredFeatureIds.Where(x => !input.AvailableFeatureIds.Contains(x)).ToArray();
            if (missing.Length > 0) notes.Add("missing-features:" + string.Join(',', missing));
            if (input.SessionVersion != plan.ExpectedSessionVersion) notes.Add("session-version-difference");
        }
        var hardFailure = notes.Any(x => x is "instrument-definition-unavailable" or "market-evidence-unavailable"
            or "definition-version-mismatch" || x.StartsWith("missing-features:", StringComparison.Ordinal));
        var comparability = hardFailure ? MarketComparability.NonComparable
            : notes.Count > 0 ? MarketComparability.PartiallyComparable : MarketComparability.Comparable;
        var points = input?.AverageMovePoints ?? 0;
        return new(instrumentId, comparability, notes, input?.Samples ?? 0, input?.IndependentEvents ?? 0,
            input?.ExpectancyR ?? 0, input?.NetR ?? 0, points,
            instrument is null ? null : points / instrument.TickSize,
            instrument is null ? null : points * instrument.PointValue,
            instrument?.DefinitionVersion ?? "unavailable", input?.EvidenceReference ?? "unavailable");
    }
}
