using System.Globalization;
using System.Text.Json;

namespace PFA_FVG_Scanner.Services;

/// <summary>
/// Encodes only information known at the decision clock. Missing sources are represented by
/// availability gates; their measurements are intentionally omitted rather than imputed as zero.
/// </summary>
public static class PointInTimeContextFeatureEncoder
{
    public const string Version = "point-in-time-context-features-1.0.0";

    public static void Add(Dictionary<string, decimal> features, string? latestBarJson,
        string? priorCloseText, string? contextWindowJson)
    {
        var latestAvailable = TryReadLatest(latestBarJson, out var close, out var high, out var low, out var volume);
        Gate(features, "canonical.latestBar", latestAvailable);

        decimal priorClose = 0;
        var priorAvailable = latestAvailable && decimal.TryParse(priorCloseText, NumberStyles.Number,
            CultureInfo.InvariantCulture, out priorClose) && priorClose != 0;
        Gate(features, "canonical.priorClose5", priorAvailable);

        var contextAvailable = TryReadWindow(contextWindowJson, out var window) && window.BarCount >= 20;
        Gate(features, "canonical.context20", contextAvailable);

        // These gates explicitly distinguish an absent external feed from a measured neutral value.
        Gate(features, "external.orderFlow", false);
        Gate(features, "external.levelTwo", false);
        Gate(features, "external.optionsPositioning", false);
        Gate(features, "external.marketBreadth", false);

        if (latestAvailable)
        {
            var range = high - low;
            features["market.rangeFraction"] = close == 0 ? 0 : range / close;
            features["market.closeLocation"] = range == 0 ? .5m : (close - low) / range;
            features["market.volumeLog"] = (decimal)Math.Log(1 + (double)Math.Max(0, volume));
        }

        decimal? momentum = null;
        if (priorAvailable)
        {
            momentum = (close - priorClose) / priorClose;
            features["context.momentum.return5Fraction"] = momentum.Value;
            OneHot(features, "context.momentum.direction", momentum > 0 ? "positive" : momentum < 0 ? "negative" : "flat");
        }

        if (!latestAvailable || !contextAvailable) return;

        var currentRange = high - low;
        var rangeRatio = window.MeanRange20 == 0 ? 0 : currentRange / window.MeanRange20;
        var shortRangeRatio = window.MeanRange20 == 0 ? 0 : window.MeanRange5 / window.MeanRange20;
        var relativeVolume = window.MeanVolume20 == 0 ? 0 : volume / window.MeanVolume20;
        var volumeAcceleration = window.MeanVolume20 == 0 ? 0 : window.MeanVolume5 / window.MeanVolume20;
        var bodyIntensity = window.MeanRange20 == 0 ? 0 : window.MeanBody20 / window.MeanRange20;

        features["context.volatility.meanRange20"] = window.MeanRange20;
        features["context.volatility.currentRangeRatio"] = rangeRatio;
        features["context.volatility.shortToBaselineRangeRatio"] = shortRangeRatio;
        features["context.volume.meanVolume20"] = window.MeanVolume20;
        features["context.volume.relativeVolume"] = relativeVolume;
        features["context.volume.acceleration5To20"] = volumeAcceleration;
        features["context.trend.meanBodyToRange20"] = bodyIntensity;
        features["context.trend.rangeWidth20"] = window.High20 - window.Low20;

        var volatilityState = rangeRatio >= 1.25m ? "expansion" : rangeRatio <= .75m ? "compression" : "normal";
        var volumeState = relativeVolume >= 1.25m ? "high" : relativeVolume <= .75m ? "low" : "normal";
        var auctionState = bodyIntensity >= .60m ? "directional" : bodyIntensity <= .30m ? "balanced" : "transition";
        OneHot(features, "context.regime.volatility", volatilityState);
        OneHot(features, "context.regime.volume", volumeState);
        OneHot(features, "context.regime.auction", auctionState);

        features["context.interaction.highVolumeExpansion"] =
            volumeState == "high" && volatilityState == "expansion" ? 1 : 0;
        features["context.interaction.lowVolumeCompression"] =
            volumeState == "low" && volatilityState == "compression" ? 1 : 0;
        features["context.interaction.directionalExpansion"] =
            auctionState == "directional" && volatilityState == "expansion" ? 1 : 0;
        if (momentum.HasValue)
        {
            var patternDirection = features.TryGetValue("direction", out var direction) ? Math.Sign(direction) : 0;
            features["context.interaction.directionAlignedMomentum5"] = patternDirection * momentum.Value;
            features["context.interaction.momentumParticipation"] = Math.Abs(momentum.Value) * relativeVolume;
        }
    }

    private static bool TryReadLatest(string? json, out decimal close, out decimal high, out decimal low, out decimal volume)
    {
        close = high = low = volume = 0;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryDecimal(document.RootElement, "close", out close) &&
                   TryDecimal(document.RootElement, "high", out high) &&
                   TryDecimal(document.RootElement, "low", out low) &&
                   TryDecimal(document.RootElement, "volume", out volume);
        }
        catch (JsonException) { return false; }
    }

    private static bool TryReadWindow(string? json, out ContextWindow window)
    {
        window = default;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json); var root = document.RootElement;
            if (!TryDecimal(root, "barCount", out var count) || !TryDecimal(root, "meanRange5", out var range5) ||
                !TryDecimal(root, "meanRange20", out var range20) || !TryDecimal(root, "meanVolume5", out var volume5) ||
                !TryDecimal(root, "meanVolume20", out var volume20) || !TryDecimal(root, "meanBody20", out var body20) ||
                !TryDecimal(root, "high20", out var high20) || !TryDecimal(root, "low20", out var low20)) return false;
            window = new((int)count, range5, range20, volume5, volume20, body20, high20, low20);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryDecimal(JsonElement root, string name, out decimal value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var node) || node.ValueKind == JsonValueKind.Null) return false;
        return node.ValueKind == JsonValueKind.Number ? node.TryGetDecimal(out value) :
            decimal.TryParse(node.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static void Gate(Dictionary<string, decimal> features, string source, bool available) =>
        features[$"context.availability.{source}"] = available ? 1 : 0;

    private static void OneHot(Dictionary<string, decimal> features, string family, string state) =>
        features[$"{family}.{state}"] = 1;

    private readonly record struct ContextWindow(int BarCount, decimal MeanRange5, decimal MeanRange20,
        decimal MeanVolume5, decimal MeanVolume20, decimal MeanBody20, decimal High20, decimal Low20);
}
