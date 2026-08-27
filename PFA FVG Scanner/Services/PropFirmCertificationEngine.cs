using System.Text.Json;
using PFA_FVG_Scanner.Domain.Certification;

namespace PFA_FVG_Scanner.Services;

public sealed class PropFirmCertificationEngine
{
    public const string Version="1.0.0";
    public PropAccountCertificationResult Evaluate(string accountId,PropFirmRulePack pack,IReadOnlyList<PropTradingDayResult> days,DateTime evaluatedAtUtc)
    {
        pack.Validate();if(evaluatedAtUtc<pack.EffectiveFromUtc||pack.EffectiveToUtc.HasValue&&evaluatedAtUtc>pack.EffectiveToUtc.Value)throw new InvalidOperationException("Rule pack is not effective at evaluation time.");
        var ordered=days.OrderBy(x=>x.TradingDate).ToArray();if(ordered.Select(x=>x.TradingDate).Distinct().Count()!=ordered.Length)throw new InvalidOperationException("Only one closed result is allowed per trading date.");if(ordered.Any(x=>x.KnownAtUtc>evaluatedAtUtc))throw new InvalidOperationException("Future-known trading days cannot enter certification.");
        var violations=new List<PropRuleViolation>();var balance=pack.StartingBalance;var high=balance;var floor=pack.StartingBalance-pack.MaximumLoss;decimal bestDay=0;
        foreach(var day in ordered)
        {
            if(day.StartBalance!=balance)violations.Add(new(PropRuleViolationCode.OperationalData,day.TradingDate,$"Expected start balance {balance}; observed {day.StartBalance}.",true));
            if(!day.OperationalDataComplete)violations.Add(new(PropRuleViolationCode.OperationalData,day.TradingDate,"Operational evidence is incomplete.",true));
            var net=day.GrossProfitLoss-day.Commissions;if(pack.DailyLossLimit.HasValue&&net<=-pack.DailyLossLimit.Value)violations.Add(new(PropRuleViolationCode.DailyLoss,day.TradingDate,$"Net daily loss {net} reached limit {-pack.DailyLossLimit.Value}.",true));
            if(day.MaximumSimultaneousContracts>pack.MaximumContracts)violations.Add(new(PropRuleViolationCode.MaximumContracts,day.TradingDate,$"Observed {day.MaximumSimultaneousContracts}; allowed {pack.MaximumContracts}.",true));
            if(day.EnteredDuringRestrictedNews&&!pack.NewsEntriesAllowed)violations.Add(new(PropRuleViolationCode.NewsRestriction,day.TradingDate,"A new position was initiated during a restricted news window.",true));
            if(day.HeldPositionAtRequiredFlatten&&pack.FlattenAtSessionEnd)violations.Add(new(PropRuleViolationCode.SessionFlatten,day.TradingDate,"A position remained open at the required session flatten time.",true));
            if(!ExecutionAllowed(day.ExecutionMode,pack.AutomationMode))violations.Add(new(PropRuleViolationCode.AutomationProhibited,day.TradingDate,$"Execution mode {day.ExecutionMode} exceeds allowed mode {pack.AutomationMode}.",true));
            var prospectiveHigh=pack.DrawdownMode==PropDrawdownMode.IntradayTrailing?Math.Max(high,day.IntradayHighEquity):high;var prospectiveFloor=pack.DrawdownMode switch{PropDrawdownMode.Static=>pack.StartingBalance-pack.MaximumLoss,PropDrawdownMode.EndOfDayTrailing=>Math.Max(pack.StartingBalance-pack.MaximumLoss,Math.Max(high,day.EndBalance)-pack.MaximumLoss),_=>Math.Max(pack.StartingBalance-pack.MaximumLoss,prospectiveHigh-pack.MaximumLoss)};
            if(day.IntradayLowEquity<=prospectiveFloor)violations.Add(new(pack.DrawdownMode==PropDrawdownMode.Static?PropRuleViolationCode.MaximumLoss:PropRuleViolationCode.TrailingDrawdown,day.TradingDate,$"Intraday equity {day.IntradayLowEquity} reached drawdown floor {prospectiveFloor}.",true));
            balance=day.EndBalance;high=pack.DrawdownMode==PropDrawdownMode.EndOfDayTrailing?Math.Max(high,day.EndBalance):prospectiveHigh;floor=prospectiveFloor;bestDay=Math.Max(bestDay,net);
        }
        var netProfit=balance-pack.StartingBalance;var profitable=ordered.Count(x=>x.GrossProfitLoss-x.Commissions>=pack.MinimumProfitableDayDollars);var bestPercent=netProfit>0?100m*bestDay/netProfit:0;var consistencyPass=netProfit>0&&bestPercent<=pack.MaximumBestDayProfitPercent;if(netProfit>=pack.ProfitTarget&&!consistencyPass&&ordered.Length>0)violations.Add(new(PropRuleViolationCode.Consistency,ordered[^1].TradingDate,$"Best day is {bestPercent:F2}% of net profit; maximum is {pack.MaximumBestDayProfitPercent}%.",false));
        var terminal=violations.Any(x=>x.Terminal);var passed=!terminal&&netProfit>=pack.ProfitTarget&&ordered.Length>=pack.MinimumTradingDays&&profitable>=pack.MinimumProfitableDays&&consistencyPass;var payout=passed&&ordered.Length>=pack.MinimumDaysBeforePayout&&balance>=pack.PayoutMinimumBalance+pack.PayoutSafetyBuffer;var status=terminal?(violations.Any(x=>x.Code==PropRuleViolationCode.OperationalData)?PropAccountCertificationStatus.OperationallyInvalid:PropAccountCertificationStatus.Failed):payout?PropAccountCertificationStatus.PayoutEligible:passed?PropAccountCertificationStatus.PassedChallenge:PropAccountCertificationStatus.Active;
        var identity=JsonSerializer.Serialize(new{accountId,PackHash=pack.ContentHash(),Days=ordered.Select(x=>new{x.TradingDate,x.SourceReference,x.KnownAtUtc}),status,balance,netProfit,high,floor,profitable,bestDay,bestPercent,violations,Version,evaluatedAtUtc});var hash=CertificationHash.Of(identity);return new($"PCR-{hash[..32]}",accountId,pack.ContentHash(),status,balance,netProfit,high,floor,ordered.Length,profitable,bestDay,bestPercent,violations,passed,payout,evaluatedAtUtc,hash,false);
    }
    private static bool ExecutionAllowed(PropAutomationMode used,PropAutomationMode allowed)=>allowed switch{PropAutomationMode.AutomatedExecutionPermitted=>used!=PropAutomationMode.Unsupported,PropAutomationMode.ManualOriginWithCopying=>used is PropAutomationMode.ResearchOnly or PropAutomationMode.AlertOnly or PropAutomationMode.ManualApprovalRequired or PropAutomationMode.ManualOriginWithCopying,PropAutomationMode.ManualApprovalRequired=>used is PropAutomationMode.ResearchOnly or PropAutomationMode.AlertOnly or PropAutomationMode.ManualApprovalRequired,PropAutomationMode.AlertOnly=>used is PropAutomationMode.ResearchOnly or PropAutomationMode.AlertOnly,PropAutomationMode.ResearchOnly=>used==PropAutomationMode.ResearchOnly,_=>false};
}
