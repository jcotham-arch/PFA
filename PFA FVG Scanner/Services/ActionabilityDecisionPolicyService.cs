using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class ActionabilityDecisionPolicyService(PfaDatabase database)
{
    public const string Version="actionability-decision-policy-1.0.0";

    public async Task<ActionabilityDecisionPolicyReport> AnalyzeAsync(int minimumSamples=100,CancellationToken token=default)
    {
        if(minimumSamples<20)throw new ArgumentOutOfRangeException(nameof(minimumSamples));
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        var run=await LatestRun(connection,token)??throw new InvalidOperationException("No finalized net-R model run exists.");
        var artifact=run.ModelArtifacts?.FirstOrDefault(x=>x.Variant=="ridge-linear")
            ??throw new InvalidOperationException("The latest net-R run has no frozen ridge-linear artifact.");
        var rows=await Rows(connection,run.DatasetId,artifact,token);
        var validation=rows.Where(x=>x.Split=="Validation").OrderBy(x=>x.Score).ToArray();
        var test=rows.Where(x=>x.Split=="Test").ToArray();
        if(validation.Length==0||test.Length==0)throw new InvalidOperationException("The net-R dataset requires validation and test examples.");
        var thresholds=Enumerable.Range(2,18).Select(x=>x*5).Select(percentile=>
            validation[(int)Math.Floor((validation.Length-1)*(percentile/100m))].Score).Distinct().ToArray();
        var candidates=thresholds.Select(threshold=>(Threshold:threshold,Metric:Metric("Validation",validation,threshold)))
            .Where(x=>x.Metric.Samples>=minimumSamples&&x.Metric.MeanNetR>0&&x.Metric.ProfitFactor>1)
            .OrderByDescending(x=>x.Metric.MeanNetR).ThenByDescending(x=>x.Metric.ProfitFactor).Take(10)
            .Select(x=>Candidate(run,artifact,x.Threshold,x.Metric,test,minimumSamples)).ToArray();
        return new(Version,run.DatasetId,run.DatasetContentHash,run.RunId,artifact.ArtifactId,validation.Length,test.Length,
            thresholds.Length,candidates,DateTime.UtcNow);
    }

    private static ActionabilityDecisionPolicyCandidate Candidate(AgentBaselineRun run,AgentLinearModelArtifact artifact,
        decimal threshold,ActionabilityDecisionPolicyMetric validation,Row[] test,int minimum)
    {
        var metric=Metric("Test",test,threshold);var reasons=new List<string>();
        if(metric.Samples<minimum)reasons.Add("Untouched test sample count is below the minimum.");
        if(metric.MeanNetR<=0)reasons.Add("Untouched test expectancy is non-positive.");
        if(metric.ProfitFactor<=1)reasons.Add("Untouched test profit factor is at or below 1.0.");
        var id=Hash($"{run.RunId}|{artifact.ArtifactId}|{threshold:G29}");
        return new($"ADP-{id[..24]}",run.RunId,artifact.ArtifactId,validation,metric,
            reasons.Count==0?"UntouchedTestConfirmed":"ValidationSelectedTestRejected",reasons);
    }

    private static ActionabilityDecisionPolicyMetric Metric(string split,IEnumerable<Row> rows,decimal threshold)
    {
        var values=rows.Where(x=>x.Score>=threshold).Select(x=>x.NetR).ToArray();
        if(values.Length==0)return new(split,0,Round(threshold),0,0,0,0);
        var wins=values.Where(x=>x>0).Sum();var losses=Math.Abs(values.Where(x=>x<0).Sum());decimal equity=0,peak=0,drawdown=0;
        foreach(var value in values){equity+=value;peak=Math.Max(peak,equity);drawdown=Math.Max(drawdown,peak-equity);}
        return new(split,values.Length,Round(threshold),Round(values.Average()),Round(values.Count(x=>x>0)/(decimal)values.Length),
            losses==0?decimal.MaxValue:Round(wins/losses),Round(drawdown));
    }

    private static async Task<AgentBaselineRun?> LatestRun(SqliteConnection connection,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText="SELECT RunJson FROM AgentBaselineRuns WHERE TargetName='netR' ORDER BY TrainedAtUtc DESC LIMIT 1";var json=Convert.ToString(await command.ExecuteScalarAsync(token));return string.IsNullOrWhiteSpace(json)?null:JsonSerializer.Deserialize<AgentBaselineRun>(json);}

    private static async Task<Row[]> Rows(SqliteConnection connection,string dataset,AgentLinearModelArtifact artifact,CancellationToken token)
    {var output=new List<Row>();await using var command=connection.CreateCommand();command.CommandText="SELECT Split,FeatureJson,LabelJson FROM AgentResearchExamples WHERE DatasetId=$id AND Split IN ('Validation','Test') ORDER BY EventTimeUtc,ExampleId";command.Parameters.AddWithValue("$id",dataset);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var features=JsonSerializer.Deserialize<Dictionary<string,decimal>>(reader.GetString(1))??[];var labels=JsonSerializer.Deserialize<Dictionary<string,decimal>>(reader.GetString(2))??[];if(!labels.TryGetValue("netR",out var netR))continue;var score=artifact.Coefficients[0];for(var i=0;i<artifact.FeatureNames.Count;i++)score+=artifact.Coefficients[i+1]*(features.GetValueOrDefault(artifact.FeatureNames[i])-artifact.Means[i])/artifact.Scales[i];output.Add(new(reader.GetString(0),netR,score));}return output.ToArray();}
    private static decimal Round(decimal value)=>decimal.Round(value,6,MidpointRounding.AwayFromZero);
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Row(string Split,decimal NetR,decimal Score);
}
