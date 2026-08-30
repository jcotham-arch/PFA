using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.OrderFlow;

namespace PFA_FVG_Scanner.Services;

public sealed class MesOrderFlowResearchService(PfaDatabase database,OrderFlowRepository orderFlow)
{
    public const string Version="mes-sweep-volume-proxy-1.0.0";
    private const decimal TargetR=1.5m;

    public async Task<MesOrderFlowResearchReport> RunAsync(int lookbackDays=120,CancellationToken token=default)
    {
        var bars=await Bars(Math.Clamp(lookbackDays,30,365),token);if(bars.Length<100)throw new InvalidOperationException("Insufficient MES one-minute history.");
        var raw=new List<MesSweepProxySample>();for(var index=20;index<bars.Length-30;index++)
        {
            var bar=bars[index];var prior=bars[(index-20)..index];var priorHigh=prior.Max(x=>x.High);var priorLow=prior.Min(x=>x.Low);
            var bullish=bar.Low<priorLow&&bar.Close>priorLow;var bearish=bar.High>priorHigh&&bar.Close<priorHigh;if(bullish==bearish)continue;
            var direction=bullish?"Bullish":"Bearish";var entry=bar.Close;var stop=bullish?bar.Low:bar.High;var risk=Math.Abs(entry-stop);if(risk<.25m||risk>10m)continue;
            var target=bullish?entry+risk*TargetR:entry-risk*TargetR;var volumeBaseline=Math.Max(1m,prior.Average(x=>x.Volume));var volumeRatio=bar.Volume/volumeBaseline;
            var outcome="Unresolved";var holding=30;for(var forward=1;forward<=30;forward++)
            {var next=bars[index+forward];var stopHit=bullish?next.Low<=stop:next.High>=stop;var targetHit=bullish?next.High>=target:next.Low<=target;
                if(stopHit){outcome="Loss";holding=forward;break;}if(targetHit){outcome="Win";holding=forward;break;}}
            var gross=outcome=="Win"?TargetR:outcome=="Loss"?-1m:0m;var costR=.498m/risk;var net=outcome=="Unresolved"?0:gross-costR;
            var position=(decimal)index/bars.Length;var split=position<.6m?"Train":position<.8m?"Validation":"Test";
            var seed=JsonSerializer.Serialize(new{bar.CloseTimeUtc,direction,entry,stop,target,risk,volumeRatio,outcome,net,holding});var hash=AgentTrainingDatasetBuilder.Hash(seed);
            raw.Add(new($"MSP-{hash[..32]}",bar.CloseTimeUtc,direction,split,entry,stop,target,risk,volumeRatio,outcome,net,holding,hash));
        }
        var variants=new[]{("StructureOnly",raw.AsEnumerable()),("VolumeExpansion125",raw.Where(x=>x.VolumeRatio>=1.25m))};
        var metrics=variants.SelectMany(variant=>new[]{"Train","Validation","Test"}.Select(split=>Metric(variant.Item1,split,variant.Item2.Where(x=>x.Split==split).ToArray()))).ToArray();
        var coverage=await orderFlow.GetCoverageAsync(token);var hashSeed=JsonSerializer.Serialize(new{Version,AsOf=bars[^1].CloseTimeUtc,Bars=bars.Length,Samples=raw.Select(x=>x.ContentHash),Metrics=metrics});var contentHash=AgentTrainingDatasetBuilder.Hash(hashSeed);
        var report=new MesOrderFlowResearchReport($"MOFR-{contentHash[..32]}",Version,bars[^1].CloseTimeUtc,bars.Length,raw.Count,
            raw.Count(x=>x.VolumeRatio>=1.25m),metrics,coverage,"BarResponseProxy",
            coverage.Events==0?"Bars can test sweep geometry and volume response, but cannot establish CVD, absorption, aggressor flow, or L2 behavior.":"True event data exists; build synchronized feature snapshots before testing order-flow admission.",
            coverage.FeatureSnapshots>0,false,false,contentHash);await Persist(report,token);return report;
    }

