namespace PFA_FVG_Scanner.Domain.Context;

public enum ResearchContextMaturity { Active,Foundation,Planned,ExternalDataRequired }
public enum ResearchContextSourceKind { CanonicalBars,TradesAndQuotes,MarketDepth,Calendar,RelatedMarkets,ExternalDataset }

public sealed record ResearchContextFamily(
    string FamilyId,string Name,string Version,string Description,ResearchContextMaturity Maturity,
    IReadOnlyList<ResearchContextSourceKind> RequiredSources,IReadOnlyList<string> FeatureExamples,
    string PointInTimeRule,bool AgentFeatureEligible,bool MissingDataMustRemainNull=true);

public sealed record ResearchContextCatalog(
    string Version,int Families,int Active,int Foundation,int Planned,int ExternalDataRequired,
    IReadOnlyList<ResearchContextFamily> Items,
    string Interpretation="Context qualifies or rejects an opportunity; it is not independently a trade signal.");

public interface IResearchContextFamilyRegistry
{
    ResearchContextCatalog GetCatalog();
    ResearchContextFamily? Find(string familyId);
}

public sealed class ResearchContextFamilyRegistry : IResearchContextFamilyRegistry
{
    public const string Version="research-context-catalog-1.0.0";
    private static readonly IReadOnlyList<ResearchContextFamily> Families =
    [
        Active("order-flow","Order Flow","Executed buying/selling pressure and auction response",[ResearchContextSourceKind.TradesAndQuotes],["aggressor delta","cumulative delta","absorption","exhaustion","volume at price","point of control"]),
        Required("level-two","Level II / Market Depth","Resting-liquidity state and queue behavior",[ResearchContextSourceKind.MarketDepth],["multi-level imbalance","stacking","pulling","replenishment","queue depletion","liquidity migration"]),
        Foundation("seasonality","Seasonality","Recurring behavior by clock, calendar and contract cycle",[ResearchContextSourceKind.CanonicalBars,ResearchContextSourceKind.Calendar],["minute of session","weekday","month","holiday window","expiration proximity","rollover proximity"]),
        Foundation("volatility-regime","Volatility Regime","Current volatility state relative to its point-in-time history",[ResearchContextSourceKind.CanonicalBars],["realized volatility","ATR percentile","compression","expansion","volatility transition"]),
        Foundation("volume-regime","Volume Regime","Participation relative to comparable historical windows",[ResearchContextSourceKind.CanonicalBars],["relative volume","volume percentile","volume acceleration","dry-up","climax volume"]),
        Foundation("trend-balance-regime","Trend vs Balance Regime","Directional auction, rotation or transition state",[ResearchContextSourceKind.CanonicalBars],["trend efficiency","range overlap","slope","rotation count","balance width"]),
        Foundation("session-structure","Session Structure","Position within exchange session and prior-session references",[ResearchContextSourceKind.CanonicalBars,ResearchContextSourceKind.Calendar],["session segment","overnight high/low","prior close","opening range","initial balance"]),
        Foundation("multi-timeframe","Multi-timeframe Alignment","Agreement and conflict across causal completed timeframes",[ResearchContextSourceKind.CanonicalBars],["higher-timeframe direction","lower-timeframe trigger","timeframe agreement","distance to HTF level"]),
        Foundation("cross-market","Cross-market Confirmation","Confirmation, divergence and lead/lag among related markets",[ResearchContextSourceKind.RelatedMarkets],["rolling correlation","relative strength","lead-lag return","divergence","risk-on/off agreement"]),
        Required("economic-events","Economic-event Proximity","Scheduled releases and event-risk windows",[ResearchContextSourceKind.ExternalDataset,ResearchContextSourceKind.Calendar],["minutes to release","event class","surprise magnitude","post-release phase"]),
        Required("options-positioning","Options Positioning","Options-implied volatility and positioning context",[ResearchContextSourceKind.ExternalDataset],["implied volatility","skew","open interest","gamma exposure","dealer positioning proxy"]),
        Foundation("auction-profile","Auction Market / Volume Profile","Price acceptance and rejection within the auction",[ResearchContextSourceKind.TradesAndQuotes],["value area","point of control","high/low-volume nodes","profile shape","value migration"]),
        Foundation("liquidity-spread","Liquidity and Spread","Immediate tradability and execution-friction state",[ResearchContextSourceKind.TradesAndQuotes],["spread","quoted size","trade size","estimated slippage","participation rate"]),
        Foundation("contract-cycle","Contract Cycle","Expiration, rollover and dated-contract effects",[ResearchContextSourceKind.CanonicalBars,ResearchContextSourceKind.Calendar],["days to expiration","roll window","front/back volume ratio","contract age"]),
        Foundation("momentum-exhaustion","Momentum and Exhaustion","Impulse strength, acceleration, deceleration and failure",[ResearchContextSourceKind.CanonicalBars],["return velocity","acceleration","consecutive closes","wick rejection","momentum divergence"]),
        Foundation("correlation-regime","Correlation Regime","Changes in normally related market relationships",[ResearchContextSourceKind.RelatedMarkets],["rolling beta","correlation percentile","correlation break","dispersion"]),
        Foundation("opening-inventory","Opening Type and Overnight Inventory","Opening auction behavior and overnight positioning",[ResearchContextSourceKind.CanonicalBars],["gap","overnight inventory direction","open-drive","open-test-drive","gap acceptance"]),
        Required("market-breadth","Market Breadth","Participation across index constituents or related instruments",[ResearchContextSourceKind.ExternalDataset],["advance/decline","up/down volume","new highs/lows","percentage above VWAP"]),
        Foundation("setup-interaction","Setup Interaction","Patterns and sequences occurring before or concurrently",[ResearchContextSourceKind.CanonicalBars],["prior event types","concurrent events","sequence stage","time since event","event density"]),
        Foundation("position-sizing","Position Sizing and Scale-out","Quantity-specific economics and account survivability",[ResearchContextSourceKind.CanonicalBars],["contracts","risk dollars","margin use","scale-out plan","drawdown utilization","risk of ruin"])
    ];

    public ResearchContextCatalog GetCatalog()=>new(Version,Families.Count,
        Families.Count(x=>x.Maturity==ResearchContextMaturity.Active),
        Families.Count(x=>x.Maturity==ResearchContextMaturity.Foundation),
        Families.Count(x=>x.Maturity==ResearchContextMaturity.Planned),
        Families.Count(x=>x.Maturity==ResearchContextMaturity.ExternalDataRequired),Families);

    public ResearchContextFamily? Find(string familyId)=>Families.FirstOrDefault(x=>x.FamilyId.Equals(familyId,StringComparison.OrdinalIgnoreCase));

    private static ResearchContextFamily Active(string id,string name,string description,ResearchContextSourceKind[] sources,string[] features)=>
        new(id,name,"1.0.0",description,ResearchContextMaturity.Active,sources,features,"Use only source events whose KnownAtUtc is no later than the decision clock.",true);
    private static ResearchContextFamily Foundation(string id,string name,string description,ResearchContextSourceKind[] sources,string[] features)=>
        new(id,name,"foundation-1.0.0",description,ResearchContextMaturity.Foundation,sources,features,"Calculate from information available at the decision clock; historical baselines must end before that clock.",false);
    private static ResearchContextFamily Required(string id,string name,string description,ResearchContextSourceKind[] sources,string[] features)=>
        new(id,name,"data-source-required",description,ResearchContextMaturity.ExternalDataRequired,sources,features,"Remain unavailable until a timestamped, revisioned source is connected; never substitute zero for missing data.",false);
}
