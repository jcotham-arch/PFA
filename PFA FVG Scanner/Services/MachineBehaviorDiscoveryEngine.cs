using System.Text.Json;
using PFA_FVG_Scanner.Domain.Discovery;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class MachineBehaviorDiscoveryEngine
{
    public MachineDiscoveryResult Discover(MachineDiscoveryManifest manifest,IReadOnlyList<MachineDiscoveryObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(manifest);ArgumentNullException.ThrowIfNull(observations);manifest.Split.Validate();
        if(manifest.ClusterCount<1||manifest.FeatureIds.Count<1)throw new ArgumentException("At least one feature and cluster are required.");
        if(manifest.FamilyWiseAlpha<=0||manifest.FamilyWiseAlpha>1)throw new ArgumentException("Family-wise alpha must be in (0,1].");
        if(!string.Equals(manifest.MultipleComparisonMethod,"Bonferroni",StringComparison.OrdinalIgnoreCase))throw new NotSupportedException("Only the explicitly versioned Bonferroni correction is currently supported.");
        var featureIds=manifest.FeatureIds.Order(StringComparer.Ordinal).ToArray();
        var rows=observations.OrderBy(x=>x.ObservationId,StringComparer.Ordinal).ToArray();
        if(rows.Select(x=>x.ObservationId).Distinct(StringComparer.Ordinal).Count()!=rows.Length)throw new InvalidOperationException("Observation identifiers must be unique.");
        foreach(var row in rows)
        {
            if(row.DatasetId!=manifest.Dataset.DatasetId||row.DataRevision!=manifest.Dataset.DataRevision)throw new InvalidOperationException("Dataset or correction revision differs from the frozen manifest.");
            if(row.FeatureKnownAtUtc>row.EventTimeUtc)throw new InvalidOperationException("Feature leakage detected: a feature was not known at decision time.");
            if(row.OutcomeKnownAtUtc>manifest.AsOfUtc)throw new InvalidOperationException("Outcome leakage detected: an outcome was not known when the run began.");
            if(featureIds.Any(id=>!row.Features.ContainsKey(id)))throw new InvalidOperationException("An observation is missing a declared feature.");
        }
        var train=rows.Where(x=>x.EventTimeUtc>=manifest.Split.TrainingStartUtc&&x.EventTimeUtc<manifest.Split.TrainingEndUtc&&x.OutcomeKnownAtUtc<=manifest.Split.TrainingEndUtc).ToArray();
        var evaluation=rows.Where(x=>x.EventTimeUtc>=manifest.Split.EvaluationStartUtc&&x.EventTimeUtc<manifest.Split.EvaluationEndUtc).ToArray();
        if(train.Length<manifest.ClusterCount)throw new InvalidOperationException("Training population is smaller than the declared cluster count.");
        var inputHash=MachineDiscoveryManifest.Hash(JsonSerializer.Serialize(rows));
        var modelId=$"MDL_{MachineDiscoveryManifest.Hash($"{manifest.ContentHash()}|{inputHash}")[..20]}";
        var assignments=train.Select(x=>(Row:x,Cluster:Assign(x,featureIds,manifest.RandomSeed,manifest.ClusterCount))).ToArray();
        var evalAssignments=evaluation.Select(x=>(Row:x,Cluster:Assign(x,featureIds,manifest.RandomSeed,manifest.ClusterCount))).ToArray();
        var populationMeans=featureIds.ToDictionary(id=>id,id=>train.Average(x=>x.Features[id]),StringComparer.Ordinal);
        var clusters=new List<MachineFeatureCluster>();var hypotheses=new List<ResearchHypothesis>();
        for(var ordinal=0;ordinal<manifest.ClusterCount;ordinal++)
        {
            var t=assignments.Where(x=>x.Cluster==ordinal).Select(x=>x.Row).ToArray();var e=evalAssignments.Where(x=>x.Cluster==ordinal).Select(x=>x.Row).ToArray();
            var centroid=featureIds.ToDictionary(id=>id,id=>t.Length==0?0:t.Average(x=>x.Features[id]),StringComparer.Ordinal);
            var explanations=featureIds.Select(id=>new FeatureExplanation(id,centroid[id],populationMeans[id],Math.Abs(centroid[id]-populationMeans[id]),0)).OrderByDescending(x=>x.AbsoluteDifference).ThenBy(x=>x.FeatureId,StringComparer.Ordinal).Select((x,i)=>x with{Rank=i+1}).ToArray();
            var trainMean=t.Length==0?0:t.Average(x=>x.OutcomeR);var evalMean=e.Length==0?0:e.Average(x=>x.OutcomeR);
            var rawP=ConservativeP(e);var adjusted=Math.Min(1m,rawP*manifest.ClusterCount);var clusterId=$"FeatureCluster_{modelId}_{ordinal:D3}";
            var clusterHash=MachineDiscoveryManifest.Hash(JsonSerializer.Serialize(new{clusterId,ordinal,centroid,Training=t.Select(x=>x.ObservationId),Evaluation=e.Select(x=>x.ObservationId),trainMean,evalMean,rawP,adjusted,explanations}));
            clusters.Add(new(clusterId,ordinal,centroid,t.Length,e.Length,trainMean,evalMean,rawP,adjusted,explanations,clusterHash));
            var status=e.Length<5?ResearchHypothesisStatus.InsufficientEvidence:adjusted<=manifest.FamilyWiseAlpha?(evalMean>0?ResearchHypothesisStatus.Positive:ResearchHypothesisStatus.Negative):ResearchHypothesisStatus.Candidate;
            hypotheses.Add(new($"HYP_{clusterHash[..20]}",clusterId,"FeatureCluster",JsonSerializer.Serialize(new{manifest.ModelVersion,Centroid=centroid,Explainability=explanations,AdjustedAlpha=manifest.FamilyWiseAlpha/manifest.ClusterCount}),status,e.Length,e.Length,[new("TrainingMeanR",trainMean,"R"),new("EvaluationMeanR",evalMean,"R"),new("RawPValue",rawP,"probability"),new("BonferroniAdjustedPValue",adjusted,"probability")],$"machine-discovery:{manifest.RunId}:{clusterId}",false));
        }
        var research=new GeneralResearchRun(manifest.RunId,manifest.EngineVersion,ResearchRunStatus.Completed,manifest.Dataset,new($"machine-{manifest.RunId}",manifest.SearchVersion,JsonSerializer.Serialize(new{manifest.ModelVersion,Features=featureIds,manifest.ClusterCount}),manifest.ClusterCount,manifest.MultipleComparisonMethod,manifest.RandomSeed),new(rows.Length,train.Length+evaluation.Length,train.Length+evaluation.Length,new Dictionary<string,int>{{"EmbargoOrOutsidePartition",rows.Length-train.Length-evaluation.Length}},"ObservationId"),hypotheses,JsonSerializer.Serialize(manifest),manifest.AsOfUtc,manifest.AsOfUtc,null,false);
        return new(manifest,manifest.ContentHash(),modelId,inputHash,clusters,research,false);
    }
    private static int Assign(MachineDiscoveryObservation row,string[] features,int seed,int count)
    {var signature=string.Join('|',features.Select(id=>$"{id}:{decimal.Round(row.Features[id],4)}"));var hash=MachineDiscoveryManifest.Hash($"{seed}|{signature}");return (int)(Convert.ToUInt32(hash[..8],16)%count);}
    private static decimal ConservativeP(MachineDiscoveryObservation[] rows)
    {if(rows.Length<5)return 1m;var positives=rows.Count(x=>x.OutcomeR>0);var negatives=rows.Count(x=>x.OutcomeR<0);var imbalance=Math.Abs(positives-negatives)/(decimal)rows.Length;return decimal.Round(1m-imbalance,6);}
}
