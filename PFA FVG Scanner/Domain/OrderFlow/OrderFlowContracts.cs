using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Domain.Sessions;

namespace PFA_FVG_Scanner.Domain.OrderFlow;

public enum OrderFlowEventKind { Trade, Quote }
public enum OrderFlowSourceOperation { Original, Correction, Cancel }
public enum TradeAggressorSide { Buy, Sell, Unknown }
public enum AggressorClassificationMethod { AtAsk, AtBid, TickUp, TickDown, Unclassified }

[Flags]
public enum OrderFlowQualityFlags
{
    None=0,EquivalentDuplicate=1,OutOfSequence=2,InvalidTrade=4,InvalidQuote=8,CrossedQuote=16,
    MissingCorrectionTarget=32,LateArrival=64,UnresolvedInstrument=128,Cancelled=256,Corrected=512
}

public sealed record ProviderOrderFlowEvent(
    string Provider,string ProviderEventId,string InstrumentId,string? ContractId,string ProviderSymbol,
    OrderFlowEventKind Kind,OrderFlowSourceOperation Operation,long? ProviderSequence,DateTime EventTimeUtc,
    DateTime ReceivedTimeUtc,decimal? TradePrice=null,decimal? TradeSize=null,decimal? BidPrice=null,
    decimal? AskPrice=null,decimal? BidSize=null,decimal? AskSize=null,string? CorrectsProviderEventId=null,
    string SourceVersion="unknown",string? RawReference=null);

public sealed record CanonicalOrderFlowEvent(
    string CanonicalEventId,int Revision,string Provider,string ProviderEventId,string InstrumentId,string? ContractId,
    string ProviderSymbol,OrderFlowEventKind Kind,OrderFlowSourceOperation Operation,long? ProviderSequence,
    DateTime EventTimeUtc,DateTime KnownAtUtc,decimal? TradePrice,decimal? TradeSize,decimal? BidPrice,
    decimal? AskPrice,decimal? BidSize,decimal? AskSize,string? CorrectsProviderEventId,string? SupersedesCanonicalEventId,string CanonicalizationVersion,
    OrderFlowQualityFlags QualityFlags,string ContentHash,string? RawReference);

public sealed record OrderFlowCanonicalizationBatch(
    IReadOnlyList<CanonicalOrderFlowEvent> Accepted,int EquivalentDuplicates,int Rejected,int Corrections,
    IReadOnlyList<string> Diagnostics);

public interface IOrderFlowProviderAdapter
{
    string Provider { get; }
    ProviderOrderFlowEvent Adapt(string payload,string instrumentId,string? contractId,string providerSymbol,DateTime receivedAtUtc);
}

