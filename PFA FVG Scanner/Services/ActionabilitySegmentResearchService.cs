using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class ActionabilitySegmentResearchService(PfaDatabase database)
{
    public const string Version="actionability-segment-research-1.1.0";
    public async Task<ActionabilitySegmentResearchReport> AnalyzeAsync(int minimumSamples=100,CancellationToken token=default)
    {
        if(minimumSamples<20)throw new ArgumentOutOfRangeException(nameof(minimumSamples));await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        var dataset=await LatestDataset(connection,token)??throw new InvalidOperationException("No actionability outcome dataset exists.");
        var rows=await Rows(connection,dataset.Id,token);var groups=rows.SelectMany(x=>Keys(x).Select(key=>(key,x)))
            .GroupBy(x=>x.key.Identity,StringComparer.Ordinal).Select(group=>Candidate(group.Key,group.Select(x=>x.x).ToArray(),minimumSamples)).Where(x=>x is not null)
            .Select(x=>x!).OrderByDescending(x=>x.Validation.MeanNetR).ThenByDescending(x=>x.Validation.ProfitFactor).ThenBy(x=>x.SegmentId).ToArray();
        return new(Version,dataset.Id,dataset.Hash,rows.Count,groups.Length,
            groups.Count(x=>x.Status=="UntouchedTestConfirmed"),groups,DateTime.UtcNow);
    }

    private static ActionabilitySegmentCandidate? Candidate(string identity,Row[] rows,int minimum)
    {
        var key=Key.Parse(identity);var train=Metric("Train",rows);var validation=Metric("Validation",rows);if(train.Samples<minimum||validation.Samples<minimum||train.MeanNetR<=0||train.ProfitFactor<=1||validation.MeanNetR<=0||validation.ProfitFactor<=1)return null;
        var test=Metric("Test",rows);var reasons=new List<string>();if(test.Samples<minimum)reasons.Add("Untouched test sample count is below the minimum.");if(test.MeanNetR<=0)reasons.Add("Untouched test expectancy is non-positive.");if(test.ProfitFactor<=1)reasons.Add("Untouched test profit factor is at or below 1.0.");
        var status=reasons.Count==0?"UntouchedTestConfirmed":"ValidationSelectedTestRejected";var hash=Hash(identity);
        return new($"ASG-{hash[..24]}",key.Granularity,key.Module,key.Instrument,key.Session,key.Context,key.Entry,key.Stop,key.Exit,key.Direction,key.TargetR,key.Hold,train,validation,test,status,reasons);
    }
    private static ActionabilitySegmentMetric Metric(string split,IReadOnlyList<Row> rows)
    {var values=rows.Where(x=>x.Split==split).Select(x=>x.NetR).ToArray();if(values.Length==0)return new(split,0,0,0,0,0);var wins=values.Where(x=>x>0).Sum();var losses=Math.Abs(values.Where(x=>x<0).Sum());decimal equity=0,peak=0,dd=0;foreach(var value in values){equity+=value;peak=Math.Max(peak,equity);dd=Math.Max(dd,peak-equity);}return new(split,values.Length,Round(values.Average()),Round(values.Count(x=>x>0)/(decimal)values.Length),losses==0?decimal.MaxValue:Round(wins/losses),Round(dd));}
    private static IEnumerable<Key> Keys(Row row)
    {yield return new("ModuleInstrument",row.Module,row.Instrument,"*","*","*","*","*","*",0,0);yield return new("ModuleSession",row.Module,"*",row.Session,"*","*","*","*","*",0,0);yield return new("ModulePolicy",row.Module,"*","*","*",row.Entry,row.Stop,row.Exit,row.Direction,row.TargetR,row.Hold);yield return new("ModuleInstrumentPolicy",row.Module,row.Instrument,"*","*",row.Entry,row.Stop,row.Exit,row.Direction,row.TargetR,row.Hold);yield return new("ModuleSessionPolicy",row.Module,"*",row.Session,"*",row.Entry,row.Stop,row.Exit,row.Direction,row.TargetR,row.Hold);foreach(var context in row.ContextBuckets){yield return new("ModuleContext",row.Module,"*","*",context,"*","*","*","*",0,0);yield return new("ModulePolicyContext",row.Module,"*","*",context,row.Entry,row.Stop,row.Exit,row.Direction,row.TargetR,row.Hold);}}
    private static async Task<(string Id,string Hash)?> LatestDataset(SqliteConnection connection,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText="SELECT DatasetId,ContentHash FROM AgentResearchDatasets WHERE DatasetVersion LIKE 'actionability-outcome-dataset-%' ORDER BY CreatedAtUtc DESC LIMIT 1";await using var reader=await command.ExecuteReaderAsync(token);return await reader.ReadAsync(token)?(reader.GetString(0),reader.GetString(1)):null;}
    private static async Task<List<Row>> Rows(SqliteConnection connection,string dataset,CancellationToken token)
    {var output=new List<Row>();await using var command=connection.CreateCommand();command.CommandText="SELECT InstrumentId,ModuleId,Split,FeatureJson,LabelJson FROM AgentResearchExamples WHERE DatasetId=$id ORDER BY EventTimeUtc,ExampleId";command.Parameters.AddWithValue("$id",dataset);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){using var features=JsonDocument.Parse(reader.GetString(3));using var labels=JsonDocument.Parse(reader.GetString(4));if(!labels.RootElement.TryGetProperty("netR",out var net)||!net.TryGetDecimal(out var netR))continue;var root=features.RootElement;output.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),netR,One(root,"context.session."),One(root,"policy.entry."),One(root,"policy.stop."),One(root,"policy.exit."),One(root,"policy.direction."),Number(root,"policy.targetR"),Number(root,"policy.maximumHoldingMinutes"),Buckets(root)));}return output;}
    private static string One(JsonElement root,string prefix)=>root.EnumerateObject().FirstOrDefault(x=>x.Name.StartsWith(prefix,StringComparison.Ordinal)&&x.Value.TryGetDecimal(out var value)&&value==1).Name?[prefix.Length..]??"unknown";
    private static decimal Number(JsonElement root,string name)=>root.TryGetProperty(name,out var value)&&value.TryGetDecimal(out var number)?number:0;
    private static string[] Buckets(JsonElement root)
    {var relative=Number(root,"context.volume.relativeVolume");var close=Number(root,"market.closeLocation");var trend=Number(root,"context.trend.meanBodyToRange20");var momentum=Number(root,"context.momentum.return5Fraction");return [$"relative-volume:{(relative<.75m?"low":relative>1.25m?"high":"normal")}",$"close-location:{(close<.25m?"lower":close>.75m?"upper":"middle")}",$"trend-intensity:{(trend<.25m?"low":trend>.60m?"high":"normal")}",$"momentum:{(momentum>0?"positive":momentum<0?"negative":"flat")}"];}
    private static decimal Round(decimal value)=>decimal.Round(value,6,MidpointRounding.AwayFromZero);
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private sealed record Row(string Instrument,string Module,string Split,decimal NetR,string Session,string Entry,string Stop,string Exit,string Direction,decimal TargetR,decimal Hold,string[] ContextBuckets);
    private sealed record Key(string Granularity,string Module,string Instrument,string Session,string Context,string Entry,string Stop,string Exit,string Direction,decimal TargetR,decimal Hold)
    {public string Identity=>$"{Granularity}|{Module}|{Instrument}|{Session}|{Context}|{Entry}|{Stop}|{Exit}|{Direction}|{TargetR:G29}|{Hold:G29}";public static Key Parse(string value){var x=value.Split('|');return new(x[0],x[1],x[2],x[3],x[4],x[5],x[6],x[7],x[8],decimal.Parse(x[9],System.Globalization.CultureInfo.InvariantCulture),decimal.Parse(x[10],System.Globalization.CultureInfo.InvariantCulture));}}
}
