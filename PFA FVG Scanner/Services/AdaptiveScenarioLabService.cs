using System.Globalization;
using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Domain.Sandbox;

namespace PFA_FVG_Scanner.Services;

public sealed class AdaptiveScenarioLabService(PfaDatabase database,ExploratorySandboxCandidateService candidates,
    PatternTradeResearchService research)
{
    public const string PolicyVersion="mes-adaptive-scenario-lab-1.1.0";
    private const int MinimumDays=5;
    private const int MinimumTrades=50;
    private static readonly string[] RequiredTimeframes=["1m","5m","15m","1h"];

    public async Task<AdaptiveScenarioDashboard> DashboardAsync(string instrumentId="MES",CancellationToken token=default)
    {
        Validate(instrumentId);var history=await History(token);
        return new("MES",history.Count,history.FirstOrDefault(),history,await Evaluations(history.FirstOrDefault()?.GenerationId,token));
    }

    public async Task<AdaptiveScenarioDashboard> EvaluateLatestAsync(string instrumentId="MES",CancellationToken token=default)
    {
        Validate(instrumentId);var dashboard=await DashboardAsync("MES",token);var generation=dashboard.Latest;
        if(generation?.Champion is null)return dashboard;var existing=(dashboard.Evaluations??[]).Select(x=>x.ChallengerId).ToHashSet(StringComparer.Ordinal);
        foreach(var challenger in generation.Challengers.Where(x=>!existing.Contains(x.ChallengerId)))
        {
            var run=await research.RunAsync(new(generation.DevelopmentCutoffUtc,["MES"],[generation.Champion.ModuleId],
                [challenger.TargetR],[challenger.MaximumHoldingMinutes],1m,1m,[challenger.StopPolicy],
                [challenger.ExitPolicy],[challenger.EntryPolicy],5_000_000,true),token);
            var train=run.Summaries.Single(x=>x.Split=="Train");var validation=run.Summaries.Single(x=>x.Split=="Validation");
            var trainResolved=Resolved(train);var validationResolved=Resolved(validation);
            var result=trainResolved>=30&&validationResolved>=10&&train.MeanNetR>0&&validation.MeanNetR>0&&train.ProfitFactor>1&&validation.ProfitFactor>1
                ?"DevelopmentSurvivor":"DevelopmentRejected";
            var interpretation=result=="DevelopmentSurvivor"
                ?"The controlled mutation survived both development partitions. It remains unvalidated until frozen and replayed on a later untouched window."
                :"The controlled mutation failed at least one development partition and will not consume blind-test evidence.";
            var evaluated=DateTime.UtcNow;var seed=JsonSerializer.Serialize(new{generation.GenerationId,challenger.ChallengerId,run.RunId,result});
            var hash=AgentTrainingDatasetBuilder.Hash(seed);var value=new AdaptiveScenarioEvaluation($"ASE-{hash[..32]}",generation.GenerationId,
                challenger.ChallengerId,run.RunId,"MES",generation.Champion.ModuleId,trainResolved,train.MeanNetR,train.ProfitFactor,
                validationResolved,validation.MeanNetR,validation.ProfitFactor,result,interpretation,evaluated,hash);
            await PersistEvaluation(value,token);
        }
        return await DashboardAsync("MES",token);
    }

    public async Task<AdaptiveScenarioDashboard> GenerateAsync(string instrumentId="MES",CancellationToken token=default)
    {
        Validate(instrumentId);var queue=await candidates.GetAsync("MES",token);var history=await History(token);
        if(queue.Candidates.Count==0)return await PersistEmpty(history.Count+1,token);
        var analyses=new List<AdaptiveScenarioChampion>();
        foreach(var candidate in queue.Candidates)
        {
            var rows=await DevelopmentRows(candidate,token);if(rows.Count==0)continue;
            var segments=rows.GroupBy(x=>new{x.Timeframe,x.TradingDate}).Select(group=>Metrics(group.Key.Timeframe,group.Key.TradingDate,group.ToArray()))
                .OrderBy(x=>x.TradingDate,StringComparer.Ordinal).ThenBy(x=>x.Timeframe,StringComparer.Ordinal).ToArray();
            var resolved=rows.Where(x=>x.NetR.HasValue).ToArray();var metric=Aggregate(resolved);
            analyses.Add(new(candidate.CandidateId,candidate.PatternTradeRunId,candidate.HypothesisId,candidate.ModuleId,
                candidate.EntryPolicy,candidate.StopPolicy,candidate.ExitPolicy,candidate.TargetR,candidate.MaximumHoldingMinutes,
                resolved.Length,resolved.Select(x=>x.TradingDate).Distinct().Count(),resolved.Select(x=>x.Timeframe).Distinct().Order().ToArray(),
                metric.Mean,metric.Pf,segments));
        }
        var champion=analyses.OrderByDescending(x=>StabilityScore(x)).ThenByDescending(x=>x.DevelopmentTrades)
            .ThenBy(x=>x.CandidateId,StringComparer.Ordinal).FirstOrDefault();
        if(champion is null)return await PersistEmpty(history.Count+1,token);
        var challengers=CreateChallengers(champion);var allDates=await AllKnownDates(champion.PatternTradeRunId,token);
        var cutoff=allDates.Count==0?DateTime.UtcNow:allDates.Max();var next=DateOnly.FromDateTime(cutoff).AddDays(1);
        var status=champion.DevelopmentTrades>=MinimumTrades&&champion.DistinctDevelopmentDays>=MinimumDays&&
            RequiredTimeframes.All(x=>champion.Timeframes.Contains(x,StringComparer.OrdinalIgnoreCase))
                ?AdaptiveScenarioGenerationStatus.AwaitingNewBlindDays:AdaptiveScenarioGenerationStatus.AwaitingDevelopmentEvidence;
        var interpretation=status==AdaptiveScenarioGenerationStatus.AwaitingDevelopmentEvidence
            ?$"The strongest development candidate has {champion.DevelopmentTrades} resolved trades across {champion.DistinctDevelopmentDays} distinct day(s) and {champion.Timeframes.Count} source timeframe(s). It needs broader chronological coverage before a new blind version is frozen."
            :$"Development coverage passed. Freeze one challenger only after MES data dated {next:yyyy-MM-dd} or later is available as an untouched blind window.";
        var unchanged=history.FirstOrDefault();
        if(unchanged?.PolicyVersion==PolicyVersion&&unchanged.SourcePatternTradeRunId==champion.PatternTradeRunId&&
           unchanged.DevelopmentCutoffUtc==cutoff&&unchanged.Champion?.CandidateId==champion.CandidateId)
            return await DashboardAsync("MES",token);
        var created=DateTime.UtcNow;var seed=JsonSerializer.Serialize(new{PolicyVersion,Number=history.Count+1,champion,challengers,cutoff,next,status});
        var hash=AgentTrainingDatasetBuilder.Hash(seed);var generation=new AdaptiveScenarioGeneration($"ASG-{hash[..32]}",history.Count+1,"MES",
            PolicyVersion,champion.PatternTradeRunId,cutoff,next,status,interpretation,champion,challengers,MinimumDays,MinimumTrades,
            RequiredTimeframes,created,hash);
        await Persist(generation,token);return await DashboardAsync("MES",token);
    }

    private async Task<AdaptiveScenarioDashboard> PersistEmpty(int number,CancellationToken token)
    {
        var created=DateTime.UtcNow;var hash=AgentTrainingDatasetBuilder.Hash($"{PolicyVersion}|{number}|empty");
        var generation=new AdaptiveScenarioGeneration($"ASG-{hash[..32]}",number,"MES",PolicyVersion,"Unavailable",created,
            DateOnly.FromDateTime(created).AddDays(1),AdaptiveScenarioGenerationStatus.AwaitingDevelopmentEvidence,
            "No MES development-qualified candidate is available. Continue chronological research without opening the withheld Test partition.",
            null,[],MinimumDays,MinimumTrades,RequiredTimeframes,created,hash);
        await Persist(generation,token);return await DashboardAsync("MES",token);
    }

    private async Task<List<DevelopmentRow>> DevelopmentRows(ExploratorySandboxCandidate candidate,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT s.SampleJson,o.Timeframe,o.FormationTimeUtc
            FROM PatternTradeResearchSamples s
            JOIN UniversalMarketObservations o ON o.ObservationId=s.ObservationId
            WHERE s.RunId=$run AND s.HypothesisId=$hypothesis AND s.InstrumentId='MES'
              AND s.Split IN ('Train','Validation')
            ORDER BY o.FormationTimeUtc,s.SampleId;
            """;command.Parameters.AddWithValue("$run",candidate.PatternTradeRunId);command.Parameters.AddWithValue("$hypothesis",candidate.HypothesisId);
        var rows=new List<DevelopmentRow>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))
        {
            var sample=JsonSerializer.Deserialize<PatternTradeHypothesisSample>(reader.GetString(0))!;
            var time=Parse(reader.GetString(2));rows.Add(new(reader.GetString(1),time.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),sample.NetR));
        }
        return rows;
    }

    private static AdaptiveScenarioSegment Metrics(string timeframe,string date,IReadOnlyList<DevelopmentRow> rows)
    {var resolved=rows.Where(x=>x.NetR.HasValue).ToArray();var value=Aggregate(resolved);return new(timeframe,date,resolved.Length,
        resolved.Count(x=>x.NetR>0),resolved.Length==0?0:Round(resolved.Count(x=>x.NetR>0)/(decimal)resolved.Length),value.Mean,value.Pf);}

    private static (decimal Mean,decimal Pf) Aggregate(IReadOnlyList<DevelopmentRow> rows)
    {if(rows.Count==0)return(0,0);var wins=rows.Where(x=>x.NetR>0).Sum(x=>x.NetR!.Value);var losses=Math.Abs(rows.Where(x=>x.NetR<0).Sum(x=>x.NetR!.Value));
        return(Round(rows.Average(x=>x.NetR!.Value)),losses==0?(wins>0?999m:0m):Round(wins/losses));}

    private static decimal StabilityScore(AdaptiveScenarioChampion value)
    {var coverage=Math.Min(value.DistinctDevelopmentDays,MinimumDays)/(decimal)MinimumDays;
        var frames=RequiredTimeframes.Count(x=>value.Timeframes.Contains(x,StringComparer.OrdinalIgnoreCase))/(decimal)RequiredTimeframes.Length;
        return value.DevelopmentMeanNetR+Math.Min(value.DevelopmentProfitFactor,3m)/10m+coverage/10m+frames/10m;}

    private static IReadOnlyList<AdaptiveScenarioChallenger> CreateChallengers(AdaptiveScenarioChampion champion)
    {
        var values=new[]{
            ("target-neighbor",champion.EntryPolicy,champion.StopPolicy,champion.ExitPolicy,Math.Max(.25m,champion.TargetR-.25m),champion.MaximumHoldingMinutes,"Test a closer profit objective without changing entry or invalidation."),
            ("time-neighbor",champion.EntryPolicy,champion.StopPolicy,champion.ExitPolicy,champion.TargetR,Math.Max(5,champion.MaximumHoldingMinutes-15),"Test whether stalled trades should be aborted earlier."),
            ("exit-neighbor",champion.EntryPolicy,champion.StopPolicy,champion.ExitPolicy=="fixed-target-or-time"?"break-even-after-0.5r":"fixed-target-or-time",champion.TargetR,champion.MaximumHoldingMinutes,"Isolate the effect of exit management while holding setup recognition constant.")};
        return values.Select(x=>{var seed=$"{PolicyVersion}|{champion.CandidateId}|{x.Item1}|{x.Item2}|{x.Item3}|{x.Item4}|{x.Item5}|{x.Item6}";
            return new AdaptiveScenarioChallenger($"ASC-{AgentTrainingDatasetBuilder.Hash(seed)[..28]}",champion.CandidateId,x.Item1,x.Item7,x.Item2,x.Item3,x.Item4,x.Item5,x.Item6);}).ToArray();
    }

    private async Task<List<DateTime>> AllKnownDates(string runId,CancellationToken token)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="""
        SELECT o.FormationTimeUtc FROM PatternTradeResearchSamples s JOIN UniversalMarketObservations o ON o.ObservationId=s.ObservationId
        WHERE s.RunId=$run AND s.InstrumentId='MES' ORDER BY o.FormationTimeUtc;
        """;command.Parameters.AddWithValue("$run",runId);var values=new List<DateTime>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(Parse(reader.GetString(0)));return values;}

    private async Task<IReadOnlyList<AdaptiveScenarioGeneration>> History(CancellationToken token)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT GenerationJson FROM AdaptiveScenarioGenerations WHERE InstrumentId='MES' ORDER BY GenerationNumber DESC";
        var values=new List<AdaptiveScenarioGeneration>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<AdaptiveScenarioGeneration>(reader.GetString(0))!);return values;}

    private async Task<IReadOnlyList<AdaptiveScenarioEvaluation>> Evaluations(string? generationId,CancellationToken token)
    {if(generationId is null)return[];await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT EvaluationJson FROM AdaptiveScenarioEvaluations WHERE GenerationId=$generation ORDER BY EvaluatedAtUtc,ChallengerId";
        command.Parameters.AddWithValue("$generation",generationId);var values=new List<AdaptiveScenarioEvaluation>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<AdaptiveScenarioEvaluation>(reader.GetString(0))!);return values;}

    private async Task PersistEvaluation(AdaptiveScenarioEvaluation value,CancellationToken token)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="""
        INSERT OR IGNORE INTO AdaptiveScenarioEvaluations
        (EvaluationId,GenerationId,ChallengerId,ResearchRunId,Result,ContentHash,EvaluationJson,EvaluatedAtUtc,CanActivateStrategy,CanRouteToRealBroker)
        VALUES($id,$generation,$challenger,$run,$result,$hash,$json,$evaluated,0,0);
        """;command.Parameters.AddWithValue("$id",value.EvaluationId);command.Parameters.AddWithValue("$generation",value.GenerationId);
        command.Parameters.AddWithValue("$challenger",value.ChallengerId);command.Parameters.AddWithValue("$run",value.ResearchRunId);
        command.Parameters.AddWithValue("$result",value.Result);command.Parameters.AddWithValue("$hash",value.ContentHash);
        command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(value));command.Parameters.AddWithValue("$evaluated",value.EvaluatedAtUtc.ToString("O"));await command.ExecuteNonQueryAsync(token);}

    private async Task Persist(AdaptiveScenarioGeneration value,CancellationToken token)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="""
        INSERT OR IGNORE INTO AdaptiveScenarioGenerations
        (GenerationId,GenerationNumber,InstrumentId,SourcePatternTradeRunId,Status,DevelopmentCutoffUtc,EarliestNextBlindTradingDate,ContentHash,GenerationJson,CreatedAtUtc,CanActivateStrategy,CanRouteToRealBroker)
        VALUES($id,$number,'MES',$run,$status,$cutoff,$next,$hash,$json,$created,0,0);
        """;command.Parameters.AddWithValue("$id",value.GenerationId);command.Parameters.AddWithValue("$number",value.GenerationNumber);
        command.Parameters.AddWithValue("$run",value.SourcePatternTradeRunId);command.Parameters.AddWithValue("$status",value.Status.ToString());
        command.Parameters.AddWithValue("$cutoff",value.DevelopmentCutoffUtc.ToString("O"));command.Parameters.AddWithValue("$next",value.EarliestNextBlindTradingDate.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$hash",value.ContentHash);command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(value));command.Parameters.AddWithValue("$created",value.CreatedAtUtc.ToString("O"));await command.ExecuteNonQueryAsync(token);}

    private static void Validate(string instrumentId){if(!instrumentId.Trim().Equals("MES",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The first adaptive scenario lab is intentionally MES-only.");}
    private static DateTime Parse(string value)=>DateTime.Parse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static decimal Round(decimal value)=>decimal.Round(value,6,MidpointRounding.AwayFromZero);
    private static int Resolved(PatternTradeHypothesisSummary value)=>value.Samples-value.Ambiguous-value.NoEntryOrInvalid;
    private sealed record DevelopmentRow(string Timeframe,string TradingDate,decimal? NetR);
}
