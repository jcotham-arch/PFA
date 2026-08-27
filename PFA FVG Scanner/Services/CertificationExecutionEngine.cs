using System.Text.Json;
using PFA_FVG_Scanner.Domain.Certification;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sandbox;

namespace PFA_FVG_Scanner.Services;

public sealed class CertificationExecutionEngine
{
    public const string Version="1.0.0";
    public CertificationOrderResult Execute(CertificationOrderRequest order,IReadOnlyList<CertificationMarketEvent> events,ExecutionRealismProfile profile,InstrumentDefinition instrument,DateTime asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(order);ArgumentNullException.ThrowIfNull(events);profile.Validate();
        if(order.Quantity<1)throw new ArgumentOutOfRangeException(nameof(order.Quantity));
        if(order.Type==SandboxOrderType.Limit&&order.LimitPrice is null||order.Type==SandboxOrderType.Stop&&order.StopPrice is null)throw new ArgumentException("The order type requires its trigger price.");
        if(order.SubmittedAtUtc>asOfUtc)throw new InvalidOperationException("Future-known orders cannot enter certification.");
        var jitter=profile.JitterMilliseconds==0?0:(int)(Draw(profile,$"{order.ClientOrderId}|latency")*(profile.JitterMilliseconds*2+1))-profile.JitterMilliseconds;
        var eligible=order.SubmittedAtUtc.AddMilliseconds(Math.Max(0,profile.BaseLatencyMilliseconds+jitter));var fills=new List<CertificationFill>();var reasons=new List<string>();var remaining=order.Quantity;var triggered=order.Type!=SandboxOrderType.Stop;var ordered=events.OrderBy(x=>x.Sequence).ThenBy(x=>x.KnownAtUtc).ToArray();
        if(ordered.Select(x=>x.Sequence).Distinct().Count()!=ordered.Length)throw new InvalidOperationException("Market event sequence values must be unique.");
        foreach(var market in ordered)
        {
            if(remaining==0)break;if(market.KnownAtUtc>asOfUtc||market.EventTimeUtc>market.KnownAtUtc)throw new InvalidOperationException("Future or out-of-order market knowledge detected.");
            if(market.InstrumentId!=order.InstrumentId||market.ContractId!=order.ContractId)continue;if(market.KnownAtUtc<eligible)continue;
            if(!market.VenueAvailable)return Result(CertificationOrderState.Rejected,CertificationRejectReason.VenueUnavailable,"Venue unavailable at first eligible event.");
            if((market.KnownAtUtc-market.EventTimeUtc).TotalMilliseconds>profile.MaximumMarketAgeMilliseconds)return Result(CertificationOrderState.Rejected,CertificationRejectReason.StaleMarket,"Market event exceeded frozen staleness limit.");
            if(order.Type==SandboxOrderType.Stop&&!triggered){triggered=order.Side==SandboxOrderSide.Buy?market.Last>=order.StopPrice:market.Last<=order.StopPrice;if(!triggered)continue;reasons.Add($"Stop triggered by {market.EventId}.");}
            var reference=Reference(order,market,triggered);if(reference is null)continue;
            var marketable=order.Type!=SandboxOrderType.Limit||(order.Side==SandboxOrderSide.Buy?market.Last<=order.LimitPrice:market.Last>=order.LimitPrice);
            if(order.Type==SandboxOrderType.Limit&&!marketable)continue;
            if(order.Type==SandboxOrderType.Limit&&market.Last==order.LimitPrice&&Draw(profile,$"{order.ClientOrderId}|{market.EventId}|touch")>profile.TouchFillProbability){reasons.Add($"Touch at {market.EventId} did not clear simulated queue.");continue;}
            var visible=order.Side==SandboxOrderSide.Buy?market.AskSize:market.BidSize;visible=Math.Max(0,visible-(int)Math.Ceiling(profile.QueueAheadContracts));var traded=Math.Max(visible,market.LastSize);var capacity=(int)Math.Floor(traded*profile.MaximumParticipationRate);if(capacity<=0){reasons.Add($"No conservative participation capacity at {market.EventId}.");continue;}
            var quantity=Math.Min(remaining,capacity);var slipTicks=profile.BaseSlippageTicks+market.ShortHorizonVolatilityTicks*profile.VolatilitySlippageFactor+Math.Max(0,quantity-1)*profile.QuantityImpactTicks;
            var raw=order.Side==SandboxOrderSide.Buy?reference.Value+slipTicks*instrument.TickSize:reference.Value-slipTicks*instrument.TickSize;
            if(order.Type==SandboxOrderType.Limit)raw=order.Side==SandboxOrderSide.Buy?Math.Min(raw,order.LimitPrice!.Value):Math.Max(raw,order.LimitPrice!.Value);
            var price=RoundAdverse(raw,instrument.TickSize,order.Side);var actualSlip=Math.Abs(price-reference.Value)/instrument.TickSize;var hash=profile.ContentHash();var fillHash=CertificationHash.Of($"{order.ClientOrderId}|{market.EventId}|{fills.Count}|{quantity}|{price}|{hash}");
            fills.Add(new($"CFI-{fillHash[..32]}",order.ClientOrderId,quantity,price,quantity*profile.CommissionPerContract,actualSlip,market.KnownAtUtc,market.EventId,market.DataRevision,hash));remaining-=quantity;reasons.Add($"Filled {quantity} at {price} from {market.EventId}; participation and queue limits applied.");
        }
        if(fills.Count==0)return Result(triggered?CertificationOrderState.Working:CertificationOrderState.Accepted,CertificationRejectReason.None,"No eligible conservative fill.");
        return Result(remaining==0?CertificationOrderState.Filled:CertificationOrderState.PartiallyFilled,CertificationRejectReason.None,remaining==0?"Order completely filled.":"Order remains partially filled.");
        CertificationOrderResult Result(CertificationOrderState state,CertificationRejectReason reject,string reason){var audit=reasons.Append(reason).ToArray();return new(order.ClientOrderId,state,reject,order.Quantity-remaining,remaining,eligible,fills,audit,profile.ContentHash(),false);}
    }
    private static decimal? Reference(CertificationOrderRequest order,CertificationMarketEvent market,bool triggered)=>order.Type switch{SandboxOrderType.Market=>order.Side==SandboxOrderSide.Buy?market.Ask:market.Bid,SandboxOrderType.Stop when triggered=>order.Side==SandboxOrderSide.Buy?market.Ask:market.Bid,SandboxOrderType.Limit=>profileLimit(order,market),_=>null};
    private static decimal? profileLimit(CertificationOrderRequest order,CertificationMarketEvent market)=>order.Side==SandboxOrderSide.Buy?market.Ask.HasValue?Math.Min(market.Ask.Value,order.LimitPrice!.Value):order.LimitPrice:market.Bid.HasValue?Math.Max(market.Bid.Value,order.LimitPrice!.Value):order.LimitPrice;
    private static decimal RoundAdverse(decimal value,decimal tick,SandboxOrderSide side){var ticks=value/tick;return(side==SandboxOrderSide.Buy?decimal.Ceiling(ticks):decimal.Floor(ticks))*tick;}
    private static decimal Draw(ExecutionRealismProfile profile,string key){var hash=CertificationHash.Of($"{profile.Seed}|{key}");return Convert.ToUInt32(hash[..8],16)/(decimal)uint.MaxValue;}
}

