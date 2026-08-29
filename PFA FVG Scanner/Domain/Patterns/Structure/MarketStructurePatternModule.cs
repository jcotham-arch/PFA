using System.Globalization;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Patterns.Structure;

public sealed record StructureProgressionGeometry(string Progression,decimal RangeLower,decimal RangeUpper,
    decimal DetectionClose,IReadOnlyList<string> StructureBarIds):IPatternGeometry;

public sealed class MarketStructurePatternModule:IMarketPatternDetector
{
    public const string Identifier="market-structure";
    public string ModuleId=>Identifier;
    public string ModuleVersion=>"capture-1.0.0";
    public IReadOnlySet<string> SupportedTimeframes{get;}=
        new HashSet<string>(StringComparer.OrdinalIgnoreCase){"5m","15m","1h"};

    public PatternDetectionResult Detect(MarketPatternContext context)
    {
        var rejection=MarketPatternContract.Validate(this,context);
        if(rejection is not null)return PatternDetectionResult.Rejected(rejection);
        var bars=context.Bars.OrderBy(x=>x.OpenTimeUtc).TakeLast(3).ToArray();
        if(bars.Length<3)return PatternDetectionResult.Rejected("Structure progression requires three completed bars.");
        var current=bars[2];var ascending=current.High>bars[1].High&&bars[1].High>bars[0].High&&current.Low>bars[1].Low;
        var descending=current.Low<bars[1].Low&&bars[1].Low<bars[0].Low&&current.High<bars[1].High;
        if(!ascending&&!descending)return PatternDetectionResult.Success([]);
        var progression=ascending?"AscendingStructure":"DescendingStructure";
        var geometry=new StructureProgressionGeometry(progression,bars.Min(x=>x.Low),bars.Max(x=>x.High),
            current.Close,bars.Select(x=>x.CanonicalBarId).ToArray());
        var discriminator=string.Join('|',progression,geometry.RangeLower.ToString("G29",CultureInfo.InvariantCulture),
            geometry.RangeUpper.ToString("G29",CultureInfo.InvariantCulture));
        return PatternDetectionResult.Success([new(MarketPatternContract.CreateObservationId(ModuleId,ModuleVersion,
            context.InstrumentId,context.Timeframe,current.OpenTimeUtc,discriminator),ModuleId,ModuleVersion,progression,
            context.InstrumentId,context.ContractId,context.Timeframe,ascending?PatternDirection.Bullish:PatternDirection.Bearish,
            current.OpenTimeUtc,current.CloseTimeUtc,PatternLifecycleState.Detected,geometry,
            bars.Select(x=>x.CanonicalBarId).ToArray(),context.QualityFlags)]);
    }
}
