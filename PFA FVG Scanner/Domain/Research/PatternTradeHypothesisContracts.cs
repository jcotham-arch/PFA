using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Research;

public enum HypothesisDirectionPolicy { PatternDirection, OpposePatternDirection }
public enum HypothesisExitOutcome { Target, Stop, BreakEven, TimeExit, Ambiguous, NoEntry, InvalidRisk }

public sealed record PatternTradeHypothesisDefinition(string HypothesisId,string Version,string ModuleId,
    HypothesisDirectionPolicy DirectionPolicy,string EntryPolicy,string StopPolicy,decimal TargetR,
    int MaximumHoldingMinutes,decimal StopBufferTicks,decimal EstimatedRoundTripCostTicks=0m,
    string ExitPolicy="fixed-target-or-time");

public sealed record PatternTradeHypothesisSample(string SampleId,string HypothesisId,string ObservationId,
    string InstrumentId,string? ContractId,string ModuleId,string PatternType,string Direction,
    DateTime DecisionTimeUtc,DateTime? EntryTimeUtc,decimal? EntryPrice,decimal? StopPrice,decimal? TargetPrice,
    DateTime? ExitTimeUtc,decimal? ExitPrice,HypothesisExitOutcome Outcome,decimal? GrossR,decimal? NetR,
    decimal? MaximumFavorableExcursionR,decimal? MaximumAdverseExcursionR,string Reason,string ContentHash,
    string Split="Unassigned",bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record JsonPatternGeometry(JsonElement Value):IPatternGeometry;

public sealed record PatternTradeResearchRequest(DateTime AsOfUtc,IReadOnlyList<string>? InstrumentIds=null,
    IReadOnlyList<string>? ModuleIds=null,IReadOnlyList<decimal>? TargetRs=null,
    IReadOnlyList<int>? MaximumHoldingMinutes=null,decimal StopBufferTicks=1m,
    decimal EstimatedRoundTripCostTicks=1m,IReadOnlyList<string>? StopPolicies=null,
    IReadOnlyList<string>? ExitPolicies=null);

public sealed record PatternTradeHypothesisSummary(string HypothesisId,string ModuleId,string EntryPolicy,string StopPolicy,
    HypothesisDirectionPolicy DirectionPolicy,decimal TargetR,int MaximumHoldingMinutes,string Split,
    int Samples,int Targets,int Stops,int TimeExits,int Ambiguous,int NoEntryOrInvalid,
    decimal MeanNetR,decimal WinRate,decimal ProfitFactor,decimal MaximumDrawdownR,
    bool IsTradableEvidence=false,string ExitPolicy="fixed-target-or-time",int BreakEvenExits=0);

public sealed record PatternTradeResearchRun(string RunId,string EngineVersion,DateTime AsOfUtc,
    IReadOnlyList<string> InstrumentIds,IReadOnlyList<string> ModuleIds,int ObservationCount,
    int HypothesisCount,int SampleCount,IReadOnlyList<PatternTradeHypothesisSummary> Summaries,
    string ContentHash,DateTime CreatedAtUtc,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public static class PatternTradeHypothesisEngine
{
    public const string Version="pattern-trade-hypothesis-engine-1.3.0";

    public static PatternTradeHypothesisSample Evaluate(PatternTradeHypothesisDefinition definition,
        MarketPatternObservation observation,IReadOnlyList<CanonicalBar> oneMinuteBars,decimal tickSize)
    {
        ArgumentNullException.ThrowIfNull(definition);ArgumentNullException.ThrowIfNull(observation);
        if(definition.TargetR<=0||definition.MaximumHoldingMinutes<1||tickSize<=0)
            throw new ArgumentException("Target, holding period, and tick size must be positive.");
        var direction=ResolveDirection(observation.Direction,definition.DirectionPolicy);DateTime? entryClock=null;
        var allBars=oneMinuteBars.Where(x=>x.IsComplete).OrderBy(x=>x.OpenTimeUtc).ToArray();
        var firstFuture=allBars.FirstOrDefault(x=>x.OpenTimeUtc>=observation.KnownAtUtc);
        var entryBar=definition.EntryPolicy switch
        {"next-one-minute-open"=>firstFuture,"one-minute-confirmation-close"=>firstFuture,
            _=>throw new NotSupportedException($"Entry policy '{definition.EntryPolicy}' is not supported.")};
        if(entryBar is null)return Result(HypothesisExitOutcome.NoEntry,"No completed one-minute entry bar exists after the decision clock.");
        var entry=definition.EntryPolicy=="one-minute-confirmation-close"?entryBar.Close:entryBar.Open;
        entryClock=definition.EntryPolicy=="one-minute-confirmation-close"?entryBar.CloseTimeUtc:entryBar.OpenTimeUtc;
        var stop=StructuralStop(observation,direction,definition.StopPolicy,tickSize,definition.StopBufferTicks);
        var risk=direction==PatternDirection.Bullish?entry-stop:stop-entry;
        if(risk<tickSize)return Result(HypothesisExitOutcome.InvalidRisk,"Structural stop does not create at least one tick of risk.",entryBar,entry,stop);
        var target=direction==PatternDirection.Bullish?entry+risk*definition.TargetR:entry-risk*definition.TargetR;
        var end=entryClock.Value.AddMinutes(definition.MaximumHoldingMinutes);
        var path=allBars.Where(x=>x.OpenTimeUtc>=entryClock.Value&&x.OpenTimeUtc<end).ToArray();
        var exitPolicy=definition.ExitPolicy??"fixed-target-or-time";
        if(exitPolicy is not("fixed-target-or-time" or "break-even-after-0.5r"))
            throw new NotSupportedException($"Exit policy '{exitPolicy}' is not supported.");
        decimal mfe=0,mae=0;var breakEvenActive=false;
        foreach(var bar in path)
        {
            var favorable=direction==PatternDirection.Bullish?bar.High-entry:entry-bar.Low;
            var adverse=direction==PatternDirection.Bullish?entry-bar.Low:bar.High-entry;
            mfe=Math.Max(mfe,favorable/risk);mae=Math.Max(mae,adverse/risk);
            var activeStop=breakEvenActive?entry:stop;
            var hitTarget=direction==PatternDirection.Bullish?bar.High>=target:bar.Low<=target;
            var hitStop=direction==PatternDirection.Bullish?bar.Low<=activeStop:bar.High>=activeStop;
            var activation=direction==PatternDirection.Bullish?entry+risk*.5m:entry-risk*.5m;
            var hitActivation=exitPolicy=="break-even-after-0.5r"&&!breakEvenActive&&
                (direction==PatternDirection.Bullish?bar.High>=activation:bar.Low<=activation);
            if(hitActivation&&hitStop)return Result(HypothesisExitOutcome.Ambiguous,
                "Break-even activation and structural stop occur inside the same one-minute bar; intrabar order is unknown.",entryBar,entry,stop,target,bar,null,mfe,mae);
            if(hitTarget&&hitStop)return Result(HypothesisExitOutcome.Ambiguous,
                "Stop and target occur inside the same one-minute bar; intrabar order is unknown.",entryBar,entry,stop,target,bar,null,mfe,mae);
            if(hitStop)return Result(breakEvenActive?HypothesisExitOutcome.BreakEven:HypothesisExitOutcome.Stop,
                breakEvenActive?"Break-even stop reached after activation.":"Structural stop reached first.",entryBar,entry,stop,target,bar,activeStop,mfe,mae);
            if(hitTarget)return Result(HypothesisExitOutcome.Target,"R-multiple target reached first.",entryBar,entry,stop,target,bar,target,mfe,mae);
            if(hitActivation)breakEvenActive=true;
        }
        var last=path.LastOrDefault();if(last is null)return Result(HypothesisExitOutcome.NoEntry,"No complete bar exists inside the holding window.");
        return Result(HypothesisExitOutcome.TimeExit,"Maximum holding period elapsed.",entryBar,entry,stop,target,last,last.Close,mfe,mae);

        PatternTradeHypothesisSample Result(HypothesisExitOutcome outcome,string reason,CanonicalBar? entered=null,
            decimal? entryPrice=null,decimal? stopPrice=null,decimal? targetPrice=null,CanonicalBar? exited=null,
            decimal? exitPrice=null,decimal? maxFavorable=null,decimal? maxAdverse=null)
        {
            decimal? gross=null,net=null;
            if(exitPrice.HasValue&&entryPrice.HasValue&&stopPrice.HasValue)
            {
                var unitRisk=Math.Abs(entryPrice.Value-stopPrice.Value);
                gross=unitRisk==0?null:(direction==PatternDirection.Bullish?exitPrice-entryPrice:entryPrice-exitPrice)/unitRisk;
                net=gross-definition.EstimatedRoundTripCostTicks*tickSize/unitRisk;
            }
            var identity=JsonSerializer.Serialize(new{definition,observation.ObservationId,direction,entered?.OpenTimeUtc,
                entryPrice,stopPrice,targetPrice,exited?.CloseTimeUtc,exitPrice,outcome,gross,net,maxFavorable,maxAdverse,reason,Version});
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            return new($"PTHS-{hash[..32]}",definition.HypothesisId,observation.ObservationId,observation.InstrumentId,
                observation.ContractId,observation.ModuleId,observation.PatternType,direction.ToString(),observation.KnownAtUtc,
                entryClock,entryPrice,stopPrice,targetPrice,exited?.CloseTimeUtc,exitPrice,outcome,gross,net,
                maxFavorable,maxAdverse,reason,hash);
        }
    }

    private static PatternDirection ResolveDirection(PatternDirection direction,HypothesisDirectionPolicy policy)=>
        policy==HypothesisDirectionPolicy.PatternDirection?direction:
            direction==PatternDirection.Bullish?PatternDirection.Bearish:PatternDirection.Bullish;

    private static decimal StructuralStop(MarketPatternObservation observation,PatternDirection direction,string stopPolicy,
        decimal tickSize,decimal bufferTicks)
    {
        var geometry=observation.Geometry is JsonPatternGeometry json?json.Value:
            JsonSerializer.SerializeToElement(observation.Geometry,observation.Geometry.GetType());var buffer=tickSize*bufferTicks;
        decimal? Value(params string[] names)
        {foreach(var name in names)if(TryFindDecimal(geometry,name,out var value))return value;return null;}
        var originalBoundary=observation.Direction==PatternDirection.Bullish?Value("RangeUpper"):Value("RangeLower");
        decimal level=stopPolicy switch
        {
            "extreme-invalidation"=>Value("SweepExtreme","BreakExtreme")??0,
            "boundary-invalidation"=>observation.ModuleId=="liquidity-sweep"?Value("ReferenceLevel")??0:originalBoundary??0,
            "opposite-range-invalidation"=>direction==PatternDirection.Bullish?Value("RangeLower")??0:Value("RangeUpper")??0,
            _=>throw new NotSupportedException($"Stop policy '{stopPolicy}' is not supported.")
        };
        return direction==PatternDirection.Bullish?level-buffer:level+buffer;
    }

    private static bool TryFindDecimal(JsonElement element,string name,out decimal value)
    {
        if(element.ValueKind==JsonValueKind.Object)foreach(var property in element.EnumerateObject())
        {
            if(string.Equals(property.Name,name,StringComparison.OrdinalIgnoreCase)&&property.Value.TryGetDecimal(out value))return true;
            if(TryFindDecimal(property.Value,name,out value))return true;
        }
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())
            if(TryFindDecimal(item,name,out value))return true;
        value=0;return false;
    }
}
