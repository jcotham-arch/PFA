using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Domain.Features;

public sealed class LegacyFvgFeatureAdapter
{
    public const string EngineVersion = "legacy-fvg-adapter-1.0.0";

    public IReadOnlyList<FeatureValue> Adapt(FvgFeatureRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var confirmation = EnsureUtc(record.ConfirmationTimeUtc);
        var entry = EnsureUtc(record.EntryTimeUtc);
        var quality = record.ExecutionPricesValid && record.IntrabarSequenceWasKnown
            ? MarketDataQualityFlags.None : MarketDataQualityFlags.InvalidOhlc;
        var source = new[] { record.FeatureRecordId.ToString(), record.FvgId.ToString(), record.ScenarioId.ToString() };
        var values = new List<FeatureValue>
        {
            Create(record, "fvg.direction", record.Direction.ToString(), confirmation, confirmation, MarketDataQualityFlags.None, source),
            Create(record, "fvg.gap_size_points", Format(record.GapSizePoints), confirmation, confirmation, MarketDataQualityFlags.None, source),
            Create(record, "fvg.session_bucket", record.SessionBucket.ToString(), confirmation, confirmation, MarketDataQualityFlags.LegacySession, source),
            Create(record, "scenario.entry_model", record.EntryModel.ToString(), confirmation, confirmation, MarketDataQualityFlags.None, source),
            Create(record, "scenario.target_r", Format(record.TargetR), confirmation, confirmation, MarketDataQualityFlags.None, source),
            Create(record, "execution.minutes_to_entry", record.MinutesFromConfirmationToEntry.ToString(CultureInfo.InvariantCulture), entry, entry, quality, source),
            Create(record, "execution.risk_ticks", Format(record.RiskTicks), entry, entry, quality, source)
        };
        var outcomeKnownAt = Latest(record.TargetHitTimeUtc, record.StopHitTimeUtc);
        if (outcomeKnownAt.HasValue)
            values.Add(Create(record, "outcome.realized_r", Format(record.RealizedR),
                outcomeKnownAt.Value, outcomeKnownAt.Value, quality, source));
        if (record.MaximumFavorableR.HasValue && outcomeKnownAt.HasValue)
            values.Add(Create(record, "diagnostic.maximum_favorable_r", Format(record.MaximumFavorableR.Value),
                outcomeKnownAt.Value, outcomeKnownAt.Value, quality, source));
        return values;
    }

    public IReadOnlyList<FeatureValue> AvailablePredictors(FvgFeatureRecord record, DateTime decisionTimeUtc,
        IFeatureDefinitionRegistry definitions) => Adapt(record).Where(value =>
            value.IsAvailableAt(decisionTimeUtc)
            && definitions.Find(value.FeatureDefinitionId)?.Role == FeatureRole.Predictor).ToArray();

    private static FeatureValue Create(FvgFeatureRecord record, string definition, string value,
        DateTime asOf, DateTime knownAt, MarketDataQualityFlags quality, IReadOnlyList<string> sources)
    {
        var naturalKey = $"{record.FeatureRecordId}|{definition}|{asOf:O}|{value}|{EngineVersion}";
        var id = "FEATURE-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(naturalKey)));
        return new(id, definition, FeatureDefinitionRegistry.Version, record.FvgId.ToString(),
            record.Symbol, EnsureUtc(asOf), EnsureUtc(knownAt), value, EngineVersion,
            record.ScenarioEngineVersion, quality, sources);
    }
    private static DateTime? Latest(DateTime? left, DateTime? right) => new[] { left, right }
        .Where(x => x.HasValue).Select(x => EnsureUtc(x!.Value)).Cast<DateTime?>().Max();
    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value, DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc), _ => value.ToUniversalTime()
    };
}
