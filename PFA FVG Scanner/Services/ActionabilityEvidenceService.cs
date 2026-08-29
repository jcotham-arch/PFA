using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class ActionabilityEvidenceService(PfaDatabase database,DailyMarketDiscoveryService discovery)
{
    public const string Version="universal-actionability-evidence-1.0.0";

    public async Task<ActionabilityDayReport> GetDayAsync(DateOnly date,CancellationToken token=default)
    {
        var start=date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc);var end=start.AddDays(1);
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        var patternRun=await LatestPatternRun(connection,token);var patternDefinitions=(patternRun?.Summaries??[])
            .GroupBy(x=>x.HypothesisId).ToDictionary(x=>x.Key,x=>x.First(),StringComparer.Ordinal);
        var patternSamples=await PatternSamples(connection,patternRun?.RunId,start,end,patternDefinitions,token);
        var records=await PatternRecords(connection,start,end,patternSamples,token);
        var sequenceSamples=await SequenceSamples(connection,start,end,patternDefinitions,token);
        records.AddRange(await SequenceRecords(connection,start,end,sequenceSamples,token));
        var daily=await discovery.StudyAsync(date,token);
        records.AddRange(daily.DiscoveredEvents.Select(ToStructuralRecord));
        var ordered=records.OrderBy(x=>x.RecognizedAtUtc).ThenBy(x=>x.RecordId,StringComparer.Ordinal).ToArray();
        var coverage=new ActionabilityCoverageSummary(ordered.Length,
            ordered.Count(x=>x.CoverageStatus==ActionabilityCoverageStatus.Evaluated),
            ordered.Count(x=>x.CoverageStatus==ActionabilityCoverageStatus.PartiallyEvaluated),
            ordered.Count(x=>x.CoverageStatus==ActionabilityCoverageStatus.AwaitingScenarioEvaluation),
            ordered.Sum(x=>x.Scenarios.Count),ordered.Sum(x=>x.Scenarios.Count(s=>s.EligibleForAgentTraining)));
        return new(date,DateTime.UtcNow,coverage,ordered,Version);
    }

    private static async Task<PatternTradeResearchRun?> LatestPatternRun(Microsoft.Data.Sqlite.SqliteConnection connection,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText="SELECT RunJson FROM PatternTradeResearchRuns WHERE EngineVersion=$version ORDER BY CreatedAtUtc DESC LIMIT 1";command.Parameters.AddWithValue("$version",PatternTradeHypothesisEngine.Version);var value=await command.ExecuteScalarAsync(token) as string;return value is null?null:JsonSerializer.Deserialize<PatternTradeResearchRun>(value);}

    private static async Task<Dictionary<string,List<ActionabilityScenario>>> PatternSamples(Microsoft.Data.Sqlite.SqliteConnection connection,string? runId,DateTime start,DateTime end,IReadOnlyDictionary<string,PatternTradeHypothesisSummary> definitions,CancellationToken token)
    {var values=new Dictionary<string,List<ActionabilityScenario>>(StringComparer.Ordinal);if(runId is null)return values;await using var command=connection.CreateCommand();command.CommandText="""
        SELECT s.ObservationId,s.SampleJson FROM PatternTradeResearchSamples s JOIN UniversalMarketObservations o ON o.ObservationId=s.ObservationId
        WHERE s.RunId=$run AND o.FormationTimeUtc >= $start AND o.FormationTimeUtc < $end ORDER BY s.ObservationId,s.HypothesisId;
        """;command.Parameters.AddWithValue("$run",runId);command.Parameters.AddWithValue("$start",start.ToString("O"));command.Parameters.AddWithValue("$end",end.ToString("O"));await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var sample=JsonSerializer.Deserialize<PatternTradeHypothesisSample>(reader.GetString(1));if(sample is null)continue;definitions.TryGetValue(sample.HypothesisId,out var definition);Add(values,reader.GetString(0),Scenario(sample,definition));}return values;}

    private static async Task<List<ActionabilityEvidenceRecord>> PatternRecords(Microsoft.Data.Sqlite.SqliteConnection connection,DateTime start,DateTime end,IReadOnlyDictionary<string,List<ActionabilityScenario>> samples,CancellationToken token)
    {var values=new List<ActionabilityEvidenceRecord>();await using var command=connection.CreateCommand();command.CommandText="""
        SELECT ObservationId,Revision,ModuleId,ModuleVersion,PatternType,InstrumentId,ContractId,Timeframe,Direction,
               FormationTimeUtc,KnownAtUtc,PayloadJson,ContentHash FROM UniversalMarketObservations
        WHERE FormationTimeUtc >= $start AND FormationTimeUtc < $end ORDER BY FormationTimeUtc,ObservationId;
        """;command.Parameters.AddWithValue("$start",start.ToString("O"));command.Parameters.AddWithValue("$end",end.ToString("O"));await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var id=reader.GetString(0);var scenarios=samples.GetValueOrDefault(id)??[];var status=Coverage(scenarios);values.Add(new($"ACT-PAT-{Hash(id)[..24]}",ActionabilitySubjectKind.Pattern,id,$"{reader.GetString(2)}:{reader.GetString(3)}:r{reader.GetInt32(1)}",reader.GetString(5),reader.IsDBNull(6)?null:reader.GetString(6),reader.GetString(7),reader.GetString(4),reader.GetString(8),Parse(reader.GetString(9)),Parse(reader.GetString(10)),status,JsonDocument.Parse(reader.GetString(11)).RootElement.Clone(),scenarios,Missing(status),reader.GetString(12)));}return values;}

    private static async Task<Dictionary<string,List<ActionabilityScenario>>> SequenceSamples(Microsoft.Data.Sqlite.SqliteConnection connection,DateTime start,DateTime end,IReadOnlyDictionary<string,PatternTradeHypothesisSummary> definitions,CancellationToken token)
    {var values=new Dictionary<string,List<ActionabilityScenario>>(StringComparer.Ordinal);await using var command=connection.CreateCommand();command.CommandText="""
        SELECT c.SequenceInstanceId,p.SampleJson FROM SequenceTradeResearchSamples c
        JOIN SequenceTradeResearchRuns r ON r.RunId=c.RunId
        JOIN PatternTradeResearchSamples p ON p.RunId=r.SourcePatternTradeRunId AND p.SampleId=c.SourceSampleId
        JOIN MarketSequenceInstances i ON i.SequenceInstanceId=c.SequenceInstanceId
        WHERE c.RunId=(SELECT RunId FROM SequenceTradeResearchRuns ORDER BY CreatedAtUtc DESC LIMIT 1)
          AND i.StartedAtUtc >= $start AND i.StartedAtUtc < $end ORDER BY c.SequenceInstanceId,c.HypothesisId;
        """;command.Parameters.AddWithValue("$start",start.ToString("O"));command.Parameters.AddWithValue("$end",end.ToString("O"));await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var sample=JsonSerializer.Deserialize<PatternTradeHypothesisSample>(reader.GetString(1));if(sample is null)continue;definitions.TryGetValue(sample.HypothesisId,out var definition);Add(values,reader.GetString(0),Scenario(sample,definition));}return values;}

    private static async Task<List<ActionabilityEvidenceRecord>> SequenceRecords(Microsoft.Data.Sqlite.SqliteConnection connection,DateTime start,DateTime end,IReadOnlyDictionary<string,List<ActionabilityScenario>> samples,CancellationToken token)
    {var values=new List<ActionabilityEvidenceRecord>();await using var command=connection.CreateCommand();command.CommandText="""
        SELECT i.SequenceInstanceId,i.SequenceDefinitionId,i.SequenceDefinitionVersion,i.InstrumentId,i.ContractId,i.Timeframe,
               i.State,i.StartedAtUtc,i.UpdatedAtUtc,i.PointInTimeConfidence,i.CurrentStageIndex,i.TerminationReason,d.DefinitionJson
        FROM MarketSequenceInstances i JOIN MarketSequenceDefinitions d ON d.SequenceDefinitionId=i.SequenceDefinitionId AND d.Version=i.SequenceDefinitionVersion
        WHERE i.StartedAtUtc >= $start AND i.StartedAtUtc < $end ORDER BY i.StartedAtUtc,i.SequenceInstanceId;
        """;command.Parameters.AddWithValue("$start",start.ToString("O"));command.Parameters.AddWithValue("$end",end.ToString("O"));await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var id=reader.GetString(0);var scenarios=samples.GetValueOrDefault(id)??[];var status=Coverage(scenarios);var facts=JsonSerializer.SerializeToElement(new{state=reader.GetString(6),pointInTimeConfidence=Decimal(reader.GetString(9)),currentStageIndex=reader.GetInt32(10),terminationReason=reader.IsDBNull(11)?null:reader.GetString(11),definition=JsonDocument.Parse(reader.GetString(12)).RootElement.Clone()});var hash=Hash($"{id}|{reader.GetString(8)}|{reader.GetString(6)}");values.Add(new($"ACT-SEQ-{Hash(id)[..24]}",ActionabilitySubjectKind.Sequence,id,$"{reader.GetString(1)}:{reader.GetString(2)}",reader.GetString(3),reader.IsDBNull(4)?null:reader.GetString(4),reader.GetString(5),reader.GetString(1),"Sequence",Parse(reader.GetString(7)),Parse(reader.GetString(8)),status,facts,scenarios,Missing(status),hash));}return values;}

    private static ActionabilityEvidenceRecord ToStructuralRecord(DailyDiscoveryEvent value)
    {var facts=JsonSerializer.SerializeToElement(new{value.Strength,value.Evidence,value.Open,value.High,value.Low,value.Close,value.Volume});return new($"ACT-EVT-{Hash(value.EventId)[..24]}",ActionabilitySubjectKind.StructuralEvent,value.EventId,"daily-discovery-1.0.0",value.Symbol,null,value.Timeframe,value.Type,value.Direction,value.TimeUtc,value.KnownAtUtc,ActionabilityCoverageStatus.AwaitingScenarioEvaluation,facts,[],Missing(ActionabilityCoverageStatus.AwaitingScenarioEvaluation),Hash(JsonSerializer.Serialize(value)));}

    private static ActionabilityScenario Scenario(PatternTradeHypothesisSample sample,PatternTradeHypothesisSummary? definition)
    {var classification=sample.Outcome switch{HypothesisExitOutcome.Ambiguous=>"Ambiguous",HypothesisExitOutcome.NoEntry or HypothesisExitOutcome.InvalidRisk=>"Unavailable",_ when sample.NetR>0=>"Good",_ when sample.NetR<0=>"Bad",_=>"Neutral"};var eligible=sample.NetR.HasValue&&sample.ExitTimeUtc.HasValue&&sample.EntryTimeUtc.HasValue&&sample.DecisionTimeUtc>=sample.EntryTimeUtc.Value.AddMinutes(-1)&&sample.Outcome is not(HypothesisExitOutcome.Ambiguous or HypothesisExitOutcome.NoEntry or HypothesisExitOutcome.InvalidRisk);return new(sample.SampleId,sample.HypothesisId,sample.Direction,definition?.DirectionPolicy.ToString(),definition?.EntryPolicy,definition?.StopPolicy,definition?.ExitPolicy,definition?.TargetR,definition?.MaximumHoldingMinutes,sample.DecisionTimeUtc,sample.EntryTimeUtc,sample.EntryPrice,sample.StopPrice,sample.TargetPrice,sample.ExitTimeUtc,sample.ExitPrice,sample.Outcome.ToString(),classification,sample.GrossR,sample.NetR,sample.MaximumFavorableExcursionR,sample.MaximumAdverseExcursionR,sample.Reason,eligible);}
    private static ActionabilityCoverageStatus Coverage(IReadOnlyList<ActionabilityScenario> scenarios)=>scenarios.Count==0?ActionabilityCoverageStatus.AwaitingScenarioEvaluation:scenarios.Any(x=>x.EligibleForAgentTraining)?ActionabilityCoverageStatus.Evaluated:ActionabilityCoverageStatus.PartiallyEvaluated;
    private static string[] Missing(ActionabilityCoverageStatus status)=>status switch{ActionabilityCoverageStatus.Evaluated=>[],ActionabilityCoverageStatus.PartiallyEvaluated=>["A valid entry/exit outcome for at least one interpretation"],_=>["Entry timing policy","Structural stop policy","Target policy","Exit/failure outcome","MFE/MAE path metrics"]};
    private static void Add(Dictionary<string,List<ActionabilityScenario>> values,string id,ActionabilityScenario scenario){if(!values.TryGetValue(id,out var rows))values[id]=rows=[];rows.Add(scenario);}
    private static DateTime Parse(string value)=>DateTime.Parse(value,null,DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static decimal Decimal(string value)=>decimal.Parse(value,CultureInfo.InvariantCulture);
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