public sealed class CertificationReconciliationEngine
{
    public const string Version="1.0.0";
    public ReconciliationReport Reconcile(InternalAccountSnapshot local,VenueAccountSnapshot venue,DateTime nowUtc)
    {
        if(local.AccountId!=venue.AccountId)throw new ArgumentException("Account identities do not match.");if(local.AsOfUtc>nowUtc||venue.KnownAtUtc>nowUtc)throw new InvalidOperationException("Future-known account state cannot be reconciled.");var breaks=new List<ReconciliationBreak>();
        foreach(var key in local.SignedPositions.Keys.Union(venue.SignedPositions.Keys).Order(StringComparer.Ordinal)){local.SignedPositions.TryGetValue(key,out var expected);venue.SignedPositions.TryGetValue(key,out var actual);if(expected!=actual)breaks.Add(new("Position",key,expected.ToString(),actual.ToString(),ReconciliationSeverity.Critical));}
        foreach(var id in local.WorkingClientOrderIds.Except(venue.WorkingClientOrderIds).Order(StringComparer.Ordinal))breaks.Add(new("MissingVenueOrder",id,"working","absent",ReconciliationSeverity.Critical));
        foreach(var id in venue.WorkingClientOrderIds.Except(local.WorkingClientOrderIds).Order(StringComparer.Ordinal))breaks.Add(new("UnknownVenueOrder",id,"absent","working",ReconciliationSeverity.Critical));
        if(local.CashBalance!=venue.CashBalance)breaks.Add(new("Cash","USD",local.CashBalance.ToString(),venue.CashBalance.ToString(),ReconciliationSeverity.Warning));
        var hash=CertificationHash.Of(JsonSerializer.Serialize(new{InternalSnapshotId=local.SnapshotId,VenueSnapshotId=venue.SnapshotId,nowUtc,Breaks=breaks,Version}));return new($"REC-{hash[..32]}",local.AccountId,nowUtc,breaks,breaks.Any(x=>x.Severity==ReconciliationSeverity.Critical),hash,false);
    }
}
