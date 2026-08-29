using System.Globalization;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Patterns.Structure;

public sealed record PullbackContinuationGeometry(string Trend,decimal ImpulseOrigin,decimal ImpulseExtreme,
    decimal PullbackExtreme,decimal RetracementFraction,decimal ReclaimLevel,decimal RangeLower,decimal RangeUpper,
    decimal DetectionClose,IReadOnlyList<string> SourceBarIds):IPatternGeometry;

public sealed class PullbackContinuationPatternModule:IMarketPatternDetector
{
    public const string Identifier="pullback-continuation";
    public string ModuleId=>Identifier;
    public string ModuleVersion=>"capture-1.0.0";
    public IReadOnlySet<string> SupportedTimeframes{get;}=
        new HashSet<string>(StringComparer.OrdinalIgnoreCase){"5m","15m","1h"};

    public PatternDetectionResult Detect(MarketPatternContext context)
    {
        var rejection=MarketPatternContract.Validate(this,context);
        if(rejection is not null)return PatternDetectionResult.Rejected(rejection);
        var bars=context.Bars.OrderBy(x=>x.OpenTimeUtc).TakeLast(6).ToArray();
        if(bars.Length<6)return PatternDetectionResult.Rejected("Pullback continuation requires six completed bars.");
        var bullish=TryBullish(bars,out var geometry);var bearish=!bullish&&TryBearish(bars,out geometry);
        if(!bullish&&!bearish)return PatternDetectionResult.Success([]);
        var current=bars[^1];var type=bullish?"BullishPullbackContinuation":"BearishPullbackContinuation";
        var discriminator=string.Join('|',type,geometry!.ImpulseOrigin.ToString("G29",CultureInfo.InvariantCulture),
            geometry.PullbackExtreme.ToString("G29",CultureInfo.InvariantCulture),geometry.ReclaimLevel.ToString("G29",CultureInfo.InvariantCulture));
        return PatternDetectionResult.Success([new(MarketPatternContract.CreateObservationId(ModuleId,ModuleVersion,
            context.InstrumentId,context.Timeframe,current.OpenTimeUtc,discriminator),ModuleId,ModuleVersion,type,
            context.InstrumentId,context.ContractId,context.Timeframe,bullish?PatternDirection.Bullish:PatternDirection.Bearish,
            current.OpenTimeUtc,current.CloseTimeUtc,PatternLifecycleState.Detected,geometry,
            bars.Select(x=>x.CanonicalBarId).ToArray(),context.QualityFlags)]);
    }

    private static bool TryBullish(CanonicalBar[] bars,out PullbackContinuationGeometry? geometry)
    {
        geometry=null;var impulse=bars[..3];var pullback=bars[3..5];var detection=bars[5];
        var origin=impulse.Min(x=>x.Low);var extreme=impulse.Max(x=>x.High);var distance=extreme-origin;
        var trend=impulse[1].High>impulse[0].High&&impulse[2].High>impulse[1].High&&impulse[2].Close>impulse[0].High;
        var pullbackExtreme=pullback.Min(x=>x.Low);var depth=distance<=0?0:(extreme-pullbackExtreme)/distance;
        var controlled=pullback[1].Close<=pullback[0].Close&&pullbackExtreme>origin&&depth>=.2m&&depth<=.8m;
        var reclaim=pullback.Max(x=>x.High);var resumed=detection.Close>detection.Open&&detection.Close>reclaim;
        if(!trend||!controlled||!resumed)return false;
        geometry=new("Bullish",origin,extreme,pullbackExtreme,depth,reclaim,pullbackExtreme,
            Math.Max(extreme,detection.High),detection.Close,bars.Select(x=>x.CanonicalBarId).ToArray());return true;
    }

    private static bool TryBearish(CanonicalBar[] bars,out PullbackContinuationGeometry? geometry)
    {
        geometry=null;var impulse=bars[..3];var pullback=bars[3..5];var detection=bars[5];
        var origin=impulse.Max(x=>x.High);var extreme=impulse.Min(x=>x.Low);var distance=origin-extreme;
        var trend=impulse[1].Low<impulse[0].Low&&impulse[2].Low<impulse[1].Low&&impulse[2].Close<impulse[0].Low;
        var pullbackExtreme=pullback.Max(x=>x.High);var depth=distance<=0?0:(pullbackExtreme-extreme)/distance;
        var controlled=pullback[1].Close>=pullback[0].Close&&pullbackExtreme<origin&&depth>=.2m&&depth<=.8m;
        var reclaim=pullback.Min(x=>x.Low);var resumed=detection.Close<detection.Open&&detection.Close<reclaim;
        if(!trend||!controlled||!resumed)return false;
        geometry=new("Bearish",origin,extreme,pullbackExtreme,depth,reclaim,
            Math.Min(extreme,detection.Low),pullbackExtreme,detection.Close,bars.Select(x=>x.CanonicalBarId).ToArray());return true;
    }
}
