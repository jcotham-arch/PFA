using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.Domain.OrderFlow;

namespace PFA_FVG_Scanner.Domain.Context;

public enum ContextFeatureAvailability { Available,InsufficientHistory,SourceUnavailable }

public sealed record ResearchContextFeatureSet(string FamilyId,string Version,ContextFeatureAvailability Availability,
    IReadOnlyDictionary<string,decimal> NumericFeatures,IReadOnlyDictionary<string,string> CategoricalFeatures,
    IReadOnlyList<string> SourceReferences,string? Reason=null);

public sealed record ResearchContextSnapshot(string SnapshotId,string Version,string InstrumentId,string? ContractId,
    string Timeframe,DateTime DecisionTimeUtc,DateTime KnownAtUtc,IReadOnlyList<ResearchContextFeatureSet> Families,
    string ContentHash,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed class BarDerivedResearchContextEngine(ITradingSessionService sessions)
{
    public const string Version="bar-derived-context-1.2.0";

    public ResearchContextSnapshot Build(string instrumentId,string? contractId,string timeframe,DateTime decisionTimeUtc,
        IReadOnlyList<CanonicalBar> source,OrderFlowFeatureSnapshot? orderFlow=null)
    {
        var decision=Utc(decisionTimeUtc);var bars=source.Where(x=>x.IsComplete&&x.CloseTimeUtc<=decision&&
            x.InstrumentId.Equals(instrumentId,StringComparison.OrdinalIgnoreCase)&&
            (contractId is null||x.ContractId==contractId)).OrderBy(x=>x.CloseTimeUtc).ToArray();
        var recent=bars.TakeLast(120).ToArray();var refs=recent.Select(x=>$"{x.CanonicalBarId}:r{x.Revision}").ToArray();
        var values=new List<ResearchContextFeatureSet>{Seasonality(decision),Session(instrumentId,decision),
            Volatility(recent,refs),Volume(recent,refs),Trend(recent,refs),Momentum(recent,refs),
            LiquidityProxy(recent,refs),ContractCycle(contractId,decision),OrderFlow(instrumentId,contractId,decision,orderFlow),
            SourceUnavailable("level-two","Timestamped market-depth snapshots are not connected for this decision clock."),
            SourceUnavailable("options-positioning","A revisioned options-positioning source is not connected for this decision clock."),
            SourceUnavailable("market-breadth","A revisioned breadth source is not connected for this decision clock.")};
        var seed=JsonSerializer.Serialize(new{Version,instrumentId,contractId,timeframe,decision,Families=values});
        var hash=Hash(seed);return new($"RCS-{hash[..32]}",Version,instrumentId,contractId,timeframe,decision,decision,values,hash);
    }

    private static ResearchContextFeatureSet Seasonality(DateTime clock)
    {var minute=clock.Hour*60+clock.Minute;var n=new Dictionary<string,decimal>{{"minuteOfDay",minute},{"hourSin",(decimal)Math.Sin(2*Math.PI*minute/1440d)},{"hourCos",(decimal)Math.Cos(2*Math.PI*minute/1440d)},{"weekdaySin",(decimal)Math.Sin(2*Math.PI*(int)clock.DayOfWeek/7d)},{"weekdayCos",(decimal)Math.Cos(2*Math.PI*(int)clock.DayOfWeek/7d)},{"monthSin",(decimal)Math.Sin(2*Math.PI*(clock.Month-1)/12d)},{"monthCos",(decimal)Math.Cos(2*Math.PI*(clock.Month-1)/12d)}};return Available("seasonality",n,new Dictionary<string,string>(),[]);}
    private ResearchContextFeatureSet Session(string instrument,DateTime clock)
    {var value=sessions.Assign(instrument,clock);var elapsed=(decimal)(clock-value.Session.SessionOpenUtc).TotalMinutes;var duration=(decimal)(value.Session.SessionCloseUtc-value.Session.SessionOpenUtc).TotalMinutes;return Available("session-structure",new Dictionary<string,decimal>{{"minutesFromSessionOpen",elapsed},{"sessionProgress",duration<=0?0:elapsed/duration},{"isHoliday",value.Session.IsHoliday?1:0},{"isEarlyClose",value.Session.IsEarlyClose?1:0}},new Dictionary<string,string>{{"segment",value.Segment.ToString()},{"assignmentQuality",value.Session.Quality.ToString()}},[]);}
    private static ResearchContextFeatureSet Volatility(CanonicalBar[] bars,string[] refs)
    {if(bars.Length<21)return Missing("volatility-regime",bars.Length,21,refs);var ranges=bars.Select(x=>x.High-x.Low).ToArray();var recent=ranges.TakeLast(5).Average();var baseline=ranges.TakeLast(20).Average();var ratio=baseline==0?0:recent/baseline;var returns=Returns(bars.TakeLast(21).ToArray());var realized=(decimal)Math.Sqrt((double)returns.Average(x=>x*x));var regime=ratio>=1.25m?"Expansion":ratio<=.75m?"Compression":"Normal";return Available("volatility-regime",new Dictionary<string,decimal>{{"meanRange5",recent},{"meanRange20",baseline},{"rangeRatio5To20",ratio},{"realizedReturnVolatility20",realized}},new Dictionary<string,string>{{"regime",regime}},refs);}
    private static ResearchContextFeatureSet Volume(CanonicalBar[] bars,string[] refs)
    {if(bars.Length<21)return Missing("volume-regime",bars.Length,21,refs);var last=bars[^1].Volume;var avg5=bars.TakeLast(5).Average(x=>x.Volume);var avg20=bars.TakeLast(20).Average(x=>x.Volume);var relative=avg20==0?0:last/avg20;var regime=relative>=1.25m?"High":relative<=.75m?"Low":"Normal";return Available("volume-regime",new Dictionary<string,decimal>{{"lastVolume",last},{"meanVolume5",avg5},{"meanVolume20",avg20},{"relativeVolume",relative},{"volumeAcceleration",avg20==0?0:avg5/avg20}},new Dictionary<string,string>{{"regime",regime}},refs);}
    private static ResearchContextFeatureSet Trend(CanonicalBar[] bars,string[] refs)
    {if(bars.Length<21)return Missing("trend-balance-regime",bars.Length,21,refs);var window=bars.TakeLast(20).ToArray();var net=Math.Abs(window[^1].Close-window[0].Open);var path=window.Sum(x=>Math.Abs(x.Close-x.Open));var high=window.Max(x=>x.High);var low=window.Min(x=>x.Low);var efficiency=path==0?0:net/path;var slope=(window[^1].Close-window[0].Close)/19m;var regime=efficiency>=.45m?"Trend":efficiency<=.20m?"Balance":"Transition";return Available("trend-balance-regime",new Dictionary<string,decimal>{{"efficiency20",efficiency},{"closeSlope20",slope},{"rangeWidth20",high-low}},new Dictionary<string,string>{{"regime",regime}},refs);}
    private static ResearchContextFeatureSet Momentum(CanonicalBar[] bars,string[] refs)
    {if(bars.Length<7)return Missing("momentum-exhaustion",bars.Length,7,refs);var returns=Returns(bars.TakeLast(7).ToArray());var recent=returns.TakeLast(3).Sum();var prior=returns.Take(3).Sum();var last=bars[^1];var range=last.High-last.Low;return Available("momentum-exhaustion",new Dictionary<string,decimal>{{"return3",recent},{"priorReturn3",prior},{"acceleration",recent-prior},{"lastCloseLocation",range==0?.5m:(last.Close-last.Low)/range},{"lastBodyToRange",range==0?0:Math.Abs(last.Close-last.Open)/range}},new Dictionary<string,string>(),refs);}
    private static ResearchContextFeatureSet LiquidityProxy(CanonicalBar[] bars,string[] refs)
    {if(bars.Length<21)return Missing("liquidity-spread",bars.Length,21,refs);var ranges=bars.TakeLast(20).Select(x=>x.High-x.Low).ToArray();var volume=bars.TakeLast(20).Average(x=>x.Volume);return Available("liquidity-spread",new Dictionary<string,decimal>{{"barRange20",ranges.Average()},{"volume20",volume},{"rangePerVolume",volume==0?0:ranges.Average()/volume}},new Dictionary<string,string>{{"measurement","bar-proxy-only"}},refs,"Bid/ask spread and depth require quote data; these are candle-derived liquidity proxies only.");}
    private static ResearchContextFeatureSet ContractCycle(string? contractId,DateTime clock)=>new("contract-cycle",Version,ContextFeatureAvailability.InsufficientHistory,new Dictionary<string,decimal>(),new Dictionary<string,string>{{"contractId",contractId??"UNRESOLVED"}},[],"Expiration and rollover dates require a reviewed contract calendar.");
    private static ResearchContextFeatureSet OrderFlow(string instrument,string? contractId,DateTime decision,OrderFlowFeatureSnapshot? value)
    {if(value is null||!value.InstrumentId.Equals(instrument,StringComparison.OrdinalIgnoreCase)||(contractId is not null&&value.ContractId is not null&&value.ContractId!=contractId)||value.KnownAtUtc>decision||value.WindowEndUtc>decision||decision-value.WindowEndUtc>TimeSpan.FromMinutes(5)||value.TotalVolume<=0||value.SourceReferences.Count==0)
            return SourceUnavailable("order-flow","No non-empty timestamped order-flow snapshot was known within five minutes of this decision clock.");
        var numeric=new Dictionary<string,decimal>{{"totalVolume",value.TotalVolume},{"buyShare",value.BuyVolume/value.TotalVolume},{"sellShare",value.SellVolume/value.TotalVolume},{"unknownShare",value.UnknownVolume/value.TotalVolume},{"deltaFraction",value.Delta/value.TotalVolume},{"cumulativeDeltaToWindowVolume",value.CumulativeDelta/value.TotalVolume}};
        if(value.PointOfControlPrice.HasValue)numeric["pointOfControlPrice"]=value.PointOfControlPrice.Value;if(value.LastBidAskImbalance.HasValue)numeric["lastBidAskImbalance"]=value.LastBidAskImbalance.Value;
        return Available("order-flow",numeric,new Dictionary<string,string>{{"dataRevision",value.DataRevision},{"featureSetVersion",value.FeatureSetVersion}},value.SourceReferences);}
    private static decimal[] Returns(CanonicalBar[] bars)=>bars.Zip(bars.Skip(1),(a,b)=>a.Close==0?0:(b.Close-a.Close)/a.Close).ToArray();
    private static ResearchContextFeatureSet Missing(string id,int actual,int required,string[] refs)=>new(id,Version,ContextFeatureAvailability.InsufficientHistory,new Dictionary<string,decimal>(),new Dictionary<string,string>(),refs,$"Requires {required} completed bars; {actual} were available at the decision clock.");
    private static ResearchContextFeatureSet SourceUnavailable(string id,string reason)=>new(id,Version,ContextFeatureAvailability.SourceUnavailable,new Dictionary<string,decimal>(),new Dictionary<string,string>(),[],reason);
    private static ResearchContextFeatureSet Available(string id,IReadOnlyDictionary<string,decimal> numeric,IReadOnlyDictionary<string,string> categorical,IReadOnlyList<string> refs,string? reason=null)=>new(id,Version,ContextFeatureAvailability.Available,numeric,categorical,refs,reason);
    private static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(value,DateTimeKind.Utc):value.ToUniversalTime();
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
