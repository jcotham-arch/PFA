using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.Certification;

public sealed record CertificationCampaignRequest(
    string CampaignId,string StrategyId,string StrategyVersion,string EvidenceRevision,
    IReadOnlyList<PropFirmRulePack> RulePacks,IReadOnlyList<PropTradingDayResult> TradingDays,
    DateTime CreatedAtUtc);

public sealed record CertificationCampaignResult(
    string CampaignId,string StrategyId,string StrategyVersion,string EvidenceRevision,
    IReadOnlyList<PropAccountCertificationResult> Results,DateTime CreatedAtUtc,string ContentHash,
    bool CanPromoteStrategy=false,bool CanRouteToRealBroker=false);
