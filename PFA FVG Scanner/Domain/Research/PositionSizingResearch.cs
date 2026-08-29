namespace PFA_FVG_Scanner.Domain.Research;

public sealed record PositionSizingResearchRequest(decimal NetRPerContract,decimal RiskDollarsPerContract,
    decimal RoundTurnCommissionPerContract,decimal AccountBalance,decimal MaximumDailyLossDollars,
    decimal MaximumDrawdownDollars,int MinimumContracts=1,int MaximumContracts=5);

public sealed record PositionSizingVariant(int Contracts,decimal GrossProfitLossDollars,decimal CommissionDollars,
    decimal NetProfitLossDollars,decimal CapitalAtRiskDollars,decimal CapitalAtRiskPercent,
    decimal DailyLossLimitUtilization,decimal DrawdownLimitUtilization,bool BreachesDailyLossLimit,
    bool BreachesDrawdownLimit,bool EligibleForSandboxResearch,string Classification);

public sealed record PositionSizingResearchResult(string Version,PositionSizingResearchRequest Request,
    IReadOnlyList<PositionSizingVariant> Variants,int? LargestEligibleQuantity,
    string Interpretation="Quantity scales economics and account risk; it does not change the underlying setup win rate.",
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed class PositionSizingResearchEngine
{
    public const string Version="position-sizing-research-1.0.0";
    public PositionSizingResearchResult Evaluate(PositionSizingResearchRequest request)
    {
        if(request.RiskDollarsPerContract<=0||request.RoundTurnCommissionPerContract<0||request.AccountBalance<=0||
           request.MaximumDailyLossDollars<=0||request.MaximumDrawdownDollars<=0||request.MinimumContracts<1||
           request.MaximumContracts<request.MinimumContracts||request.MaximumContracts>100)
            throw new ArgumentException("Position-sizing research inputs are invalid.");
        var variants=Enumerable.Range(request.MinimumContracts,request.MaximumContracts-request.MinimumContracts+1)
            .Select(quantity=>Variant(request,quantity)).ToArray();
        return new(Version,request,variants,variants.Where(x=>x.EligibleForSandboxResearch).Select(x=>(int?)x.Contracts).LastOrDefault());
    }

    private static PositionSizingVariant Variant(PositionSizingResearchRequest request,int quantity)
    {
        var risk=request.RiskDollarsPerContract*quantity;var gross=request.NetRPerContract*risk;
        var commission=request.RoundTurnCommissionPerContract*quantity;var net=gross-commission;
        var dailyUtil=risk/request.MaximumDailyLossDollars;var drawdownUtil=risk/request.MaximumDrawdownDollars;
        var daily=risk>request.MaximumDailyLossDollars;var drawdown=risk>request.MaximumDrawdownDollars;
        var eligible=!daily&&!drawdown&&risk/request.AccountBalance<=.02m;
        var classification=daily||drawdown?"RuleLimitBreach":risk/request.AccountBalance>.02m?"ExcessiveCapitalRisk":
            net>0?"PositiveScenarioEconomics":net<0?"NegativeScenarioEconomics":"FlatAfterCosts";
        return new(quantity,Round(gross),Round(commission),Round(net),Round(risk),
            Round(100*risk/request.AccountBalance),Round(dailyUtil),Round(drawdownUtil),daily,drawdown,eligible,classification);
    }
    private static decimal Round(decimal value)=>decimal.Round(value,6,MidpointRounding.AwayFromZero);
}
