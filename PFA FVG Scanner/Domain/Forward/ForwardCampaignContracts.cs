using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Forward;

public enum ForwardCampaignStatus { Created,Running,Suspended,Stopped,Completed }
public enum ForwardComparisonStatus { Accumulating,Stable,Degraded,OperationallyInvalid }
public enum ForwardSuspensionReason { None,FeedCoverage,FeedHealth,ExpectancyDegradation,WinRateDegradation,DrawdownDegradation }

public sealed record ForwardExpectation(
    string ExpectationId,string SourceReportId,string SourceContentHash,string StrategyId,string StrategyVersion,
    decimal HistoricalExpectancyR,decimal HistoricalWinRate,decimal HistoricalMaximumDrawdownR,
    decimal FixedRiskDollars,int MinimumForwardTrades,int HealthSampleIntervalSeconds,decimal MinimumExpectancyRetentionPercent,
    decimal MaximumWinRateDeclinePoints,decimal MaximumDrawdownMultiple,decimal MinimumOperationalCoveragePercent,
    string ExpectationVersion,DateTime FrozenAtUtc)
{
    public string ContentHash()=>ForwardHash.Of(JsonSerializer.Serialize(this));
    public void Validate(){if(string.IsNullOrWhiteSpace(SourceReportId)||string.IsNullOrWhiteSpace(SourceContentHash)||FixedRiskDollars<=0||MinimumForwardTrades<1||HealthSampleIntervalSeconds<1||MinimumExpectancyRetentionPercent<0||MaximumWinRateDeclinePoints<0||MaximumDrawdownMultiple<=0||MinimumOperationalCoveragePercent is <0 or >100)throw new ArgumentException("Forward expectation is invalid.");}
}

public sealed record ForwardCampaign(
    string CampaignId,string AccountId,string InstanceId,string StrategyId,string StrategyVersion,
    ForwardExpectation Expectation,ForwardCampaignStatus Status,DateTime CreatedAtUtc,DateTime? StartedAtUtc,
    DateTime? StoppedAtUtc,string StatusReason,string CreatedBy,bool CanPromoteStrategy=false);

public sealed record ForwardCampaignEvent(
    string CampaignEventId,string CampaignId,string EventType,ForwardCampaignStatus Status,DateTime OccurredAtUtc,
    string Actor,string Reason,string ContentHash);

public sealed record ForwardHealthSample(
    string SampleId,string CampaignId,DateTime SampledAtUtc,bool FeedHealthy,bool FeedStale,
    DateTime? LastMarketEventUtc,DateTime? LastHealthCheckUtc,DateTime? LastReconnectAttemptUtc,
    string Message,string ContentHash);

public sealed record ForwardDailySnapshot(
    string SnapshotId,string CampaignId,DateOnly TradingDate,DateTime WindowStartUtc,DateTime WindowEndUtc,
    DateTime KnownAtUtc,int Trades,int Wins,int Losses,decimal GrossProfitLoss,decimal Commissions,
    decimal NetProfitLoss,decimal ExpectancyR,decimal WinRate,decimal MaximumDrawdownR,int HealthSamples,
    int HealthySamples,int ReconnectSamples,decimal OperationalCoveragePercent,bool SessionClosed,
    string SandboxLedgerThroughReference,string ContentHash,bool CanPromoteStrategy=false);

public sealed record ForwardComparison(
    string ComparisonId,string CampaignId,string ExpectationId,DateTime ComparedAtUtc,ForwardComparisonStatus Status,
    ForwardSuspensionReason SuspensionReason,int ForwardTrades,decimal ForwardExpectancyR,
    decimal ExpectancyRetentionPercent,decimal ForwardWinRate,decimal WinRateChangePoints,
    decimal ForwardMaximumDrawdownR,decimal OperationalCoveragePercent,string Summary,string ContentHash,
    bool SuspendedAutomatically,bool CanPromoteStrategy=false);

public sealed class ForwardExpectationComparator
{
    public const string Version="1.0.0";
    public ForwardComparison Compare(ForwardCampaign campaign,IReadOnlyList<ForwardDailySnapshot> snapshots,DateTime comparedAtUtc)
    {
        campaign.Expectation.Validate();var ordered=snapshots.OrderBy(x=>x.TradingDate).ToArray();if(ordered.Any(x=>x.KnownAtUtc>comparedAtUtc))throw new InvalidOperationException("Forward comparison cannot use future-known snapshots.");
        var trades=ordered.Sum(x=>x.Trades);var wins=ordered.Sum(x=>x.Wins);var pnl=ordered.Sum(x=>x.NetProfitLoss);var expectancy=trades==0?0:pnl/trades/campaign.Expectation.FixedRiskDollars;var winRate=trades==0?0:100m*wins/trades;var retention=campaign.Expectation.HistoricalExpectancyR==0?0:100m*expectancy/campaign.Expectation.HistoricalExpectancyR;var winChange=winRate-campaign.Expectation.HistoricalWinRate;var maxDrawdown=ordered.Length==0?0:ordered.Max(x=>x.MaximumDrawdownR);var coverage=ordered.Length==0?0:ordered.Average(x=>x.OperationalCoveragePercent);
        var status=ForwardComparisonStatus.Stable;var reason=ForwardSuspensionReason.None;string summary;
        if(ordered.Any(x=>!x.SessionClosed)||coverage<campaign.Expectation.MinimumOperationalCoveragePercent){status=ForwardComparisonStatus.OperationallyInvalid;reason=ForwardSuspensionReason.FeedCoverage;summary="Operational coverage is insufficient; strategy performance is not classified.";}
        else if(trades<campaign.Expectation.MinimumForwardTrades){status=ForwardComparisonStatus.Accumulating;summary="Forward evidence has not reached the frozen minimum trade count.";}
        else if(retention<campaign.Expectation.MinimumExpectancyRetentionPercent){status=ForwardComparisonStatus.Degraded;reason=ForwardSuspensionReason.ExpectancyDegradation;summary="Forward expectancy retention is below the frozen threshold.";}
        else if(winChange < -campaign.Expectation.MaximumWinRateDeclinePoints){status=ForwardComparisonStatus.Degraded;reason=ForwardSuspensionReason.WinRateDegradation;summary="Forward win rate decline exceeds the frozen threshold.";}
        else if(maxDrawdown>campaign.Expectation.HistoricalMaximumDrawdownR*campaign.Expectation.MaximumDrawdownMultiple){status=ForwardComparisonStatus.Degraded;reason=ForwardSuspensionReason.DrawdownDegradation;summary="Forward drawdown exceeds the frozen historical multiple.";}
        else summary="Forward evidence remains within frozen expectations.";
        var automatic=status is ForwardComparisonStatus.Degraded or ForwardComparisonStatus.OperationallyInvalid;var identity=JsonSerializer.Serialize(new{campaign.CampaignId,campaign.Expectation.ExpectationId,comparedAtUtc,status,reason,trades,expectancy,retention,winRate,winChange,maxDrawdown,coverage,summary,automatic,CanPromoteStrategy=false});var hash=ForwardHash.Of(identity);return new($"FWC-{hash[..32]}",campaign.CampaignId,campaign.Expectation.ExpectationId,comparedAtUtc,status,reason,trades,expectancy,retention,winRate,winChange,maxDrawdown,coverage,summary,hash,automatic,false);
    }
}

internal static class ForwardHash{internal static string Of(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));}
