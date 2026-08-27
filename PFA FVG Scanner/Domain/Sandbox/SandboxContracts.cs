using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Strategies;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Domain.Sandbox;

public enum SandboxInstanceStatus { Created,Running,Stopped }
public enum SandboxOrderSide { Buy,Sell }
public enum SandboxOrderType { Market,Limit,Stop }
public enum SandboxOrderStatus { Working,PartiallyFilled,Filled,Cancelled,Rejected,Expired }

public interface ISandboxClock { DateTime UtcNow { get; } }
public sealed class SystemSandboxClock:ISandboxClock { public DateTime UtcNow=>DateTime.UtcNow; }

public sealed record SandboxAccount(string AccountId,string DisplayName,string Currency,decimal InitialBalance,DateTime CreatedAtUtc);
public sealed record SandboxInstance(string InstanceId,string AccountId,string StrategyId,string StrategyVersion,string InstrumentId,string? ContractId,SandboxInstanceStatus Status,DateTime CreatedAtUtc,DateTime? StartedAtUtc,DateTime? StoppedAtUtc);
public sealed record SandboxSignal(string SignalId,string InstanceId,SandboxOrderSide Side,SandboxOrderType OrderType,int Quantity,decimal? LimitPrice,decimal? StopPrice,DateTime KnownAtUtc,string Reason,IReadOnlyList<string> EvidenceReferences);
public sealed record SandboxOrder(string OrderId,string SignalId,string InstanceId,string AccountId,string InstrumentId,string? ContractId,SandboxOrderSide Side,SandboxOrderType OrderType,int Quantity,int FilledQuantity,decimal? LimitPrice,decimal? StopPrice,SandboxOrderStatus Status,DateTime SubmittedAtUtc,DateTime EligibleAtUtc,string FillModelVersion,string FillModelHash);
public sealed record SandboxMarketSlice(string SourceId,string InstrumentId,string? ContractId,DateTime OpenTimeUtc,DateTime CloseTimeUtc,DateTime KnownAtUtc,decimal Open,decimal High,decimal Low,decimal Close,int AvailableQuantity,string DataRevision);
public sealed record SandboxFill(string FillId,string OrderId,string InstanceId,string AccountId,string InstrumentId,string? ContractId,SandboxOrderSide Side,int Quantity,decimal Price,decimal Commission,decimal Slippage,DateTime FilledAtUtc,string SourceId,string DataRevision,string FillModelVersion,string FillModelHash);
public sealed record SandboxPosition(string AccountId,string InstrumentId,string? ContractId,int SignedQuantity,decimal AveragePrice,decimal RealizedProfitLoss,decimal Commissions,DateTime UpdatedAtUtc);
public sealed record SandboxTrade(string TradeId,string AccountId,string InstanceId,string InstrumentId,string? ContractId,int ClosedQuantity,decimal EntryPrice,decimal ExitPrice,decimal GrossProfitLoss,decimal Commission,decimal NetProfitLoss,DateTime ClosedAtUtc);
public sealed record SandboxPerformance(string AccountId,decimal InitialBalance,decimal CashBalance,decimal RealizedProfitLoss,decimal Commissions,int FillCount,int TradeCount,decimal PeakCashBalance,decimal MaximumDrawdown,DateTime AsOfUtc);
public sealed record SandboxFillModel(string Version,int LatencyMilliseconds,decimal SlippageTicks,decimal CommissionPerContract,bool AllowPartialFills=true)
{
    public string ContentHash()=>SandboxBrokerSimulator.Hash(JsonSerializer.Serialize(this));
}
public sealed record SandboxLedgerEvent(string LedgerEventId,string CommandId,string AccountId,string? InstanceId,long Sequence,string EventType,DateTime OccurredAtUtc,string PayloadJson,string ContentHash);
public sealed record SandboxLedgerDraft(string CommandId,string AccountId,string? InstanceId,string EventType,DateTime OccurredAtUtc,object Payload);
public sealed record SandboxAccountState(SandboxAccount Account,IReadOnlyDictionary<string,SandboxInstance> Instances,IReadOnlyDictionary<string,SandboxSignal> Signals,IReadOnlyDictionary<string,SandboxOrder> Orders,IReadOnlyList<SandboxFill> Fills,IReadOnlyDictionary<string,SandboxPosition> Positions,IReadOnlyList<SandboxTrade> Trades,SandboxPerformance Performance,long LastSequence);

