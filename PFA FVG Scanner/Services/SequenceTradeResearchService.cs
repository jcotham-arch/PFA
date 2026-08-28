using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class SequenceTradeResearchService(PfaDatabase database)
{
    public const string Version="sequence-trade-context-engine-1.0.0";
    private static readonly string[] Definitions=
    ["liquidity-sweep-to-breakout","breakout-continuation","breakout-failure","failed-breakout-reversal"];

    public async Task<SequenceTradeResearchRun> RunAsync(SequenceTradeResearchRequest request,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(request);var asOf=Utc(request.AsOfUtc);
        var source=await ReadSourceRunAsync(request.SourcePatternTradeRunId,token)??
            throw new InvalidOperationException("No source pattern-trade run is available.");
        if(source.AsOfUtc>asOf)throw new ArgumentException("The sequence study AsOfUtc cannot precede its source trade run.");
        var metadata=source.Summaries.GroupBy(x=>x.HypothesisId).ToDictionary(x=>x.Key,x=>x.First(),StringComparer.Ordinal);
        var samples=await ReadSamplesAsync(source.RunId,asOf,token);
        var summaries=Summarize(samples,metadata);
        var seed=JsonSerializer.Serialize(new{Version,source.RunId,asOf,Samples=samples.Select(x=>x.ContentHash),summaries});
        var hash=AgentTrainingDatasetBuilder.Hash(seed);
        var run=new SequenceTradeResearchRun($"STR-{hash[..32]}",Version,source.RunId,asOf,
            samples.Select(x=>x.SequenceInstanceId).Distinct().Count(),samples.Count,summaries,hash,DateTime.UtcNow);
        await PersistAsync(run,samples,token);return run;
    }

    public async Task<IReadOnlyList<SequenceTradeResearchRun>> GetAllAsync(CancellationToken token=default)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="SELECT RunJson FROM SequenceTradeResearchRuns ORDER BY CreatedAtUtc DESC";
        var values=new List<SequenceTradeResearchRun>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<SequenceTradeResearchRun>(reader.GetString(0))!);
        return values;
    }

    private async Task<PatternTradeResearchRun?> ReadSourceRunAsync(string? requested,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText=requested is null?
            "SELECT RunJson FROM PatternTradeResearchRuns WHERE EngineVersion='pattern-trade-hypothesis-engine-1.3.0' ORDER BY CreatedAtUtc DESC LIMIT 1":
            "SELECT RunJson FROM PatternTradeResearchRuns WHERE RunId=$id LIMIT 1";
        if(requested is not null)command.Parameters.AddWithValue("$id",requested);
        var value=await command.ExecuteScalarAsync(token);return value is string json?JsonSerializer.Deserialize<PatternTradeResearchRun>(json):null;
    }

    private async Task<List<SequenceTradeContextSample>> ReadSamplesAsync(string sourceRunId,DateTime asOf,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText=$"""
            SELECT s.SampleId,s.HypothesisId,s.ObservationId,s.Split,s.Outcome,s.NetR,
                   i.SequenceInstanceId,i.SequenceDefinitionId,i.UpdatedAtUtc,m.Role,o.KnownAtUtc
            FROM PatternTradeResearchSamples s
            JOIN UniversalMarketObservations o ON o.ObservationId=s.ObservationId
            JOIN MarketSequenceMembers m ON m.ObservationId=s.ObservationId
            JOIN MarketSequenceInstances i ON i.SequenceInstanceId=m.SequenceInstanceId
            WHERE s.RunId=$run AND i.State='Successful' AND i.UpdatedAtUtc<=$asOf
              AND i.SequenceDefinitionId IN ({string.Join(',',Definitions.Select((_,i)=>$"$definition{i}"))})
              AND m.Ordinal=(SELECT MAX(m2.Ordinal) FROM MarketSequenceMembers m2 WHERE m2.SequenceInstanceId=i.SequenceInstanceId)
            ORDER BY i.UpdatedAtUtc,i.SequenceInstanceId,s.HypothesisId;
            """;
        command.Parameters.AddWithValue("$run",sourceRunId);command.Parameters.AddWithValue("$asOf",asOf.ToString("O"));
        for(var i=0;i<Definitions.Length;i++)command.Parameters.AddWithValue($"$definition{i}",Definitions[i]);
        var values=new List<SequenceTradeContextSample>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))
        {
            var sequenceKnown=Parse(reader.GetString(8));var decision=Parse(reader.GetString(10));
            if(sequenceKnown>decision)continue;
            var sourceSample=reader.GetString(0);var hypothesis=reader.GetString(1);var observation=reader.GetString(2);
            var sequence=reader.GetString(6);var definition=reader.GetString(7);var role=reader.GetString(9);
            var split=reader.GetString(3);var outcome=Enum.Parse<HypothesisExitOutcome>(reader.GetString(4));
            decimal? net=reader.IsDBNull(5)?null:decimal.Parse(reader.GetString(5),CultureInfo.InvariantCulture);
            var identity=JsonSerializer.Serialize(new{Version,sourceSample,sequence,definition,role,hypothesis,observation,split,outcome,net,sequenceKnown,decision});
            var hash=AgentTrainingDatasetBuilder.Hash(identity);
            values.Add(new($"STCS-{hash[..32]}",sourceSample,sequence,definition,role,hypothesis,observation,
                split,outcome,net,sequenceKnown,decision,hash));
        }
        return values;
    }

    private static IReadOnlyList<SequenceTradeHypothesisSummary> Summarize(IReadOnlyList<SequenceTradeContextSample> samples,
        IReadOnlyDictionary<string,PatternTradeHypothesisSummary> metadata)=>samples
        .GroupBy(x=>new{x.SequenceDefinitionId,x.Role,x.HypothesisId,x.Split})
        .Select(group=>
        {
            var rows=group.OrderBy(x=>x.DecisionTimeUtc).ToArray();var resolved=rows.Where(x=>x.NetR.HasValue).ToArray();
            var wins=resolved.Where(x=>x.NetR>0).Sum(x=>x.NetR!.Value);var losses=Math.Abs(resolved.Where(x=>x.NetR<0).Sum(x=>x.NetR!.Value));
            decimal equity=0,peak=0,drawdown=0;foreach(var row in resolved){equity+=row.NetR!.Value;peak=Math.Max(peak,equity);drawdown=Math.Max(drawdown,peak-equity);}
            var meta=metadata[group.Key.HypothesisId];
            return new SequenceTradeHypothesisSummary(group.Key.SequenceDefinitionId,group.Key.Role,group.Key.HypothesisId,
                meta.ModuleId,meta.EntryPolicy,meta.StopPolicy,meta.ExitPolicy,meta.DirectionPolicy,meta.TargetR,
                meta.MaximumHoldingMinutes,group.Key.Split,rows.Length,rows.Count(x=>x.Outcome==HypothesisExitOutcome.Target),
                rows.Count(x=>x.Outcome==HypothesisExitOutcome.Stop),rows.Count(x=>x.Outcome==HypothesisExitOutcome.BreakEven),
                rows.Count(x=>x.Outcome==HypothesisExitOutcome.TimeExit),rows.Count(x=>x.Outcome==HypothesisExitOutcome.Ambiguous),
                rows.Count(x=>x.Outcome is HypothesisExitOutcome.NoEntry or HypothesisExitOutcome.InvalidRisk),
                resolved.Length==0?0:Round(resolved.Average(x=>x.NetR!.Value)),resolved.Length==0?0:Round(resolved.Count(x=>x.NetR>0)/(decimal)resolved.Length),
                losses==0?(wins>0?999m:0m):Round(wins/losses),Round(drawdown));
        }).OrderBy(x=>x.SequenceDefinitionId).ThenBy(x=>x.HypothesisId).ThenBy(x=>x.Split).ToArray();

    private async Task PersistAsync(SequenceTradeResearchRun run,IReadOnlyList<SequenceTradeContextSample> samples,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="INSERT OR IGNORE INTO SequenceTradeResearchRuns(RunId,EngineVersion,SourcePatternTradeRunId,AsOfUtc,SequenceCompletionCount,ContextSampleCount,ContentHash,RunJson,CreatedAtUtc,CanActivateStrategy,CanRouteToRealBroker) VALUES($id,$version,$source,$asOf,$sequences,$samples,$hash,$json,$created,0,0)";
            Add(command,"$id",run.RunId);Add(command,"$version",run.EngineVersion);Add(command,"$source",run.SourcePatternTradeRunId);Add(command,"$asOf",run.AsOfUtc.ToString("O"));Add(command,"$sequences",run.SequenceCompletionCount);Add(command,"$samples",run.ContextSampleCount);Add(command,"$hash",run.ContentHash);Add(command,"$json",JsonSerializer.Serialize(run));Add(command,"$created",run.CreatedAtUtc.ToString("O"));await command.ExecuteNonQueryAsync(token);}
        foreach(var sample in samples){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="INSERT OR IGNORE INTO SequenceTradeResearchSamples(RunId,ContextSampleId,SourceSampleId,SequenceInstanceId,SequenceDefinitionId,Role,HypothesisId,ObservationId,Split,Outcome,NetR,SequenceKnownAtUtc,DecisionTimeUtc,ContentHash) VALUES($run,$id,$source,$sequence,$definition,$role,$hypothesis,$observation,$split,$outcome,$net,$known,$decision,$hash)";
            Add(command,"$run",run.RunId);Add(command,"$id",sample.ContextSampleId);Add(command,"$source",sample.SourceSampleId);Add(command,"$sequence",sample.SequenceInstanceId);Add(command,"$definition",sample.SequenceDefinitionId);Add(command,"$role",sample.Role);Add(command,"$hypothesis",sample.HypothesisId);Add(command,"$observation",sample.ObservationId);Add(command,"$split",sample.Split);Add(command,"$outcome",sample.Outcome.ToString());Add(command,"$net",sample.NetR);Add(command,"$known",sample.SequenceKnownAtUtc.ToString("O"));Add(command,"$decision",sample.DecisionTimeUtc.ToString("O"));Add(command,"$hash",sample.ContentHash);await command.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);
    }

    private static DateTime Parse(string value)=>DateTime.Parse(value,null,DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static DateTime Utc(DateTime value)=>value.Kind switch{DateTimeKind.Utc=>value,DateTimeKind.Unspecified=>DateTime.SpecifyKind(value,DateTimeKind.Utc),_=>value.ToUniversalTime()};
    private static decimal Round(decimal value)=>decimal.Round(value,6,MidpointRounding.AwayFromZero);
    private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}