public sealed class OrderFlowCanonicalizer
{
    public const string Version="1.0.0";
    public OrderFlowCanonicalizationBatch Canonicalize(IReadOnlyList<ProviderOrderFlowEvent> source,IReadOnlyList<CanonicalOrderFlowEvent>? knownEvents=null)
    {
        source??=[];var accepted=new List<CanonicalOrderFlowEvent>();var diagnostics=new List<string>();var seen=new Dictionary<string,string>(StringComparer.Ordinal);var bySourceId=(knownEvents??[]).ToDictionary(x=>$"{x.Provider}|{x.ProviderEventId}",StringComparer.Ordinal);var lastSequence=new Dictionary<string,long>(StringComparer.Ordinal);var duplicates=0;var rejected=0;var corrections=0;
        foreach(var item in source.OrderBy(x=>Utc(x.ReceivedTimeUtc)).ThenBy(x=>x.ProviderSequence??long.MaxValue).ThenBy(x=>x.ProviderEventId,StringComparer.Ordinal))
        {
            if(string.IsNullOrWhiteSpace(item.Provider)||string.IsNullOrWhiteSpace(item.ProviderEventId)||string.IsNullOrWhiteSpace(item.InstrumentId)){rejected++;diagnostics.Add("Rejected event with missing provider, event id, or instrument.");continue;}
            var normalized=Normalize(item);var sourceKey=$"{normalized.Provider}|{normalized.ProviderEventId}";var sourceHash=Hash(JsonSerializer.Serialize(normalized));
            if(seen.TryGetValue(sourceKey,out var priorHash)){if(priorHash==sourceHash){duplicates++;continue;}rejected++;diagnostics.Add($"Provider event identity conflict: {sourceKey}.");continue;}seen[sourceKey]=sourceHash;
            var flags=Quality(normalized);var stream=$"{normalized.Provider}|{normalized.ProviderSymbol}";
            if(normalized.ProviderSequence.HasValue&&lastSequence.TryGetValue(stream,out var last)&&normalized.ProviderSequence.Value<=last)flags|=OrderFlowQualityFlags.OutOfSequence;
            if(normalized.ProviderSequence.HasValue)lastSequence[stream]=Math.Max(lastSequence.GetValueOrDefault(stream),normalized.ProviderSequence.Value);
            string? supersedes=null;if(normalized.Operation is OrderFlowSourceOperation.Correction or OrderFlowSourceOperation.Cancel){corrections++;if(normalized.CorrectsProviderEventId is null||!bySourceId.TryGetValue($"{normalized.Provider}|{normalized.CorrectsProviderEventId}",out var prior))flags|=OrderFlowQualityFlags.MissingCorrectionTarget;else supersedes=prior.CanonicalEventId;flags|=normalized.Operation==OrderFlowSourceOperation.Cancel?OrderFlowQualityFlags.Cancelled:OrderFlowQualityFlags.Corrected;}
            var id=$"OFE-{Hash(sourceKey)[..32]}";var content=Hash(JsonSerializer.Serialize(new{normalized.Provider,normalized.ProviderEventId,normalized.InstrumentId,normalized.ContractId,normalized.ProviderSymbol,normalized.Kind,normalized.Operation,normalized.ProviderSequence,normalized.EventTimeUtc,KnownAtUtc=normalized.ReceivedTimeUtc,normalized.TradePrice,normalized.TradeSize,normalized.BidPrice,normalized.AskPrice,normalized.BidSize,normalized.AskSize,normalized.CorrectsProviderEventId,supersedes,flags}));
            var canonical=new CanonicalOrderFlowEvent(id,1,normalized.Provider,normalized.ProviderEventId,normalized.InstrumentId,normalized.ContractId,normalized.ProviderSymbol,normalized.Kind,normalized.Operation,normalized.ProviderSequence,normalized.EventTimeUtc,normalized.ReceivedTimeUtc,normalized.TradePrice,normalized.TradeSize,normalized.BidPrice,normalized.AskPrice,normalized.BidSize,normalized.AskSize,normalized.CorrectsProviderEventId,supersedes,Version,flags,content,normalized.RawReference);
            accepted.Add(canonical);bySourceId[sourceKey]=canonical;
        }
        return new(accepted,duplicates,rejected,corrections,diagnostics);
    }
    private static ProviderOrderFlowEvent Normalize(ProviderOrderFlowEvent value)=>value with{Provider=value.Provider.Trim(),ProviderEventId=value.ProviderEventId.Trim(),InstrumentId=value.InstrumentId.Trim().ToUpperInvariant(),ProviderSymbol=value.ProviderSymbol.Trim().ToUpperInvariant(),EventTimeUtc=Utc(value.EventTimeUtc),ReceivedTimeUtc=Utc(value.ReceivedTimeUtc)};
    private static OrderFlowQualityFlags Quality(ProviderOrderFlowEvent value){var flags=OrderFlowQualityFlags.None;if(value.ReceivedTimeUtc-value.EventTimeUtc>TimeSpan.FromSeconds(5))flags|=OrderFlowQualityFlags.LateArrival;if(value.Kind==OrderFlowEventKind.Trade&&(value.TradePrice is null or <=0||value.TradeSize is null or <=0))flags|=OrderFlowQualityFlags.InvalidTrade;if(value.Kind==OrderFlowEventKind.Quote&&(value.BidPrice is null or <=0||value.AskPrice is null or <=0||value.BidSize is null or <0||value.AskSize is null or <0))flags|=OrderFlowQualityFlags.InvalidQuote;if(value.Kind==OrderFlowEventKind.Quote&&value.BidPrice>=value.AskPrice)flags|=OrderFlowQualityFlags.CrossedQuote;return flags;}
    internal static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(value,DateTimeKind.Utc):value.ToUniversalTime();
    internal static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record ClassifiedTrade(
    string CanonicalEventId,string InstrumentId,DateTime EventTimeUtc,DateTime KnownAtUtc,decimal Price,decimal Size,
    TradeAggressorSide Side,AggressorClassificationMethod Method,string ClassifierVersion,string DataRevision,
    OrderFlowQualityFlags QualityFlags);

public sealed class TradeAggressorClassifier
{
    public const string Version="1.0.0";
    public IReadOnlyList<ClassifiedTrade> Classify(IReadOnlyList<CanonicalOrderFlowEvent> events,DateTime asOfUtc,string dataRevision)
    {
        var asOf=OrderFlowCanonicalizer.Utc(asOfUtc);decimal? bid=null,ask=null,previousTrade=null;DateTime? quoteEventTime=null,previousTradeTime=null;var output=new List<ClassifiedTrade>();
        var visible=events.Where(x=>x.KnownAtUtc<=asOf).OrderBy(x=>x.KnownAtUtc).ThenBy(x=>x.ProviderSequence??long.MaxValue).ThenBy(x=>x.CanonicalEventId,StringComparer.Ordinal).ToArray();var superseded=visible.Where(x=>x.SupersedesCanonicalEventId is not null).Select(x=>x.SupersedesCanonicalEventId!).ToHashSet(StringComparer.Ordinal);
        foreach(var item in visible)
        {
            if(item.Operation==OrderFlowSourceOperation.Cancel||superseded.Contains(item.CanonicalEventId))continue;
            if(item.Kind==OrderFlowEventKind.Quote){if((item.QualityFlags&(OrderFlowQualityFlags.InvalidQuote|OrderFlowQualityFlags.CrossedQuote))==0){bid=item.BidPrice;ask=item.AskPrice;quoteEventTime=item.EventTimeUtc;}continue;}
            if(item.TradePrice is null||item.TradeSize is null||(item.QualityFlags&OrderFlowQualityFlags.InvalidTrade)!=0)continue;
            var side=TradeAggressorSide.Unknown;var method=AggressorClassificationMethod.Unclassified;
            if(quoteEventTime<=item.EventTimeUtc&&ask.HasValue&&item.TradePrice>=ask){side=TradeAggressorSide.Buy;method=AggressorClassificationMethod.AtAsk;}
            else if(quoteEventTime<=item.EventTimeUtc&&bid.HasValue&&item.TradePrice<=bid){side=TradeAggressorSide.Sell;method=AggressorClassificationMethod.AtBid;}
            else if(previousTradeTime<=item.EventTimeUtc&&previousTrade.HasValue&&item.TradePrice>previousTrade){side=TradeAggressorSide.Buy;method=AggressorClassificationMethod.TickUp;}
            else if(previousTradeTime<=item.EventTimeUtc&&previousTrade.HasValue&&item.TradePrice<previousTrade){side=TradeAggressorSide.Sell;method=AggressorClassificationMethod.TickDown;}
            output.Add(new(item.CanonicalEventId,item.InstrumentId,item.EventTimeUtc,item.KnownAtUtc,item.TradePrice.Value,item.TradeSize.Value,side,method,Version,dataRevision,item.QualityFlags));previousTrade=item.TradePrice;previousTradeTime=item.EventTimeUtc;
        }
        return output;
    }
}

public sealed record OrderFlowPriceLevel(decimal Price,decimal TotalVolume,decimal BuyVolume,decimal SellVolume,decimal UnknownVolume,decimal Delta,int Trades);
public sealed record OrderFlowFeatureSnapshot(
    string SnapshotId,string InstrumentId,string? ContractId,DateTime WindowStartUtc,DateTime WindowEndUtc,
    DateTime KnownAtUtc,string TradingSessionId,string SessionAssignmentVersion,string FeatureSetVersion,string DataRevision,
    decimal TotalVolume,decimal BuyVolume,decimal SellVolume,decimal UnknownVolume,decimal Delta,decimal CumulativeDelta,
    decimal? PointOfControlPrice,decimal? LastBidAskImbalance,IReadOnlyList<OrderFlowPriceLevel> Profile,
    IReadOnlyList<string> SourceReferences,OrderFlowQualityFlags QualityFlags,string ContentHash);

public sealed class OrderFlowFeatureEngine
{
    public const string Version="1.0.0";private readonly ITradingSessionService _sessions;public OrderFlowFeatureEngine(ITradingSessionService sessions)=>_sessions=sessions;
    public OrderFlowFeatureSnapshot Build(string instrumentId,string? contractId,DateTime startUtc,DateTime endUtc,decimal priceIncrement,DateTime asOfUtc,string dataRevision,IReadOnlyList<CanonicalOrderFlowEvent> events,IReadOnlyList<ClassifiedTrade> trades)
    {
        var start=OrderFlowCanonicalizer.Utc(startUtc);var end=OrderFlowCanonicalizer.Utc(endUtc);var asOf=OrderFlowCanonicalizer.Utc(asOfUtc);if(end<=start)throw new ArgumentException("Window end must be after start.");if(priceIncrement<=0)throw new ArgumentOutOfRangeException(nameof(priceIncrement));
        var session=_sessions.Assign(instrumentId,start);if(end>session.Session.SessionCloseUtc)throw new ArgumentException("Order-flow profiles may not cross a trading-session boundary.");
        var selected=trades.Where(x=>x.InstrumentId==instrumentId&&x.EventTimeUtc>=start&&x.EventTimeUtc<end&&x.KnownAtUtc<=asOf).ToArray();var levels=selected.GroupBy(x=>Bucket(x.Price,priceIncrement)).Select(g=>new OrderFlowPriceLevel(g.Key,g.Sum(x=>x.Size),g.Where(x=>x.Side==TradeAggressorSide.Buy).Sum(x=>x.Size),g.Where(x=>x.Side==TradeAggressorSide.Sell).Sum(x=>x.Size),g.Where(x=>x.Side==TradeAggressorSide.Unknown).Sum(x=>x.Size),g.Where(x=>x.Side==TradeAggressorSide.Buy).Sum(x=>x.Size)-g.Where(x=>x.Side==TradeAggressorSide.Sell).Sum(x=>x.Size),g.Count())).OrderBy(x=>x.Price).ToArray();
        var buy=selected.Where(x=>x.Side==TradeAggressorSide.Buy).Sum(x=>x.Size);var sell=selected.Where(x=>x.Side==TradeAggressorSide.Sell).Sum(x=>x.Size);var unknown=selected.Where(x=>x.Side==TradeAggressorSide.Unknown).Sum(x=>x.Size);var visibleEvents=events.Where(x=>x.InstrumentId==instrumentId&&x.EventTimeUtc>=start&&x.EventTimeUtc<end&&x.KnownAtUtc<=asOf).ToArray();var superseded=visibleEvents.Where(x=>x.SupersedesCanonicalEventId is not null).Select(x=>x.SupersedesCanonicalEventId!).ToHashSet(StringComparer.Ordinal);var lastQuote=visibleEvents.Where(x=>x.Kind==OrderFlowEventKind.Quote&&x.Operation!=OrderFlowSourceOperation.Cancel&&!superseded.Contains(x.CanonicalEventId)&&x.BidSize.HasValue&&x.AskSize.HasValue).OrderBy(x=>x.KnownAtUtc).LastOrDefault();decimal? imbalance=lastQuote is null||lastQuote.BidSize+lastQuote.AskSize==0?null:(lastQuote.BidSize-lastQuote.AskSize)/(lastQuote.BidSize+lastQuote.AskSize);
        var sessionTrades=trades.Where(x=>x.InstrumentId==instrumentId&&x.EventTimeUtc>=session.Session.SessionOpenUtc&&x.EventTimeUtc<end&&x.KnownAtUtc<=asOf).ToArray();var cumulative=sessionTrades.Where(x=>x.Side==TradeAggressorSide.Buy).Sum(x=>x.Size)-sessionTrades.Where(x=>x.Side==TradeAggressorSide.Sell).Sum(x=>x.Size);
        var flags=visibleEvents.Aggregate(OrderFlowQualityFlags.None,(current,x)=>current|x.QualityFlags);var sources=visibleEvents.Select(x=>x.CanonicalEventId).Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();var poc=levels.OrderByDescending(x=>x.TotalVolume).ThenBy(x=>x.Price).FirstOrDefault()?.Price;var identity=JsonSerializer.Serialize(new{instrumentId,contractId,start,end,KnownAt=asOf,session.Session.TradingSessionId,Version,dataRevision,Levels=levels,buy,sell,unknown,cumulative,imbalance,sources,flags});var hash=OrderFlowCanonicalizer.Hash(identity);
        return new($"OFS-{hash[..32]}",instrumentId,contractId,start,end,asOf,session.Session.TradingSessionId,session.AssignmentVersion,Version,dataRevision,buy+sell+unknown,buy,sell,unknown,buy-sell,cumulative,poc,imbalance,levels,sources,flags,hash);
    }
    private static decimal Bucket(decimal price,decimal increment)=>Math.Floor(price/increment)*increment;
}

public sealed record OrderFlowRetentionPolicy(string PolicyVersion,int RawEventRetentionDays,int FeatureRetentionDays,bool AutomaticDeletionEnabled=false);
