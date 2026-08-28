using System.Text.Json;
using PFA_FVG_Scanner.Domain.Certification;

namespace PFA_FVG_Scanner.Services;

public sealed class CertificationCampaignEngine(PropFirmCertificationEngine rules)
{
    public const string Version="1.0.0";
    public CertificationCampaignResult Evaluate(CertificationCampaignRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.CampaignId)||string.IsNullOrWhiteSpace(request.StrategyId)||string.IsNullOrWhiteSpace(request.StrategyVersion)||string.IsNullOrWhiteSpace(request.EvidenceRevision))throw new ArgumentException("Campaign, strategy version, and evidence revision are required.");
        if(request.RulePacks.Count==0)throw new ArgumentException("At least one frozen rule pack is required.");
        if(request.RulePacks.Any(x=>!x.IsOfficiallyVerified))throw new InvalidOperationException("Unverified rule-pack snapshots cannot enter certification.");
        var hashes=request.RulePacks.Select(x=>x.ContentHash()).ToArray();if(hashes.Distinct(StringComparer.Ordinal).Count()!=hashes.Length)throw new ArgumentException("Duplicate rule-pack snapshots are not allowed.");
        var results=request.RulePacks.OrderBy(x=>x.FirmId,StringComparer.Ordinal).ThenBy(x=>x.ProgramId,StringComparer.Ordinal).ThenBy(x=>x.RuleVersion,StringComparer.Ordinal).Select(pack=>rules.Evaluate($"{request.CampaignId}:{pack.FirmId}:{pack.ProgramId}",pack,request.TradingDays,request.CreatedAtUtc)).ToArray();
        var identity=JsonSerializer.Serialize(new{request.CampaignId,request.StrategyId,request.StrategyVersion,request.EvidenceRevision,RulePackHashes=hashes.Order(),ResultHashes=results.Select(x=>x.ContentHash).Order(),request.CreatedAtUtc,Version});
        return new(request.CampaignId,request.StrategyId,request.StrategyVersion,request.EvidenceRevision,results,request.CreatedAtUtc,CertificationHash.Of(identity),false,false);
    }
}
