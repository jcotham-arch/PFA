using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class ActionabilityLabelProfileService(PfaDatabase database)
{
    public const string Version="actionability-label-profile-1.0.0";
    public async Task<ActionabilityLabelProfileReport> GetAsync(CancellationToken token=default)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        var dataset=await LatestDataset(connection,token)??throw new InvalidOperationException("No actionability outcome dataset exists.");
        var rows=await Rows(connection,dataset.Id,token);
        var profiles=rows.GroupBy(x=>(x.Split,x.Module)).OrderBy(x=>x.Key.Split).ThenBy(x=>x.Key.Module)
            .Select(x=>Profile(x.Key.Split,x.Key.Module,x.ToArray())).ToArray();
        return new(Version,dataset.Id,dataset.Hash,rows.Count,
            ["profitable","conditionalPositiveNetR","conditionalNegativeNetR","maximumFavorableExcursionR","maximumAdverseExcursionR"],
            profiles,DateTime.UtcNow);
    }
    private static ActionabilityLabelProfile Profile(string split,string module,Row[] rows)
    {var positive=rows.Where(x=>x.NetR>0).Select(x=>x.NetR).ToArray();var negative=rows.Where(x=>x.NetR<0).Select(x=>x.NetR).ToArray();return new(split,module,rows.Length,positive.Length,
        Round(positive.Length/(decimal)rows.Length),Round(rows.Average(x=>x.NetR)),positive.Length==0?0:Round(positive.Average()),
        negative.Length==0?0:Round(negative.Average()),Round(rows.Average(x=>x.Mfe)),Round(rows.Average(x=>x.Mae)));}
    private static async Task<(string Id,string Hash)?> LatestDataset(SqliteConnection connection,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText="SELECT DatasetId,ContentHash FROM AgentResearchDatasets WHERE DatasetVersion LIKE 'actionability-outcome-dataset-%' ORDER BY CreatedAtUtc DESC LIMIT 1";await using var reader=await command.ExecuteReaderAsync(token);return await reader.ReadAsync(token)?(reader.GetString(0),reader.GetString(1)):null;}
    private static async Task<List<Row>> Rows(SqliteConnection connection,string dataset,CancellationToken token)
    {var output=new List<Row>();await using var command=connection.CreateCommand();command.CommandText="SELECT Split,ModuleId,LabelJson FROM AgentResearchExamples WHERE DatasetId=$id ORDER BY EventTimeUtc,ExampleId";command.Parameters.AddWithValue("$id",dataset);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var labels=JsonSerializer.Deserialize<Dictionary<string,decimal>>(reader.GetString(2))??[];if(!labels.TryGetValue("netR",out var netR))continue;output.Add(new(reader.GetString(0),reader.GetString(1),netR,labels.GetValueOrDefault("maximumFavorableExcursionR"),labels.GetValueOrDefault("maximumAdverseExcursionR")));}return output;}
    private static decimal Round(decimal value)=>decimal.Round(value,6,MidpointRounding.AwayFromZero);
    private sealed record Row(string Split,string Module,decimal NetR,decimal Mfe,decimal Mae);
}
