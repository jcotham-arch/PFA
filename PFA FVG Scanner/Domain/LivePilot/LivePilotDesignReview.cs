using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PFA_FVG_Scanner.Domain.LivePilot;

public enum LivePilotDecisionStatus { Proposed,Approved,Rejected }
public enum LivePilotReadinessStatus { DesignReviewRequired,EvidenceRequired,ReadyForInfrastructureBuild }

public sealed record LivePilotDesignDecision(
    string Topic,string DecisionVersion,LivePilotDecisionStatus Status,string DecisionJson,
    string DecidedBy,DateTime? DecidedAtUtc,string EvidenceReference)
{
    public string ContentHash()=>Hash(JsonSerializer.Serialize(this));
    internal static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record LivePilotEvidenceSnapshot(
    string StrategyId,string StrategyVersion,string WalkForwardReportId,string WalkForwardContentHash,
    bool WalkForwardStable,string ForwardCampaignId,string ForwardComparisonId,string ForwardComparisonContentHash,
    bool ForwardStable,int ForwardTrades,DateTime KnownAtUtc);

public sealed record LivePilotDesignReview(
    string ReviewId,string ReviewVersion,IReadOnlyList<LivePilotDesignDecision> Decisions,
    LivePilotEvidenceSnapshot? Evidence,DateTime ReviewedAtUtc);

public sealed record LivePilotReadinessResult(
    string ReviewId,LivePilotReadinessStatus Status,IReadOnlyList<string> MissingOrRejectedTopics,
    IReadOnlyList<string> EvidenceFailures,string ReviewContentHash,bool CanBuildInfrastructure,
    bool CanRouteToRealBroker=false,bool CanActivateStrategy=false);

public sealed class LivePilotReadinessAuditor
{
    public const string Version="1.0.0";
    public static readonly IReadOnlyList<string> RequiredTopics=
    [
        "execution-provider-and-certification","credential-custody-and-rotation","separate-operational-authentication",
        "pilot-account-and-capital-boundary","instrument-and-session-allowlist","order-types-and-time-in-force",
        "duplicate-order-idempotency","reconnect-and-reconciliation","partial-rejected-fill-policy",
        "independent-kill-switch-ownership","incident-response-and-rollback"
    ];

    public LivePilotReadinessResult Evaluate(LivePilotDesignReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        if(review.ReviewedAtUtc.Kind!=DateTimeKind.Utc)throw new ArgumentException("Review time must be UTC.");
        var duplicates=review.Decisions.GroupBy(x=>x.Topic,StringComparer.Ordinal).Where(x=>x.Count()>1).Select(x=>x.Key).ToArray();
        if(duplicates.Length>0)throw new InvalidOperationException("A design review must contain one decision version per required topic.");
        var decisions=review.Decisions.ToDictionary(x=>x.Topic,StringComparer.Ordinal);
        var missing=RequiredTopics.Where(topic=>!decisions.TryGetValue(topic,out var decision)||decision.Status!=LivePilotDecisionStatus.Approved||string.IsNullOrWhiteSpace(decision.DecidedBy)||!decision.DecidedAtUtc.HasValue||decision.DecidedAtUtc>review.ReviewedAtUtc||string.IsNullOrWhiteSpace(decision.EvidenceReference)).ToArray();
        var evidenceFailures=new List<string>();
        if(review.Evidence is null)evidenceFailures.Add("Approved walk-forward and forward-sandbox evidence is missing.");
        else
        {
            if(review.Evidence.KnownAtUtc>review.ReviewedAtUtc)evidenceFailures.Add("Evidence was not known when the review occurred.");
            if(!review.Evidence.WalkForwardStable||string.IsNullOrWhiteSpace(review.Evidence.WalkForwardReportId)||string.IsNullOrWhiteSpace(review.Evidence.WalkForwardContentHash))evidenceFailures.Add("Stable immutable walk-forward evidence is missing.");
            if(!review.Evidence.ForwardStable||review.Evidence.ForwardTrades<1||string.IsNullOrWhiteSpace(review.Evidence.ForwardCampaignId)||string.IsNullOrWhiteSpace(review.Evidence.ForwardComparisonId)||string.IsNullOrWhiteSpace(review.Evidence.ForwardComparisonContentHash))evidenceFailures.Add("Stable immutable forward-sandbox evidence is missing.");
        }
        var status=missing.Length>0?LivePilotReadinessStatus.DesignReviewRequired:evidenceFailures.Count>0?LivePilotReadinessStatus.EvidenceRequired:LivePilotReadinessStatus.ReadyForInfrastructureBuild;
        var hash=LivePilotDesignDecision.Hash(JsonSerializer.Serialize(new{review.ReviewId,review.ReviewVersion,Decisions=review.Decisions.OrderBy(x=>x.Topic,StringComparer.Ordinal).Select(x=>x.ContentHash()).ToArray(),review.Evidence,review.ReviewedAtUtc,AuditorVersion=Version}));
        return new(review.ReviewId,status,missing,evidenceFailures,hash,status==LivePilotReadinessStatus.ReadyForInfrastructureBuild,false,false);
    }
}
