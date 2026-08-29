using PFA_FVG_Scanner.Domain.Agent;

namespace PFA_FVG_Scanner.Services;

public sealed record AgentSandboxPromotionCheck(string Check,string Status,string Evidence);
public sealed record AgentSandboxPromotionReadiness(string Status,string? RunId,string? DatasetId,string? ArtifactId,
    string? Candidate,IReadOnlyList<AgentSandboxPromotionCheck> Checks,IReadOnlyList<string> BlockingReasons,
    bool CanCreateProspectiveSandbox,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false,
    string Interpretation="Only a frozen model with qualified validation, untouched test, and economic walk-forward evidence may enter a blind prospective sandbox.");

public sealed class AgentSandboxPromotionReadinessService(AgentBaselineTrainingService training)
{
    public async Task<AgentSandboxPromotionReadiness> GetAsync(CancellationToken token=default)
    {
        var run=(await training.GetAllAsync(token)).FirstOrDefault(x=>x.TargetName=="netR");
        if(run is null)return new("NoNetRRun",null,null,null,null,[],["No finalized net-R training run exists."],false);
        var artifact=run.ModelArtifacts?.FirstOrDefault(x=>x.Variant==run.PromotionGate?.Candidate)??
            run.ModelArtifacts?.FirstOrDefault(x=>x.Variant=="ridge-linear");
        var candidate=run.PromotionGate?.Candidate;var validation=run.EconomicPolicyMetrics?.FirstOrDefault(x=>x.Variant==candidate&&x.Split=="Validation");
        var test=run.EconomicPolicyMetrics?.FirstOrDefault(x=>x.Variant==candidate&&x.Split=="Test");
        var folds=run.EconomicWalkForwardMetrics??[];var checks=new List<AgentSandboxPromotionCheck>
        {
            Check("Frozen dataset",run.DatasetContentHash.Length>0,$"{run.DatasetId} · {run.DatasetContentHash}"),
            Check("Frozen model artifact",artifact is not null,artifact is null?"No deployable artifact":$"{artifact.ArtifactId} · {artifact.ContentHash}"),
            Check("Validation expectancy",validation is{SelectedSamples:>=100,MeanNetR:>0,ProfitFactor:>1},Economic(validation)),
            Check("Untouched test expectancy",test is{SelectedSamples:>=100,MeanNetR:>0,ProfitFactor:>1},Economic(test)),
            Check("Economic walk-forward",folds.Count>=3&&folds.All(x=>x.SelectedSamples>=100&&x.MeanNetR>0&&x.ProfitFactor>1),
                folds.Count==0?"No economic folds":string.Join(" · ",folds.Select(x=>$"F{x.Fold} {x.MeanNetR:F3}R PF {x.ProfitFactor:F2} n={x.SelectedSamples}"))),
            Check("Research promotion gate",run.PromotionGate?.Status=="EligibleForResearchReview",run.PromotionGate?.Status??"Missing")
        };
        var blockers=checks.Where(x=>x.Status=="Failed").Select(x=>$"{x.Check}: {x.Evidence}").Concat(run.PromotionGate?.Reasons??[]).Distinct().ToArray();
        var eligible=blockers.Length==0&&artifact is not null;
        return new(eligible?"ReadyForProspectiveSandbox":"RejectedByResearchEvidence",run.RunId,run.DatasetId,
            artifact?.ArtifactId,candidate,checks,blockers,eligible);
    }
    private static AgentSandboxPromotionCheck Check(string name,bool pass,string evidence)=>new(name,pass?"Passed":"Failed",evidence);
    private static string Economic(AgentEconomicPolicyMetric? value)=>value is null?"Missing":$"{value.MeanNetR:F3}R · PF {value.ProfitFactor:F2} · n={value.SelectedSamples} · DD {value.MaximumDrawdownR:F1}R";
}
