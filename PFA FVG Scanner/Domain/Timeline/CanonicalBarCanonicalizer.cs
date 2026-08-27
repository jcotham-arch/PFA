using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PFA_FVG_Scanner.Domain.Contracts;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Domain.Timeline;

public sealed record CanonicalizationRequest(
    Candle Candle,
    string Provider,
    string ProviderSymbol,
    string SourceEventType,
    DateTime SourceTimestampUtc,
    DateTime ReceivedTimestampUtc,
    string SourceVersion,
    string IngestionRunId,
    string? RawReference = null);

public sealed record CanonicalizedBarCandidate(CanonicalBar Bar, CanonicalBarSource Source);

public interface ICanonicalBarCanonicalizer
{
    CanonicalizedBarCandidate Canonicalize(CanonicalizationRequest request);
}

public sealed class CanonicalBarCanonicalizer : ICanonicalBarCanonicalizer
{
    public const string CanonicalizationVersion = "1.0.0";
    public const string TransformationVersion = "legacy-candle-adapter-1.0.0";
    private readonly IInstrumentDefinitionRegistry _instruments;
    private readonly IContractResolver _contracts;
    private readonly ITradingSessionService _sessions;

    public CanonicalBarCanonicalizer(IInstrumentDefinitionRegistry instruments,
        IContractResolver contracts, ITradingSessionService sessions)
    {
        _instruments = instruments;
        _contracts = contracts;
        _sessions = sessions;
    }

    public CanonicalizedBarCandidate Canonicalize(CanonicalizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Candle);
        var candle = request.Candle;
        var openUtc = EnsureUtc(candle.OpenTimeUtc);
        var closeUtc = CalculateClose(openUtc, candle.Timeframe);
        var resolution = _contracts.Resolve(request.Provider, request.ProviderSymbol);
        var rootDefinition = _instruments.Find(candle.Symbol, DateOnly.FromDateTime(openUtc));
        var instrumentId = resolution.InstrumentId ?? rootDefinition?.InstrumentId ?? "UNRESOLVED";
        var assignment = _sessions.Assign(instrumentId, openUtc);
        var quality = MarketDataQualityFlags.None;
        if (!candle.IsClosed) quality |= MarketDataQualityFlags.Incomplete;
        if (candle.High < candle.Low || candle.Open > candle.High || candle.Open < candle.Low
            || candle.Close > candle.High || candle.Close < candle.Low || candle.Volume < 0)
            quality |= MarketDataQualityFlags.InvalidOhlc;
        if (instrumentId == "UNRESOLVED") quality |= MarketDataQualityFlags.UnresolvedInstrument;
        if (!resolution.IsResolved) quality |= MarketDataQualityFlags.UnresolvedContract;
        if (assignment.Session.Quality == SessionAssignmentQuality.LegacyCompatibility)
            quality |= MarketDataQualityFlags.LegacySession;

        // Until a dated contract resolves, retain the normalized provider symbol
        // in identity so unrelated unresolved contracts cannot collide.
        var naturalKey = Join(instrumentId,
            resolution.ContractId ?? request.ProviderSymbol.Trim().ToUpperInvariant(),
            candle.Timeframe.Trim().ToLowerInvariant(), openUtc.ToString("O"));
        var canonicalId = "BAR-" + Hash(naturalKey);
        var content = Join(candle.Open, candle.High, candle.Low, candle.Close, candle.Volume,
            candle.IsClosed, closeUtc.ToString("O"), TransformationVersion);
        var contentHash = Hash(content);
        var now = DateTime.UtcNow;
        var bar = new CanonicalBar(canonicalId, 1, instrumentId, resolution.ContractId,
            request.ProviderSymbol.Trim().ToUpperInvariant(), candle.Timeframe.Trim().ToLowerInvariant(),
            openUtc, closeUtc, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume,
            candle.IsClosed, assignment.Session.TradingSessionId, assignment.Session.TradingDate,
            CanonicalizationVersion, TransformationVersion, CorrectionState.Original, quality, now, contentHash);
        var sourceNaturalKey = Join(request.Provider, request.ProviderSymbol, request.SourceEventType,
            request.SourceTimestampUtc.ToUniversalTime().ToString("O"), request.SourceVersion,
            request.RawReference ?? contentHash);
        var source = new CanonicalBarSource("SRC-" + Hash(sourceNaturalKey), canonicalId, 1,
            request.Provider.Trim(), request.ProviderSymbol.Trim(), request.SourceEventType,
            candle.Timeframe.Trim().ToLowerInvariant(), EnsureUtc(request.SourceTimestampUtc),
            EnsureUtc(request.ReceivedTimestampUtc), request.SourceVersion, request.IngestionRunId,
            request.RawReference);
        return new(bar, source);
    }

    private static string Join(params object[] values) => string.Join('|', values.Select(x =>
        x is IFormattable value ? value.ToString(null, CultureInfo.InvariantCulture) : x.ToString()));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };
    private static DateTime CalculateClose(DateTime open, string timeframe) => timeframe.Trim().ToLowerInvariant() switch
    {
        "1m" => open.AddMinutes(1), "5m" => open.AddMinutes(5), "15m" => open.AddMinutes(15),
        "1h" => open.AddHours(1), "4h" => open.AddHours(4), "1d" or "daily" => open.AddDays(1),
        _ => open
    };
}
