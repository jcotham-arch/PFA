using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Services;

public sealed class PatternTradeResearchService(PfaDatabase database,IInstrumentDefinitionRegistry instruments)
{
    private static readonly string[] SupportedModules=["liquidity-sweep","range-breakout","failed-breakout","market-structure"];

    public async Task<PatternTradeResearchRun> RunAsync(PatternTradeResearchRequest request,CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(request);var asOf=Utc(request.AsOfUtc);
        if(asOf==default)throw new ArgumentException("A non-default AsOfUtc is required.");
        var modules=(request.ModuleIds??SupportedModules).Select(Normalize).Distinct().Order().ToArray();
        if(modules.Length==0||modules.Any(x=>!SupportedModules.Contains(x,StringComparer.Ordinal)))
            throw new ArgumentException("Only liquidity-sweep, range-breakout, failed-breakout, and market-structure are supported.");
        var roots=(request.InstrumentIds??[]).Select(x=>x.Trim().ToUpperInvariant()).Where(x=>x.Length>0).Distinct().Order().ToArray();
        var targetRs=(request.TargetRs??[1m,2m,3m]).Distinct().Order().ToArray();
        var holds=(request.MaximumHoldingMinutes??[15,30,60]).Distinct().Order().ToArray();
        var requestedStops=request.StopPolicies?.Select(Normalize).Distinct().Order().ToArray();
        var entryPolicies=(request.EntryPolicies??["next-one-minute-open","one-minute-confirmation-close"]).Select(Normalize).Distinct().Order().ToArray();
        var exitPolicies=(request.ExitPolicies??["fixed-target-or-time","break-even-after-0.5r"]).Select(Normalize).Distinct().Order().ToArray();
        if(entryPolicies.Length==0||entryPolicies.Any(x=>x is not("next-one-minute-open" or "one-minute-confirmation-close" or "directional-confirmation-close" or "two-bar-confirmation-close")))
            throw new ArgumentException("Unknown entry policy.");
        if(requestedStops?.Any(x=>x is not("extreme-invalidation" or "boundary-invalidation" or "opposite-range-invalidation"))==true)
            throw new ArgumentException("Unknown stop policy.");
        if(exitPolicies.Length==0||exitPolicies.Any(x=>x is not("fixed-target-or-time" or "break-even-after-0.5r" or "break-even-after-1r" or "trail-half-r-after-1r" or "opposite-bar-close")))
            throw new ArgumentException("Unknown exit policy.");
        if(targetRs.Any(x=>x<=0)||holds.Any(x=>x<1)||request.StopBufferTicks<0||request.EstimatedRoundTripCostTicks<0)
            throw new ArgumentException("Targets and holding periods must be positive; costs and buffers cannot be negative.");
        if(request.MaximumScenarioEvaluations<1)throw new ArgumentException("MaximumScenarioEvaluations must be positive.");
        var observations=await ReadObservationsAsync(asOf,roots,modules,token);
        if(observations.Count==0)throw new InvalidOperationException("No eligible non-FVG observations were found.");
        var splitObservations=AssignSplits(observations);
        var bars=await ReadBarsAsync(splitObservations.Select(x=>x.Observation.InstrumentId).Distinct().ToArray(),
            splitObservations.Min(x=>x.Observation.KnownAtUtc).AddMinutes(-1),asOf,token);
        var definitions=Definitions(modules,targetRs,holds,request.StopBufferTicks,request.EstimatedRoundTripCostTicks,requestedStops,entryPolicies,exitPolicies);
        var estimated=(long)definitions.Count*splitObservations.Count;if(estimated>request.MaximumScenarioEvaluations)
            throw new InvalidOperationException($"The requested grid would evaluate {estimated:N0} scenarios, above the {request.MaximumScenarioEvaluations:N0} safety cap. Narrow the instruments, modules, entries, exits, targets, or holding periods.");
        var samples=new List<PatternTradeHypothesisSample>();var maxHold=holds.Max();
        foreach(var item in splitObservations)
        {
            var observation=item.Observation;var definition=instruments.Find(observation.InstrumentId,
                DateOnly.FromDateTime(observation.FormationTimeUtc));if(definition is null)continue;
            var window=Window(bars.GetValueOrDefault(observation.InstrumentId,[]),observation.KnownAtUtc,maxHold);
            foreach(var hypothesis in definitions.Where(x=>x.ModuleId==observation.ModuleId))
                samples.Add(PatternTradeHypothesisEngine.Evaluate(hypothesis,observation,window,definition.TickSize) with{Split=item.Split});
        }
        var summaries=Summarize(definitions,samples);var seed=JsonSerializer.Serialize(new
        {PatternTradeHypothesisEngine.Version,asOf,roots,modules,targetRs,holds,entryPolicies,exitPolicies,request.StopBufferTicks,request.MaximumScenarioEvaluations,
            request.EstimatedRoundTripCostTicks,Samples=samples.Select(x=>x.ContentHash),summaries});
        var hash=AgentTrainingDatasetBuilder.Hash(seed);var run=new PatternTradeResearchRun($"PTR-{hash[..32]}",
            PatternTradeHypothesisEngine.Version,asOf,splitObservations.Select(x=>x.Observation.InstrumentId).Distinct().Order().ToArray(),
            modules,splitObservations.Count,definitions.Count,samples.Count,summaries,hash,DateTime.UtcNow);
        await PersistAsync(run,samples,token);return run;
    }