public sealed class SandboxBrokerSimulator
{
    public SandboxOrder Submit(SandboxAccount account,SandboxInstance instance,SandboxSignal signal,SandboxFillModel model,ISandboxClock clock)
    {
        if(instance.AccountId!=account.AccountId||signal.InstanceId!=instance.InstanceId)throw new ArgumentException("Sandbox account, instance, and signal identities do not match.");
        if(instance.Status!=SandboxInstanceStatus.Running)throw new InvalidOperationException("Sandbox instance is not running.");
        if(signal.KnownAtUtc>clock.UtcNow)throw new InvalidOperationException("A future-known signal cannot be submitted.");
        if(signal.Quantity<=0)throw new ArgumentOutOfRangeException(nameof(signal.Quantity));
        if(signal.OrderType==SandboxOrderType.Limit&&signal.LimitPrice is null)throw new ArgumentException("Limit orders require a limit price.");
        if(signal.OrderType==SandboxOrderType.Stop&&signal.StopPrice is null)throw new ArgumentException("Stop orders require a stop price.");
        return new($"SBO-{Hash(signal.SignalId+model.ContentHash())[..32]}",signal.SignalId,instance.InstanceId,account.AccountId,instance.InstrumentId,instance.ContractId,signal.Side,signal.OrderType,signal.Quantity,0,signal.LimitPrice,signal.StopPrice,SandboxOrderStatus.Working,clock.UtcNow,clock.UtcNow.AddMilliseconds(model.LatencyMilliseconds),model.Version,model.ContentHash());
    }