    public async Task<MesOrderFlowResearchReport?> LatestAsync(CancellationToken token=default)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT ReportJson FROM MesOrderFlowResearchReports ORDER BY CreatedAtUtc DESC LIMIT 1";
        var json=await command.ExecuteScalarAsync(token) as string;return json is null?null:JsonSerializer.Deserialize<MesOrderFlowResearchReport>(json);}

    private static MesSweepProxyMetrics Metric(string variant,string split,IReadOnlyList<MesSweepProxySample> values)
    {var resolved=values.Where(x=>x.Outcome!="Unresolved").ToArray();var wins=resolved.Count(x=>x.Outcome=="Win");var losses=resolved.Length-wins;
        var gains=resolved.Where(x=>x.NetR>0).Sum(x=>x.NetR);var loss=Math.Abs(resolved.Where(x=>x.NetR<0).Sum(x=>x.NetR));return new(variant,split,values.Count,resolved.Length,wins,losses,
            resolved.Length==0?0:wins/(decimal)resolved.Length,resolved.Length==0?0:resolved.Average(x=>x.NetR),loss==0?0:gains/loss,resolved.Sum(x=>x.NetR),resolved.Select(x=>DateOnly.FromDateTime(x.SignalTimeUtc)).Distinct().Count());}

    private async Task<BarRow[]> Bars(int days,CancellationToken token)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="""
        WITH latest AS (SELECT MAX(OpenTimeUtc) Value FROM Candles WHERE Symbol LIKE 'MES%' AND Timeframe='1m'),
        ranked AS (SELECT OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume,
         ROW_NUMBER() OVER(PARTITION BY OpenTimeUtc ORDER BY Id DESC) rank FROM Candles,latest
         WHERE Symbol LIKE 'MES%' AND Timeframe='1m' AND IsComplete=1 AND julianday(OpenTimeUtc)>=julianday(latest.Value)-$days)
        SELECT OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume FROM ranked WHERE rank=1 ORDER BY OpenTimeUtc;
        """;command.Parameters.AddWithValue("$days",days);var values=new List<BarRow>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))values.Add(new(
            ParseTime(reader,0),ParseTime(reader,1),Parse(reader,2),Parse(reader,3),Parse(reader,4),Parse(reader,5),Parse(reader,6)));return values.ToArray();}
    private async Task Persist(MesOrderFlowResearchReport report,CancellationToken token)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="""
        INSERT OR IGNORE INTO MesOrderFlowResearchReports
        (ReportId,EngineVersion,AsOfUtc,BarsEvaluated,CandidateCount,DataTier,ContentHash,ReportJson,CreatedAtUtc,CanActivateStrategy,CanRouteToRealBroker)
        VALUES($id,$version,$asOf,$bars,$candidates,$tier,$hash,$json,$created,0,0);
        """;command.Parameters.AddWithValue("$id",report.ReportId);command.Parameters.AddWithValue("$version",report.EngineVersion);command.Parameters.AddWithValue("$asOf",report.AsOfUtc.ToString("O"));command.Parameters.AddWithValue("$bars",report.BarsEvaluated);command.Parameters.AddWithValue("$candidates",report.StructuralSweepCandidates);command.Parameters.AddWithValue("$tier",report.DataTier);command.Parameters.AddWithValue("$hash",report.ContentHash);command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(report));command.Parameters.AddWithValue("$created",DateTime.UtcNow.ToString("O"));await command.ExecuteNonQueryAsync(token);}
    private static DateTime ParseTime(SqliteDataReader reader,int index)=>DateTime.Parse(reader.GetString(index),null,DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static decimal Parse(SqliteDataReader reader,int index)=>decimal.Parse(reader.GetString(index),CultureInfo.InvariantCulture);
    private sealed record BarRow(DateTime OpenTimeUtc,DateTime CloseTimeUtc,decimal Open,decimal High,decimal Low,decimal Close,decimal Volume);
}