    public async Task<IReadOnlyList<PatternTradeResearchRun>> GetAllAsync(CancellationToken token=default)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="SELECT RunJson FROM PatternTradeResearchRuns ORDER BY CreatedAtUtc DESC";
        var values=new List<PatternTradeResearchRun>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<PatternTradeResearchRun>(reader.GetString(0))!);return values;
    }

    private async Task<List<MarketPatternObservation>> ReadObservationsAsync(DateTime asOf,
        string[] roots,string[] modules,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        var rootFilter=roots.Length==0?"":$" AND InstrumentId IN ({string.Join(',',roots.Select((_,i)=>$"$root{i}"))})";
        command.CommandText=$"""
            SELECT ObservationId,ModuleId,ModuleVersion,PatternType,InstrumentId,ContractId,Timeframe,Direction,
                   FormationTimeUtc,KnownAtUtc,LifecycleState,PayloadJson,QualityFlags
            FROM UniversalMarketObservations
            WHERE KnownAtUtc<=$asOf AND ModuleId IN ({string.Join(',',modules.Select((_,i)=>$"$module{i}"))}) {rootFilter}
            ORDER BY FormationTimeUtc,ObservationId;
            """;command.Parameters.AddWithValue("$asOf",asOf.ToString("O"));
        for(var i=0;i<modules.Length;i++)command.Parameters.AddWithValue($"$module{i}",modules[i]);
        for(var i=0;i<roots.Length;i++)command.Parameters.AddWithValue($"$root{i}",roots[i]);
        var values=new List<MarketPatternObservation>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))
        {
            using var payload=JsonDocument.Parse(reader.GetString(11));
            values.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),
                reader.IsDBNull(5)?null:reader.GetString(5),reader.GetString(6),Enum.Parse<PatternDirection>(reader.GetString(7),true),
                Parse(reader.GetString(8)),Parse(reader.GetString(9)),Enum.Parse<PatternLifecycleState>(reader.GetString(10),true),
                new JsonPatternGeometry(payload.RootElement.Clone()),[],(MarketDataQualityFlags)reader.GetInt32(12)));
        }
        return values;
    }

    private static List<(MarketPatternObservation Observation,string Split)> AssignSplits(IReadOnlyList<MarketPatternObservation> values)=>
        values.GroupBy(x=>$"{x.InstrumentId}|{x.ModuleId}",StringComparer.Ordinal).SelectMany(group=>
        {
            var ordered=group.OrderBy(x=>x.FormationTimeUtc).ThenBy(x=>x.ObservationId).ToArray();
            var train=(int)Math.Floor(ordered.Length*.70m);var validation=(int)Math.Floor(ordered.Length*.85m);
            return ordered.Select((observation,index)=>(observation,index<train?"Train":index<validation?"Validation":"Test"));
        }).OrderBy(x=>x.observation.FormationTimeUtc).Select(x=>(x.observation,x.Item2)).ToList();

    private async Task<Dictionary<string,CanonicalBar[]>> ReadBarsAsync(string[] roots,DateTime start,DateTime end,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText=$"""
            SELECT CanonicalBarId,InstrumentId,Timeframe,OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume
            FROM CanonicalResolvedResearchBars WHERE Timeframe='1m' AND CloseTimeUtc>=$start AND OpenTimeUtc<=$end
              AND InstrumentId IN ({string.Join(',',roots.Select((_,i)=>$"$root{i}"))}) ORDER BY InstrumentId,OpenTimeUtc;
            """;command.Parameters.AddWithValue("$start",start.ToString("O"));command.Parameters.AddWithValue("$end",end.ToString("O"));
        for(var i=0;i<roots.Length;i++)command.Parameters.AddWithValue($"$root{i}",roots[i]);
        var values=new List<CanonicalBar>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)){var open=Parse(reader.GetString(3));values.Add(new(reader.GetString(0),1,reader.GetString(1),null,
            reader.GetString(1),reader.GetString(2),open,Parse(reader.GetString(4)),Decimal(reader.GetString(5)),Decimal(reader.GetString(6)),
            Decimal(reader.GetString(7)),Decimal(reader.GetString(8)),Decimal(reader.GetString(9)),true,"RESEARCH",DateOnly.FromDateTime(open),
            "resolved-research-1.0.0","revision-one",CorrectionState.Original,MarketDataQualityFlags.None,open,""));}
        return values.GroupBy(x=>x.InstrumentId,StringComparer.Ordinal).ToDictionary(x=>x.Key,x=>x.ToArray(),StringComparer.Ordinal);
    }

    private static IReadOnlyList<CanonicalBar> Window(CanonicalBar[] bars,DateTime decision,int maxHold)
    {var start=Array.FindIndex(bars,x=>x.CloseTimeUtc>decision);if(start<0)return[];var end=start;var cutoff=decision.AddMinutes(maxHold+2);while(end<bars.Length&&bars[end].OpenTimeUtc<cutoff)end++;return bars[start..end];}

    private static List<PatternTradeHypothesisDefinition> Definitions(string[] modules,decimal[] targets,int[] holds,decimal buffer,decimal costs,string[]? requestedStops,string[] entryPolicies,string[] exitPolicies)
    {
        var values=new List<PatternTradeHypothesisDefinition>();foreach(var module in modules)
        {
            var policies=module is "failed-breakout" or "market-structure"?new[]{HypothesisDirectionPolicy.PatternDirection,HypothesisDirectionPolicy.OpposePatternDirection}:
                new[]{HypothesisDirectionPolicy.PatternDirection};
            foreach(var policy in policies)foreach(var entry in entryPolicies)
            foreach(var stop in requestedStops??Stops(module,policy))foreach(var exit in exitPolicies)foreach(var target in targets)foreach(var hold in holds)
            {var signature=$"{module}|{policy}|{entry}|{stop}|{exit}|{target:G29}|{hold}|{buffer:G29}|{costs:G29}";var hash=AgentTrainingDatasetBuilder.Hash(signature);
                values.Add(new($"PTH-{hash[..24]}","1.3.0",module,policy,entry,stop,target,hold,buffer,costs,exit));}
        }return values;
    }

    private static string[] Stops(string module,HypothesisDirectionPolicy policy)=>module switch
    {"liquidity-sweep"=>["extreme-invalidation","boundary-invalidation"],
     "range-breakout"=>["boundary-invalidation","opposite-range-invalidation"],
     "failed-breakout" when policy==HypothesisDirectionPolicy.OpposePatternDirection=>["extreme-invalidation","boundary-invalidation"],
     _=>["boundary-invalidation","opposite-range-invalidation"]};

    private static IReadOnlyList<PatternTradeHypothesisSummary> Summarize(IReadOnlyList<PatternTradeHypothesisDefinition> definitions,
        IReadOnlyList<PatternTradeHypothesisSample> samples)=>definitions.SelectMany(definition=>new[]{"Train","Validation","Test"}.Select(split=>
    {
        var rows=samples.Where(x=>x.HypothesisId==definition.HypothesisId&&x.Split==split).ToArray();var resolved=rows.Where(x=>x.NetR.HasValue).ToArray();
        var wins=resolved.Where(x=>x.NetR>0).Sum(x=>x.NetR!.Value);var losses=Math.Abs(resolved.Where(x=>x.NetR<0).Sum(x=>x.NetR!.Value));
        decimal peak=0,equity=0,drawdown=0;foreach(var row in resolved.OrderBy(x=>x.EntryTimeUtc)){equity+=row.NetR!.Value;peak=Math.Max(peak,equity);drawdown=Math.Max(drawdown,peak-equity);}
        return new PatternTradeHypothesisSummary(definition.HypothesisId,definition.ModuleId,definition.EntryPolicy,definition.StopPolicy,definition.DirectionPolicy,definition.TargetR,
            definition.MaximumHoldingMinutes,split,rows.Length,rows.Count(x=>x.Outcome==HypothesisExitOutcome.Target),
            rows.Count(x=>x.Outcome==HypothesisExitOutcome.Stop),rows.Count(x=>x.Outcome==HypothesisExitOutcome.TimeExit),
            rows.Count(x=>x.Outcome==HypothesisExitOutcome.Ambiguous),rows.Count(x=>x.Outcome is HypothesisExitOutcome.NoEntry or HypothesisExitOutcome.InvalidRisk),
            resolved.Length==0?0:Round(resolved.Average(x=>x.NetR!.Value)),resolved.Length==0?0:Round(resolved.Count(x=>x.NetR>0)/(decimal)resolved.Length),
            losses==0?(wins>0?999m:0m):Round(wins/losses),Round(drawdown),false,definition.ExitPolicy,
            rows.Count(x=>x.Outcome==HypothesisExitOutcome.BreakEven));
    })).ToArray();

    private async Task PersistAsync(PatternTradeResearchRun run,IReadOnlyList<PatternTradeHypothesisSample> samples,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="INSERT OR IGNORE INTO PatternTradeResearchRuns(RunId,EngineVersion,AsOfUtc,ObservationCount,HypothesisCount,SampleCount,ContentHash,RunJson,CreatedAtUtc,CanActivateStrategy,CanRouteToRealBroker) VALUES($id,$version,$asOf,$observations,$hypotheses,$samples,$hash,$json,$created,0,0)";
            Add(command,"$id",run.RunId);Add(command,"$version",run.EngineVersion);Add(command,"$asOf",run.AsOfUtc.ToString("O"));Add(command,"$observations",run.ObservationCount);Add(command,"$hypotheses",run.HypothesisCount);Add(command,"$samples",run.SampleCount);Add(command,"$hash",run.ContentHash);Add(command,"$json",JsonSerializer.Serialize(run));Add(command,"$created",run.CreatedAtUtc.ToString("O"));await command.ExecuteNonQueryAsync(token);}
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="INSERT OR IGNORE INTO PatternTradeResearchSamples(RunId,SampleId,HypothesisId,ObservationId,InstrumentId,ModuleId,Split,Outcome,NetR,ContentHash,SampleJson) VALUES($run,$sample,$hypothesis,$observation,$instrument,$module,$split,$outcome,$net,$hash,$json)";
            var runParameter=command.Parameters.Add("$run",SqliteType.Text);var sampleParameter=command.Parameters.Add("$sample",SqliteType.Text);
            var hypothesisParameter=command.Parameters.Add("$hypothesis",SqliteType.Text);var observationParameter=command.Parameters.Add("$observation",SqliteType.Text);
            var instrumentParameter=command.Parameters.Add("$instrument",SqliteType.Text);var moduleParameter=command.Parameters.Add("$module",SqliteType.Text);
            var splitParameter=command.Parameters.Add("$split",SqliteType.Text);var outcomeParameter=command.Parameters.Add("$outcome",SqliteType.Text);
            var netParameter=command.Parameters.Add("$net",SqliteType.Text);var hashParameter=command.Parameters.Add("$hash",SqliteType.Text);
            var jsonParameter=command.Parameters.Add("$json",SqliteType.Text);command.Prepare();
            foreach(var sample in samples){runParameter.Value=run.RunId;sampleParameter.Value=sample.SampleId;hypothesisParameter.Value=sample.HypothesisId;
                observationParameter.Value=sample.ObservationId;instrumentParameter.Value=sample.InstrumentId;moduleParameter.Value=sample.ModuleId;
                splitParameter.Value=sample.Split;outcomeParameter.Value=sample.Outcome.ToString();netParameter.Value=(object?)sample.NetR??DBNull.Value;
                hashParameter.Value=sample.ContentHash;jsonParameter.Value=JsonSerializer.Serialize(sample);await command.ExecuteNonQueryAsync(token);}}
        await transaction.CommitAsync(token);
    }

    private static string Normalize(string value)=>value.Trim().ToLowerInvariant();
    private static DateTime Parse(string value)=>DateTime.Parse(value,null,DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static decimal Decimal(string value)=>decimal.Parse(value,NumberStyles.Number,CultureInfo.InvariantCulture);
    private static DateTime Utc(DateTime value)=>value.Kind switch{DateTimeKind.Utc=>value,DateTimeKind.Unspecified=>DateTime.SpecifyKind(value,DateTimeKind.Utc),_=>value.ToUniversalTime()};
    private static decimal Round(decimal value)=>decimal.Round(value,6,MidpointRounding.AwayFromZero);
    private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}
