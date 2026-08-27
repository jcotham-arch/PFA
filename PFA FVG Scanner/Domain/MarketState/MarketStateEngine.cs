using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PFA_FVG_Scanner.Domain.Features;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.MarketState;

public sealed class MarketStateEngine : IMarketStateEngine
{
    public const string EngineVersion = "1.0.0";

    public MarketStateSnapshot Build(string instrumentId, string? contractId, DateTime asOfUtc,
        string dataRevision, IReadOnlyList<CanonicalBar> bars)
    {
        var asOf = EnsureUtc(asOfUtc);
        var eligible = (bars ?? Array.Empty<CanonicalBar>())
            .Where(x => x.InstrumentId.Equals(instrumentId, StringComparison.OrdinalIgnoreCase))
            .Where(x => contractId is null || string.Equals(x.ContractId, contractId, StringComparison.OrdinalIgnoreCase))
            .Where(x => x.CloseTimeUtc <= asOf && x.RevisionEffectiveUtc <= asOf)
            .GroupBy(x => x.CanonicalBarId)
            .Select(x => x.OrderByDescending(y => y.Revision).First())
            .OrderBy(x => x.OpenTimeUtc).ToArray();
        var latest = eligible.LastOrDefault();
        var sourceIds = eligible.Select(x => $"{x.CanonicalBarId}:{x.Revision}").ToArray();
        var quality = eligible.Aggregate(MarketDataQualityFlags.None, (current, bar) => current | bar.QualityFlags);
        var session = latest?.TradingSessionId ?? "UNRESOLVED";
        var facts = new List<FeatureValue>();
        if (latest is not null)
        {
            facts.Add(Fact("market.close", latest.Close.ToString(CultureInfo.InvariantCulture), instrumentId, asOf,
                dataRevision, quality, sourceIds));
            facts.Add(Fact("market.range", (latest.High - latest.Low).ToString(CultureInfo.InvariantCulture), instrumentId,
                asOf, dataRevision, quality, sourceIds));
        }
        facts.Add(Fact("market.bar_count", eligible.Length.ToString(CultureInfo.InvariantCulture), instrumentId,
            asOf, dataRevision, quality, sourceIds));
        var identity = $"{instrumentId}|{contractId}|{asOf:O}|{dataRevision}|{EngineVersion}|{string.Join(',', sourceIds)}";
        var id = "STATE-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new(id, instrumentId, contractId, asOf, asOf, dataRevision, EngineVersion,
            session, quality, facts, sourceIds);
    }

    private static FeatureValue Fact(string definition, string value, string instrument, DateTime asOf,
        string revision, MarketDataQualityFlags quality, IReadOnlyList<string> sources)
    {
        var identity = $"{definition}|{instrument}|{asOf:O}|{revision}|{value}|{EngineVersion}";
        return new("FEATURE-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))),
            definition, FeatureDefinitionRegistry.Version, instrument, instrument, asOf, asOf, value,
            EngineVersion, revision, quality, sources);
    }
    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value, DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc), _ => value.ToUniversalTime()
    };
}
