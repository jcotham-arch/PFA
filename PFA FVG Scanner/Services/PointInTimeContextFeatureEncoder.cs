using System.Globalization;
using System.Text.Json;

namespace PFA_FVG_Scanner.Services;

/// <summary>
/// Encodes only information known at the decision clock. Missing sources are represented by
/// availability gates; their measurements are intentionally omitted rather than imputed as zero.
/// </summary>
public static class PointInTimeContextFeatureEncoder
{
    public const string Version = "point-in-time-context-features-1.4.0";

    public static void Add(Dictionary<string, decimal> features, string? latestBarJson,
        string? priorCloseText, string? contextWindowJson,string? orderFlowSnapshotJson=null,
        string? seasonalityHistoryJson=null,string? crossMarketJson=null)
    {
        var latestAvailable = TryReadLatest(latestBarJson, out var close, out var high, out var low, out var volume);
        Gate(features, "canonical.latestBar", latestAvailable);

        decimal priorClose = 0;
        var priorAvailable = latestAvailable && decimal.TryParse(priorCloseText, NumberStyles.Number,
            CultureInfo.InvariantCulture, out priorClose) && priorClose != 0;
        Gate(features, "canonical.priorClose5", priorAvailable);

        var contextAvailable = TryReadWindow(contextWindowJson, out var window) && window.BarCount >= 20;
        Gate(features, "canonical.context20", contextAvailable);
        var trendAvailable=contextAvailable&&window.HasTrend;
        Gate(features,"canonical.trend20",trendAvailable);
        var seasonalAvailable=TryReadSeasonality(seasonalityHistoryJson,out var seasonal)&&seasonal.SampleCount>=10;
        Gate(features,"canonical.seasonalityHistory",seasonalAvailable);
        var crossMarketAvailable=TryReadCrossMarket(crossMarketJson,out var peers)&&peers.Length>0;
        Gate(features,"canonical.crossMarket",crossMarketAvailable);

        // These gates explicitly distinguish an absent external feed from a measured neutral value.
        var orderFlowAvailable=TryReadOrderFlow(orderFlowSnapshotJson,out var orderFlow);
        Gate(features, "external.orderFlow", orderFlowAvailable);
        Gate(features, "external.levelTwo", false);
        Gate(features, "external.optionsPositioning", false);
        Gate(features, "external.marketBreadth", false);

        if(orderFlowAvailable)
        {
            features["context.orderFlow.totalVolumeLog"]=(decimal)Math.Log(1+(double)Math.Max(0,orderFlow.TotalVolume));
            features["context.orderFlow.buyShare"]=orderFlow.TotalVolume==0?0:orderFlow.BuyVolume/orderFlow.TotalVolume;
            features["context.orderFlow.sellShare"]=orderFlow.TotalVolume==0?0:orderFlow.SellVolume/orderFlow.TotalVolume;
            features["context.orderFlow.unknownShare"]=orderFlow.TotalVolume==0?0:orderFlow.UnknownVolume/orderFlow.TotalVolume;
            features["context.orderFlow.deltaFraction"]=orderFlow.TotalVolume==0?0:orderFlow.Delta/orderFlow.TotalVolume;
            features["context.orderFlow.cumulativeDeltaToWindowVolume"]=orderFlow.TotalVolume==0?0:orderFlow.CumulativeDelta/orderFlow.TotalVolume;
            if(orderFlow.LastBidAskImbalance.HasValue)features["context.orderFlow.lastBidAskImbalance"]=orderFlow.LastBidAskImbalance.Value;
            if(latestAvailable&&close!=0&&orderFlow.PointOfControlPrice.HasValue)
                features["context.orderFlow.pointOfControlDistanceFraction"]=(orderFlow.PointOfControlPrice.Value-close)/close;
        }

        if(seasonalAvailable)
        {
            features["context.seasonality.historySampleCountLog"]=(decimal)Math.Log(1+seasonal.SampleCount);
            features["context.seasonality.meanReturnFractionAtClock"]=seasonal.MeanReturnFraction;
            features["context.seasonality.meanAbsoluteReturnFractionAtClock"]=seasonal.MeanAbsoluteReturnFraction;
            features["context.seasonality.positiveCloseRateAtClock"]=seasonal.PositiveCloseRate;
            features["context.seasonality.directionalBiasAtClock"]=2*seasonal.PositiveCloseRate-1;
            if(latestAvailable)
            {var seasonalCurrentRange=high-low;features["context.seasonality.rangeVsClockBaseline"]=seasonal.MeanRange==0?0:seasonalCurrentRange/seasonal.MeanRange;
                features["context.seasonality.volumeVsClockBaseline"]=seasonal.MeanVolume==0?0:volume/seasonal.MeanVolume;}
            if(features.TryGetValue("direction",out var patternDirection))
                features["context.interaction.directionAlignedSeasonalBias"]=Math.Sign(patternDirection)*(2*seasonal.PositiveCloseRate-1);
        }

        if(crossMarketAvailable)
        {
            var peerReturns=peers.Select(x=>x.Return5Fraction).ToArray();
            var positive=peerReturns.Count(x=>x>0);var negative=peerReturns.Count(x=>x<0);
            var breadth=(positive-negative)/(decimal)peers.Length;
            features["context.crossMarket.peerCountLog"]=(decimal)Math.Log(1+peers.Length);
            features["context.crossMarket.meanReturn5Fraction"]=peerReturns.Average();
            features["context.crossMarket.meanAbsoluteReturn5Fraction"]=peerReturns.Average(Math.Abs);
            features["context.crossMarket.positiveShare"]=positive/(decimal)peers.Length;
            features["context.crossMarket.directionalBreadth"]=breadth;
            features["context.crossMarket.returnDispersion"]=MeanAbsoluteDeviation(peerReturns);
            foreach(var peer in peers)features[$"context.crossMarket.peerReturn5.{FeatureId(peer.InstrumentId)}"]=peer.Return5Fraction;
            if(features.TryGetValue("direction",out var patternDirection))
                features["context.interaction.directionAlignedCrossMarketBreadth"]=Math.Sign(patternDirection)*breadth;
        }

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

        if(trendAvailable)
        {
            var net=window.LatestClose20-window.FirstClose20;
            var efficiency=window.PathBody20==0?0:Math.Abs(net)/window.PathBody20;
            var signedEfficiency=window.PathBody20==0?0:net/window.PathBody20;
            var width=window.High20-window.Low20;
            var location=width==0?.5m:(window.LatestClose20-window.Low20)/width;
            features["context.trend.netChangePoints20"]=net;
            features["context.trend.netChangeFraction20"]=window.FirstClose20==0?0:net/window.FirstClose20;
            features["context.trend.pathBodyPoints20"]=window.PathBody20;
            features["context.trend.efficiency20"]=efficiency;
            features["context.trend.signedEfficiency20"]=signedEfficiency;
            features["context.trend.closeSlopePointsPerBar20"]=net/19m;
            features["context.trend.closeLocation20"]=location;
            features["context.trend.upBodyRate20"]=window.UpBodyRate20;
            features["context.trend.directionalBodyRate20"]=2*window.UpBodyRate20-1;
            var trendState=efficiency>=.45m?"trend":efficiency<=.20m?"balance":"transition";
            var directionState=net>0?"up":net<0?"down":"flat";
            OneHot(features,"context.regime.trendBalance",trendState);
            OneHot(features,"context.regime.trendDirection",directionState);
            if(features.TryGetValue("direction",out var patternDirection))
            {
                features["context.interaction.directionAlignedTrendEfficiency20"]=Math.Sign(patternDirection)*signedEfficiency;
                features["context.interaction.directionAlignedRangeLocation20"]=Math.Sign(patternDirection)*(2*location-1);
            }
        }

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
            decimal firstClose=0,latestClose=0,pathBody=0,upBodyRate=0;
            var hasTrend=TryDecimal(root,"firstClose20",out firstClose)&&TryDecimal(root,"latestClose20",out latestClose)&&
                TryDecimal(root,"pathBody20",out pathBody)&&TryDecimal(root,"upBodyRate20",out upBodyRate);
            window = new((int)count, range5, range20, volume5, volume20, body20, high20, low20,
                hasTrend,firstClose,latestClose,pathBody,upBodyRate);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryReadOrderFlow(string? json,out OrderFlowWindow window)
    {
        window=default;if(string.IsNullOrWhiteSpace(json))return false;
        try
        {using var document=JsonDocument.Parse(json);var root=document.RootElement;
            if(!TryDecimal(root,"TotalVolume",out var total)||total<=0||!TryDecimal(root,"BuyVolume",out var buy)||
               !TryDecimal(root,"SellVolume",out var sell)||!TryDecimal(root,"UnknownVolume",out var unknown)||
               !TryDecimal(root,"Delta",out var delta)||!TryDecimal(root,"CumulativeDelta",out var cumulative))return false;
            decimal? poc=TryDecimal(root,"PointOfControlPrice",out var p)?p:null;
            decimal? imbalance=TryDecimal(root,"LastBidAskImbalance",out var i)?i:null;
            window=new(total,buy,sell,unknown,delta,cumulative,poc,imbalance);return true;}
        catch(JsonException){return false;}
    }

    private static bool TryReadSeasonality(string? json,out SeasonalWindow window)
    {window=default;if(string.IsNullOrWhiteSpace(json))return false;try{using var document=JsonDocument.Parse(json);var root=document.RootElement;
        if(!TryDecimal(root,"sampleCount",out var count)||!TryDecimal(root,"meanRange",out var range)||!TryDecimal(root,"meanVolume",out var volume)||
           !TryDecimal(root,"meanReturnFraction",out var meanReturn)||!TryDecimal(root,"meanAbsoluteReturnFraction",out var meanAbsolute)||
           !TryDecimal(root,"positiveCloseRate",out var positive))return false;
        window=new((int)count,range,volume,meanReturn,meanAbsolute,positive);return true;}catch(JsonException){return false;}}

    private static bool TryReadCrossMarket(string? json,out CrossMarketPeer[] peers)
    {peers=[];if(string.IsNullOrWhiteSpace(json))return false;try{using var document=JsonDocument.Parse(json);
        if(document.RootElement.ValueKind!=JsonValueKind.Array)return false;var values=new List<CrossMarketPeer>();
        foreach(var item in document.RootElement.EnumerateArray())if(item.TryGetProperty("instrumentId",out var instrument)&&
            instrument.ValueKind==JsonValueKind.String&&TryDecimal(item,"return5Fraction",out var value))
            values.Add(new(instrument.GetString()!,value));peers=values.OrderBy(x=>x.InstrumentId,StringComparer.Ordinal).ToArray();return peers.Length>0;}
        catch(JsonException){return false;}}

    private static decimal MeanAbsoluteDeviation(decimal[] values)
    {var mean=values.Average();return values.Average(x=>Math.Abs(x-mean));}
    private static string FeatureId(string value)=>new(value.Select(x=>char.IsLetterOrDigit(x)||x is '-' or '_'?x:'_').ToArray());

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
        decimal MeanVolume5, decimal MeanVolume20, decimal MeanBody20, decimal High20, decimal Low20,
        bool HasTrend,decimal FirstClose20,decimal LatestClose20,decimal PathBody20,decimal UpBodyRate20);
    private readonly record struct OrderFlowWindow(decimal TotalVolume,decimal BuyVolume,decimal SellVolume,
        decimal UnknownVolume,decimal Delta,decimal CumulativeDelta,decimal? PointOfControlPrice,decimal? LastBidAskImbalance);
    private readonly record struct SeasonalWindow(int SampleCount,decimal MeanRange,decimal MeanVolume,
        decimal MeanReturnFraction,decimal MeanAbsoluteReturnFraction,decimal PositiveCloseRate);
    private readonly record struct CrossMarketPeer(string InstrumentId,decimal Return5Fraction);
}