    public (SandboxOrder Order,SandboxFill? Fill) Process(SandboxOrder order,SandboxMarketSlice market,InstrumentDefinition instrument,SandboxFillModel model,ISandboxClock clock)
    {
        if(market.KnownAtUtc>clock.UtcNow||market.CloseTimeUtc>clock.UtcNow)throw new InvalidOperationException("Sandbox processing cannot observe future market data.");
        if(order.FillModelVersion!=model.Version||order.FillModelHash!=model.ContentHash())throw new InvalidOperationException("The order's frozen fill model does not match the execution model.");
        if(order.InstrumentId!=market.InstrumentId||order.ContractId!=market.ContractId)throw new ArgumentException("Order and market slice identity mismatch.");
        if(order.Status is not(SandboxOrderStatus.Working or SandboxOrderStatus.PartiallyFilled)||market.KnownAtUtc<order.EligibleAtUtc)return(order,null);
        var price=FillPrice(order,market,instrument.TickSize,model.SlippageTicks);if(price is null||market.AvailableQuantity<=0)return(order,null);
        var remaining=order.Quantity-order.FilledQuantity;var quantity=model.AllowPartialFills?Math.Min(remaining,market.AvailableQuantity):market.AvailableQuantity>=remaining?remaining:0;if(quantity==0)return(order,null);
        var slippage=Math.Abs(price.Value-ReferencePrice(order,market));var commission=model.CommissionPerContract*quantity;var fillId=$"SBF-{Hash($"{order.OrderId}|{market.SourceId}|{order.FilledQuantity}|{quantity}|{price}")[..32]}";
        var fill=new SandboxFill(fillId,order.OrderId,order.InstanceId,order.AccountId,order.InstrumentId,order.ContractId,order.Side,quantity,price.Value,commission,slippage,market.KnownAtUtc,market.SourceId,market.DataRevision,model.Version,model.ContentHash());var filled=order.FilledQuantity+quantity;return(order with{FilledQuantity=filled,Status=filled==order.Quantity?SandboxOrderStatus.Filled:SandboxOrderStatus.PartiallyFilled},fill);
    }
    private static decimal? FillPrice(SandboxOrder order,SandboxMarketSlice market,decimal tick,decimal slippageTicks)
    {var slip=tick*slippageTicks;return order.OrderType switch{SandboxOrderType.Market=>order.Side==SandboxOrderSide.Buy?market.Open+slip:market.Open-slip,SandboxOrderType.Limit when order.Side==SandboxOrderSide.Buy&&market.Low<=order.LimitPrice=>Math.Min(order.LimitPrice!.Value,market.Open+slip),SandboxOrderType.Limit when order.Side==SandboxOrderSide.Sell&&market.High>=order.LimitPrice=>Math.Max(order.LimitPrice!.Value,market.Open-slip),SandboxOrderType.Stop when order.Side==SandboxOrderSide.Buy&&market.High>=order.StopPrice=>Math.Max(order.StopPrice!.Value,market.Open)+slip,SandboxOrderType.Stop when order.Side==SandboxOrderSide.Sell&&market.Low<=order.StopPrice=>Math.Min(order.StopPrice!.Value,market.Open)-slip,_=>null};}
    private static decimal ReferencePrice(SandboxOrder order,SandboxMarketSlice market)=>order.OrderType switch{SandboxOrderType.Limit=>order.LimitPrice!.Value,SandboxOrderType.Stop=>order.StopPrice!.Value,_=>market.Open};
    internal static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed class SandboxPortfolioProjector
{
    public (SandboxPosition Position,SandboxTrade? Trade) Apply(SandboxPosition? current,SandboxFill fill,InstrumentDefinition instrument)
    {
        current??=new(fill.AccountId,fill.InstrumentId,fill.ContractId,0,0,0,0,fill.FilledAtUtc);var signedFill=fill.Side==SandboxOrderSide.Buy?fill.Quantity:-fill.Quantity;var same=current.SignedQuantity==0||Math.Sign(current.SignedQuantity)==Math.Sign(signedFill);
        if(same){var total=Math.Abs(current.SignedQuantity)+fill.Quantity;var average=total==0?0:(current.AveragePrice*Math.Abs(current.SignedQuantity)+fill.Price*fill.Quantity)/total;return(current with{SignedQuantity=current.SignedQuantity+signedFill,AveragePrice=average,Commissions=current.Commissions+fill.Commission,UpdatedAtUtc=fill.FilledAtUtc},null);}
        var closed=Math.Min(Math.Abs(current.SignedQuantity),fill.Quantity);var gross=(current.SignedQuantity>0?fill.Price-current.AveragePrice:current.AveragePrice-fill.Price)*instrument.PointValue*closed;var remaining=current.SignedQuantity+signedFill;var newAverage=remaining==0?0:Math.Sign(remaining)==Math.Sign(current.SignedQuantity)?current.AveragePrice:fill.Price;var tradeCommission=fill.Commission*(closed/(decimal)fill.Quantity);var trade=new SandboxTrade($"SBT-{SandboxBrokerSimulator.Hash(fill.FillId+current.SignedQuantity)[..32]}",fill.AccountId,fill.InstanceId,fill.InstrumentId,fill.ContractId,closed,current.AveragePrice,fill.Price,gross,tradeCommission,gross-tradeCommission,fill.FilledAtUtc);return(current with{SignedQuantity=remaining,AveragePrice=newAverage,RealizedProfitLoss=current.RealizedProfitLoss+gross,Commissions=current.Commissions+fill.Commission,UpdatedAtUtc=fill.FilledAtUtc},trade);
    }
}

public static class SandboxLedger
{
    public static SandboxLedgerEvent Event(string commandId,string accountId,string? instanceId,long sequence,string eventType,DateTime occurredAtUtc,object payload)
    {var json=JsonSerializer.Serialize(payload,payload.GetType());var hash=SandboxBrokerSimulator.Hash($"{commandId}|{accountId}|{instanceId}|{sequence}|{eventType}|{occurredAtUtc:O}|{json}");return new($"SLE-{hash[..32]}",commandId,accountId,instanceId,sequence,eventType,occurredAtUtc,json,hash);}
}

public sealed class SandboxStateProjector
{
    public SandboxAccountState Project(IReadOnlyList<SandboxLedgerEvent> events)
    {
        var ordered=events.OrderBy(x=>x.Sequence).ToArray();var accountEvent=ordered.FirstOrDefault(x=>x.EventType=="AccountCreated")??throw new InvalidOperationException("Sandbox account creation event is missing.");var account=Read<SandboxAccount>(accountEvent);var instances=new Dictionary<string,SandboxInstance>(StringComparer.Ordinal);var signals=new Dictionary<string,SandboxSignal>(StringComparer.Ordinal);var orders=new Dictionary<string,SandboxOrder>(StringComparer.Ordinal);var fills=new List<SandboxFill>();var positions=new Dictionary<string,SandboxPosition>(StringComparer.Ordinal);var trades=new List<SandboxTrade>();
        foreach(var item in ordered){switch(item.EventType){case "InstanceCreated":case "InstanceUpdated":var instance=Read<SandboxInstance>(item);instances[instance.InstanceId]=instance;break;case "SignalReceived":var signal=Read<SandboxSignal>(item);signals[signal.SignalId]=signal;break;case "OrderSubmitted":case "OrderUpdated":var order=Read<SandboxOrder>(item);orders[order.OrderId]=order;break;case "FillRecorded":fills.Add(Read<SandboxFill>(item));break;case "PositionUpdated":var position=Read<SandboxPosition>(item);positions[Key(position.InstrumentId,position.ContractId)]=position;break;case "TradeClosed":trades.Add(Read<SandboxTrade>(item));break;}}
        decimal cash=account.InitialBalance,peak=cash,maxDrawdown=0;foreach(var item in ordered){if(item.EventType=="FillRecorded")cash-=Read<SandboxFill>(item).Commission;else if(item.EventType=="TradeClosed")cash+=Read<SandboxTrade>(item).GrossProfitLoss;peak=Math.Max(peak,cash);maxDrawdown=Math.Max(maxDrawdown,peak-cash);}var performance=new SandboxPerformance(account.AccountId,account.InitialBalance,cash,trades.Sum(x=>x.GrossProfitLoss),fills.Sum(x=>x.Commission),fills.Count,trades.Count,peak,maxDrawdown,ordered[^1].OccurredAtUtc);
        return new(account,instances,signals,orders,fills,positions,trades,performance,ordered[^1].Sequence);
    }
    private static T Read<T>(SandboxLedgerEvent item)=>JsonSerializer.Deserialize<T>(item.PayloadJson)??throw new InvalidOperationException($"Invalid sandbox ledger payload for {item.EventType}.");internal static string Key(string instrument,string? contract)=>$"{instrument}|{contract}";
}

public static class SandboxStrategyDecisionAdapter
{
    public static SandboxSignal ToSignal(StrategyDecision decision,string instanceId,SandboxOrderSide side,SandboxOrderType type,int quantity,decimal? limitPrice,decimal? stopPrice,string evidenceReference)
    {if(decision.Decision!=StrategyDecisionType.TradeProposal)throw new InvalidOperationException("Only an explicit trade proposal can become a sandbox signal.");return new($"SBS-{SandboxBrokerSimulator.Hash($"{decision.StrategyId}|{decision.StrategyVersion}|{decision.AsOfUtc:O}|{evidenceReference}")[..32]}",instanceId,side,type,quantity,limitPrice,stopPrice,decision.AsOfUtc,decision.Reason,[evidenceReference]);}
}

public static class SandboxCanonicalBarAdapter
{
    public static SandboxMarketSlice From(CanonicalBar bar,int availableQuantity)
    {if(!bar.IsComplete)throw new InvalidOperationException("Only complete canonical bars can enter the sandbox.");return new($"{bar.CanonicalBarId}|R{bar.Revision}",bar.InstrumentId,bar.ContractId,bar.OpenTimeUtc,bar.CloseTimeUtc,bar.RevisionEffectiveUtc,bar.Open,bar.High,bar.Low,bar.Close,availableQuantity,bar.ContentHash);}
}
