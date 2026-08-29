using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Domain.Strategies;

namespace PFA_FVG_Scanner.Services;

public sealed class ExploratoryPaperCampaignService(PfaDatabase database,
    ExploratorySandboxCandidateService candidates,IStrategyRegistry strategies,
    IInstrumentDefinitionRegistry instruments)
{
    public const string Version="mes-tier1-blind-paper-replay-1.0.0";
    private sealed record TelemetrySupplement(string CampaignId,string ExecutionId,long? TimeToMfeMilliseconds,
        long? TimeToMaeMilliseconds,string ContentHash);

    public async Task<ExploratoryPaperDashboard> RunBlindReplayAsync(string instrumentId="MES",
        CancellationToken token=default)
    {
        var queue=await candidates.GetAsync(instrumentId,token);
        foreach(var candidate in queue.Candidates)await RunCandidateAsync(candidate,token);
        return await DashboardAsync(instrumentId,token);
    }

    public async Task<ExploratoryPaperDashboard> DashboardAsync(string instrumentId="MES",
        CancellationToken token=default)
    {
        instrumentId=instrumentId.Trim().ToUpperInvariant();
        if(instrumentId!="MES")throw new ArgumentException("The first exploratory paper campaign lane is MES-only.");
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT CampaignJson FROM ExploratoryPaperCampaigns
            WHERE InstrumentId=$instrument ORDER BY StartedAtUtc DESC,CampaignId;
            """;command.Parameters.AddWithValue("$instrument",instrumentId);
        var values=new List<ExploratoryPaperCampaign>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<ExploratoryPaperCampaign>(reader.GetString(0))!);
        return new(instrumentId,values.Select(x=>$"{x.StrategyId}|{x.StrategyVersion}").Distinct().Count(),
            values.Count,values.Sum(x=>x.ResolvedExecutions),values,values.Any(x=>x.ResolvedExecutions>0));
    }

    public async Task<ExploratoryPaperCampaignDetail?> DetailAsync(string campaignId,CancellationToken token=default)
    {
        var campaign=await FindAsync(campaignId,token);if(campaign is null)return null;
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT ExecutionJson FROM ExploratoryPaperExecutions WHERE CampaignId=$id ORDER BY EntryTimeUtc,ExecutionId";
        command.Parameters.AddWithValue("$id",campaignId);var executions=new List<ExploratoryPaperExecution>();
        await using(var reader=await command.ExecuteReaderAsync(token))while(await reader.ReadAsync(token))
            executions.Add(JsonSerializer.Deserialize<ExploratoryPaperExecution>(reader.GetString(0))!);
        await using var supplements=connection.CreateCommand();supplements.CommandText="SELECT SupplementJson FROM ExploratoryPaperTelemetrySupplements WHERE CampaignId=$id";
        supplements.Parameters.AddWithValue("$id",campaignId);var enrichment=new Dictionary<string,TelemetrySupplement>(StringComparer.Ordinal);
        await using(var supplementReader=await supplements.ExecuteReaderAsync(token))while(await supplementReader.ReadAsync(token))
        {var value=JsonSerializer.Deserialize<TelemetrySupplement>(supplementReader.GetString(0))!;enrichment[value.ExecutionId]=value;}
        executions=executions.Select(x=>enrichment.TryGetValue(x.ExecutionId,out var value)?x with
            {TimeToMfeMilliseconds=value.TimeToMfeMilliseconds,TimeToMaeMilliseconds=value.TimeToMaeMilliseconds}:x).ToList();
        return new(campaign,executions);
    }

    private async Task<ExploratoryPaperCampaign> RunCandidateAsync(ExploratorySandboxCandidate candidate,
        CancellationToken token)
    {
        var source=await ReadSourceAsync(candidate,token);var sampleHashes=source.Samples.Select(x=>x.ContentHash).Order().ToArray();
        var campaignSeed=JsonSerializer.Serialize(new{Version,candidate.CandidateId,candidate.PatternTradeRunId,
            candidate.HypothesisId,SampleHashes=sampleHashes,Partition="Test"});
        var campaignId=$"EPC-{AgentTrainingDatasetBuilder.Hash(campaignSeed)[..32]}";
        var existing=await FindAsync(campaignId,token);if(existing is not null)
        {await EnrichExistingAsync(existing,source.Samples,token);return existing;}
        var strategy=await FreezeCandidateAsync(candidate,source.SourceAsOfUtc,token);
        var definition=instruments.GetAll().Single(x=>x.InstrumentId==candidate.InstrumentId);
        var executions=new List<ExploratoryPaperExecution>();
        foreach(var sample in source.Samples.Where(IsResolved).OrderBy(x=>x.EntryTimeUtc).ThenBy(x=>x.SampleId,StringComparer.Ordinal))
            executions.Add(await ExecutionAsync(campaignId,candidate,sample,definition,token));
        var metrics=Enumerable.Range(1,5).Select(quantity=>Metrics(executions,quantity)).ToArray();
        var lead=metrics[0];var status=Status(lead);var recommendation=Recommendation(status,lead);
        var now=DateTime.UtcNow;var contentSeed=JsonSerializer.Serialize(new{Version,campaignId,candidate.CandidateId,
            strategy.Definition.StrategyId,strategy.Definition.StrategyVersion,Executions=executions.Select(x=>x.ContentHash),metrics,status,recommendation});
        var campaign=new ExploratoryPaperCampaign(campaignId,candidate.CandidateId,strategy.Definition.StrategyId,
            strategy.Definition.StrategyVersion,candidate.InstrumentId,candidate.PatternTradeRunId,candidate.HypothesisId,
            "BlindHistoricalTestPartitionReplay",status,recommendation,now,now,source.Samples.Count,executions.Count,
            metrics,AgentTrainingDatasetBuilder.Hash(contentSeed));
        await PersistAsync(campaign,executions,token);return campaign;
    }

    private async Task<StrategyRegistryEntry> FreezeCandidateAsync(ExploratorySandboxCandidate candidate,
        DateTime sourceAsOfUtc,CancellationToken token)
    {
        var identity=AgentTrainingDatasetBuilder.Hash(JsonSerializer.Serialize(new{Version,candidate.CandidateId,
            candidate.HypothesisId,candidate.DirectionPolicy,candidate.EntryPolicy,candidate.StopPolicy,
            candidate.ExitPolicy,candidate.TargetR,candidate.MaximumHoldingMinutes}));
        var strategyId=$"T1-{candidate.ModuleId.ToUpperInvariant()}-{identity[..12]}";
        var strategyVersion=$"0.1.0-t1.{identity[..12].ToLowerInvariant()}";
        var definition=new ImmutableStrategyDefinition(strategyId,strategyVersion,candidate.ModuleId,
            $"MES Tier 1 · {candidate.ModuleId} · {candidate.EntryPolicy}","ExploratoryPaper",
            candidate.DirectionPolicy,JsonSerializer.Serialize(new{candidate.EntryPolicy}),
            JsonSerializer.Serialize(new{candidate.StopPolicy}),JsonSerializer.Serialize(new{candidate.TargetR}),
            JsonSerializer.Serialize(new{candidate.ExitPolicy,candidate.MaximumHoldingMinutes}),
            JsonSerializer.Serialize(new{ContractVariants=new[]{1,2,3,4,5},RealCapital=false}),
            JsonSerializer.Serialize(new{TestPartitionMayRewriteVersion=false,LiveRouting=false}),
            ["MES"],[],[new("PatternTradeHypothesis",candidate.HypothesisId,PatternTradeHypothesisEngine.Version,"Source",true)],
            [new("DevelopmentAdmission",candidate.CandidateId,candidate.PatternTradeRunId,sourceAsOfUtc)],
            new(candidate.PatternTradeRunId,"bar-derived-context","universal-pattern-modules","market-sequence-engine",
                Version,"one-minute-top-of-book-touch","pattern-trade-hypothesis-engine","legacy-utc-session","contract-resolver"),
            candidate.PatternTradeRunId,$"{candidate.PatternTradeRunId}:train-validation-only","PFA exploratory sandbox",
            sourceAsOfUtc,"Tier 1 definition; not eligible for strict SandboxService activation");
        var entry=await strategies.RegisterAsync(definition,token);
        if(entry.Status==StrategyRegistryStatus.Draft)
            entry=await strategies.TransitionAsync(strategyId,strategyVersion,StrategyRegistryStatus.FrozenResearch,
                "Tier 1 candidate frozen before blind test-partition replay","exploratory-paper-service",token);
        return entry;
    }

    private async Task<ExploratoryPaperExecution> ExecutionAsync(string campaignId,
        ExploratorySandboxCandidate candidate,PatternTradeHypothesisSample sample,InstrumentDefinition instrument,
        CancellationToken token)
    {
        var entry=sample.EntryPrice!.Value;var stop=sample.StopPrice!.Value;var risk=Math.Abs(entry-stop);
        var grossR=sample.GrossR!.Value;var netR=sample.NetR!.Value;var mfe=sample.MaximumFavorableExcursionR??0;
        var mae=sample.MaximumAdverseExcursionR??0;var times=await ExcursionTimesAsync(sample,risk,token);
        var costTicks=risk==0?0:Math.Max(0,(grossR-netR)*risk/instrument.TickSize);
        var variants=Enumerable.Range(1,5).Select(quantity=>new ExploratoryContractVariantResult(quantity,
            Round(netR*risk*instrument.PointValue*quantity),Round(mfe*risk*instrument.PointValue*quantity),
            Round(mae*risk*instrument.PointValue*quantity))).ToArray();
        var seed=JsonSerializer.Serialize(new{Version,campaignId,sample.ContentHash,variants,times,costTicks});
        return new($"EPE-{AgentTrainingDatasetBuilder.Hash(seed)[..32]}",campaignId,candidate.CandidateId,
            sample.SampleId,sample.ObservationId,sample.InstrumentId,sample.ContractId,sample.Direction,
            sample.DecisionTimeUtc,sample.EntryTimeUtc!.Value,entry,entry,stop,sample.TargetPrice!.Value,
            sample.ExitTimeUtc!.Value,sample.ExitPrice!.Value,sample.Outcome.ToString(),grossR,netR,mfe,mae,
            times.TimeToMfeMilliseconds,times.TimeToMaeMilliseconds,0,costTicks,null,null,null,null,null,
            "OneMinuteBarReplay;L1L2TelemetryUnavailable",variants,AgentTrainingDatasetBuilder.Hash(seed));
    }

    private async Task<(long? TimeToMfeMilliseconds,long? TimeToMaeMilliseconds)> ExcursionTimesAsync(
        PatternTradeHypothesisSample sample,decimal risk,CancellationToken token)
    {
        if(risk<=0)return(null,null);await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT CloseTimeUtc,High,Low FROM CanonicalResolvedResearchBars
            WHERE InstrumentId=$instrument AND Timeframe='1m'
              AND OpenTimeUtc>=$entry AND OpenTimeUtc<$exit
            ORDER BY OpenTimeUtc;
            """;Add(command,"$instrument",sample.InstrumentId);
        Add(command,"$entry",sample.EntryTimeUtc!.Value.ToString("O"));Add(command,"$exit",sample.ExitTimeUtc!.Value.ToString("O"));
        decimal maxFavorable=decimal.MinValue,maxAdverse=decimal.MinValue;long? mfeTime=null,maeTime=null;
        await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))
        {
            var close=ParseDate(reader.GetString(0));var high=ParseDecimal(reader.GetString(1));var low=ParseDecimal(reader.GetString(2));
            var favorable=sample.Direction.Equals("Bullish",StringComparison.OrdinalIgnoreCase)?high-sample.EntryPrice!.Value:sample.EntryPrice!.Value-low;
            var adverse=sample.Direction.Equals("Bullish",StringComparison.OrdinalIgnoreCase)?sample.EntryPrice!.Value-low:high-sample.EntryPrice!.Value;
            var elapsed=Math.Max(0,(long)(close-sample.EntryTimeUtc!.Value).TotalMilliseconds);
            if(favorable>maxFavorable){maxFavorable=favorable;mfeTime=elapsed;}if(adverse>maxAdverse){maxAdverse=adverse;maeTime=elapsed;}
        }
        return(mfeTime,maeTime);
    }

    private async Task EnrichExistingAsync(ExploratoryPaperCampaign campaign,
        IReadOnlyList<PatternTradeHypothesisSample> samples,CancellationToken token)
    {
        var detail=await DetailAsync(campaign.CampaignId,token);if(detail is null)return;
        var byId=samples.ToDictionary(x=>x.SampleId,StringComparer.Ordinal);var pending=new List<TelemetrySupplement>();
        foreach(var execution in detail.Executions.Where(x=>x.TimeToMfeMilliseconds is null||x.TimeToMaeMilliseconds is null))
        {
            if(!byId.TryGetValue(execution.SourceSampleId,out var sample)||!sample.EntryPrice.HasValue||!sample.StopPrice.HasValue)continue;
            var times=await ExcursionTimesAsync(sample,Math.Abs(sample.EntryPrice.Value-sample.StopPrice.Value),token);
            if(times.TimeToMfeMilliseconds is null&&times.TimeToMaeMilliseconds is null)continue;
            var hash=AgentTrainingDatasetBuilder.Hash(JsonSerializer.Serialize(new{Version,campaign.CampaignId,execution.ExecutionId,times}));
            pending.Add(new(campaign.CampaignId,execution.ExecutionId,times.TimeToMfeMilliseconds,times.TimeToMaeMilliseconds,hash));
        }
        if(pending.Count==0)return;await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        foreach(var value in pending)
        {
            await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
                INSERT OR IGNORE INTO ExploratoryPaperTelemetrySupplements
                (CampaignId,ExecutionId,TimeToMfeMilliseconds,TimeToMaeMilliseconds,ContentHash,SupplementJson)
                VALUES($campaign,$execution,$mfe,$mae,$hash,$json);
                """;Add(command,"$campaign",value.CampaignId);Add(command,"$execution",value.ExecutionId);
            Add(command,"$mfe",value.TimeToMfeMilliseconds);Add(command,"$mae",value.TimeToMaeMilliseconds);
            Add(command,"$hash",value.ContentHash);Add(command,"$json",JsonSerializer.Serialize(value));
            await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token);
    }

    private static ExploratoryContractMetrics Metrics(IReadOnlyList<ExploratoryPaperExecution> executions,int quantity)
    {
        var values=executions.OrderBy(x=>x.EntryTimeUtc).Select(x=>x.ContractVariants.Single(v=>v.Contracts==quantity).NetProfitLoss).ToArray();
        var positives=values.Where(x=>x>0).Sum();var losses=Math.Abs(values.Where(x=>x<0).Sum());
        decimal equity=0,peak=0,drawdown=0;foreach(var value in values){equity+=value;peak=Math.Max(peak,equity);drawdown=Math.Max(drawdown,peak-equity);}
        return new(quantity,values.Length,values.Count(x=>x>0),values.Length==0?0:Round(values.Count(x=>x>0)/(decimal)values.Length),
            executions.Count==0?0:Round(executions.Average(x=>x.NetR)),losses==0?(positives>0?999m:0m):Round(positives/losses),
            Round(values.Sum()),Round(drawdown),values.Length==0?0:values.Min(),values.Length==0?0:values.Max());
    }

    private static ExploratoryPaperCampaignStatus Status(ExploratoryContractMetrics metrics)=>
        metrics.Trades==0?ExploratoryPaperCampaignStatus.AwaitingBlindSamples:
        metrics.Trades>=100&&metrics.ProfitFactor<.5m?ExploratoryPaperCampaignStatus.Terminated:
        metrics.Trades>=30&&metrics.MeanNetR>0&&metrics.ProfitFactor>1.1m?ExploratoryPaperCampaignStatus.Tier2ReviewEligible:
        ExploratoryPaperCampaignStatus.AccumulatingProspectiveEvidence;

    private static string Recommendation(ExploratoryPaperCampaignStatus status,ExploratoryContractMetrics metrics)=>status switch
    {
        ExploratoryPaperCampaignStatus.Terminated=>"Automatically culled: profit factor remained below 0.50 over at least 100 blind executions.",
        ExploratoryPaperCampaignStatus.Tier2ReviewEligible=>"Blind replay survived the Tier 1 threshold; begin prospective observation without changing this version.",
        ExploratoryPaperCampaignStatus.AwaitingBlindSamples=>"No resolved withheld samples were available; await prospective MES triggers.",
        _=>$"Keep frozen and accumulate prospective evidence; blind sample size is {metrics.Trades}, below the 30-trade review floor."
    };

    private async Task<(DateTime SourceAsOfUtc,IReadOnlyList<PatternTradeHypothesisSample> Samples)> ReadSourceAsync(
        ExploratorySandboxCandidate candidate,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        DateTime sourceAsOf;await using(var run=connection.CreateCommand()){run.CommandText="SELECT AsOfUtc FROM PatternTradeResearchRuns WHERE RunId=$run";run.Parameters.AddWithValue("$run",candidate.PatternTradeRunId);var value=await run.ExecuteScalarAsync(token) as string??throw new KeyNotFoundException("Source research run was not found.");sourceAsOf=ParseDate(value);}
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT SampleJson FROM PatternTradeResearchSamples
            WHERE RunId=$run AND HypothesisId=$hypothesis AND Split='Test'
            ORDER BY SampleId;
            """;command.Parameters.AddWithValue("$run",candidate.PatternTradeRunId);command.Parameters.AddWithValue("$hypothesis",candidate.HypothesisId);
        var values=new List<PatternTradeHypothesisSample>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<PatternTradeHypothesisSample>(reader.GetString(0))!);
        return(sourceAsOf,values);
    }

    private async Task<ExploratoryPaperCampaign?> FindAsync(string campaignId,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT CampaignJson FROM ExploratoryPaperCampaigns WHERE CampaignId=$id";command.Parameters.AddWithValue("$id",campaignId);
        var value=await command.ExecuteScalarAsync(token) as string;return value is null?null:JsonSerializer.Deserialize<ExploratoryPaperCampaign>(value);
    }

    private async Task PersistAsync(ExploratoryPaperCampaign campaign,IReadOnlyList<ExploratoryPaperExecution> executions,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO ExploratoryPaperCampaigns
            (CampaignId,CandidateId,StrategyId,StrategyVersion,InstrumentId,SourcePatternTradeRunId,HypothesisId,Mode,Status,
             ExecutionCount,ContentHash,CampaignJson,StartedAtUtc,CompletedAtUtc,CanActivateStrategy,CanRouteToRealBroker)
            VALUES($id,$candidate,$strategy,$version,$instrument,$run,$hypothesis,$mode,$status,$count,$hash,$json,$started,$completed,0,0);
            """;Add(command,"$id",campaign.CampaignId);Add(command,"$candidate",campaign.CandidateId);Add(command,"$strategy",campaign.StrategyId);
            Add(command,"$version",campaign.StrategyVersion);Add(command,"$instrument",campaign.InstrumentId);Add(command,"$run",campaign.SourcePatternTradeRunId);
            Add(command,"$hypothesis",campaign.HypothesisId);Add(command,"$mode",campaign.Mode);Add(command,"$status",campaign.Status.ToString());
            Add(command,"$count",campaign.ResolvedExecutions);Add(command,"$hash",campaign.ContentHash);Add(command,"$json",JsonSerializer.Serialize(campaign));
            Add(command,"$started",campaign.StartedAtUtc.ToString("O"));Add(command,"$completed",campaign.CompletedAtUtc?.ToString("O"));await command.ExecuteNonQueryAsync(token);}
        foreach(var execution in executions){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO ExploratoryPaperExecutions
            (CampaignId,ExecutionId,SourceSampleId,ObservationId,EntryTimeUtc,ExitTimeUtc,Outcome,NetR,ContentHash,ExecutionJson)
            VALUES($campaign,$id,$sample,$observation,$entry,$exit,$outcome,$net,$hash,$json);
            """;Add(command,"$campaign",campaign.CampaignId);Add(command,"$id",execution.ExecutionId);Add(command,"$sample",execution.SourceSampleId);
            Add(command,"$observation",execution.ObservationId);Add(command,"$entry",execution.EntryTimeUtc.ToString("O"));Add(command,"$exit",execution.ExitTimeUtc.ToString("O"));
            Add(command,"$outcome",execution.Outcome);Add(command,"$net",execution.NetR);Add(command,"$hash",execution.ContentHash);Add(command,"$json",JsonSerializer.Serialize(execution));await command.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);
    }

    private static bool IsResolved(PatternTradeHypothesisSample sample)=>sample.EntryTimeUtc.HasValue&&sample.EntryPrice.HasValue&&
        sample.StopPrice.HasValue&&sample.TargetPrice.HasValue&&sample.ExitTimeUtc.HasValue&&sample.ExitPrice.HasValue&&
        sample.GrossR.HasValue&&sample.NetR.HasValue&&sample.Outcome is not(HypothesisExitOutcome.Ambiguous or HypothesisExitOutcome.NoEntry or HypothesisExitOutcome.InvalidRisk);
    private static DateTime ParseDate(string value)=>DateTime.Parse(value,null,DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static decimal ParseDecimal(string value)=>decimal.Parse(value,NumberStyles.Number,CultureInfo.InvariantCulture);
    private static decimal Round(decimal value)=>decimal.Round(value,4,MidpointRounding.AwayFromZero);
    private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}
