using System.Globalization;
using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class PatternObservationResearchService(PfaDatabase database)
{
    public async Task<object?> GetAsync(string observationId,CancellationToken token=default)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var observationCommand=connection.CreateCommand();observationCommand.CommandText="""
            SELECT ObservationId,Revision,ModuleId,ModuleVersion,PatternType,InstrumentId,ContractId,Timeframe,
                   Direction,FormationTimeUtc,KnownAtUtc,LifecycleState,PayloadSchema,PayloadJson,
                   SourceReferencesJson,QualityFlags,ContentHash
            FROM UniversalMarketObservations WHERE ObservationId=$id ORDER BY Revision DESC LIMIT 1;
            """;observationCommand.Parameters.AddWithValue("$id",observationId);
        await using var observationReader=await observationCommand.ExecuteReaderAsync(token);if(!await observationReader.ReadAsync(token))return null;
        var observation=new
        {
            observationId=observationReader.GetString(0),revision=observationReader.GetInt32(1),moduleId=observationReader.GetString(2),
            moduleVersion=observationReader.GetString(3),patternType=observationReader.GetString(4),instrumentId=observationReader.GetString(5),
            contractId=observationReader.IsDBNull(6)?null:observationReader.GetString(6),timeframe=observationReader.GetString(7),
            direction=observationReader.GetString(8),formationTimeUtc=observationReader.GetString(9),knownAtUtc=observationReader.GetString(10),
            lifecycleState=observationReader.GetString(11),payloadSchema=observationReader.GetString(12),
            geometry=JsonDocument.Parse(observationReader.GetString(13)).RootElement.Clone(),
            sourceReferences=JsonSerializer.Deserialize<string[]>(observationReader.GetString(14))??[],
            qualityFlags=observationReader.GetInt32(15),contentHash=observationReader.GetString(16)
        };await observationReader.DisposeAsync();
        var scenarios=new List<object>();await using(var command=connection.CreateCommand())
        {
            command.CommandText="""
                SELECT s.SampleJson FROM PatternTradeResearchSamples s
                JOIN PatternTradeResearchRuns r ON r.RunId=s.RunId
                WHERE s.ObservationId=$id AND r.EngineVersion='pattern-trade-hypothesis-engine-1.3.0'
                  AND r.CreatedAtUtc=(SELECT MAX(r2.CreatedAtUtc) FROM PatternTradeResearchRuns r2 WHERE r2.EngineVersion='pattern-trade-hypothesis-engine-1.3.0')
                ORDER BY s.HypothesisId;
                """;command.Parameters.AddWithValue("$id",observationId);await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token)){var sample=JsonSerializer.Deserialize<PatternTradeHypothesisSample>(reader.GetString(0));if(sample is null)continue;scenarios.Add(new{sample.SampleId,sample.HypothesisId,sample.Direction,sample.EntryTimeUtc,sample.EntryPrice,sample.StopPrice,sample.TargetPrice,sample.ExitTimeUtc,sample.ExitPrice,Outcome=sample.Outcome.ToString(),sample.GrossR,sample.NetR,sample.MaximumFavorableExcursionR,sample.MaximumAdverseExcursionR,sample.Reason,Classification=Classify(sample)});}
        }
        var sequences=new List<object>();await using(var command=connection.CreateCommand())
        {command.CommandText="""
            SELECT i.SequenceInstanceId,i.SequenceDefinitionId,i.State,i.CurrentStageIndex,i.StartedAtUtc,i.UpdatedAtUtc,
                   i.PointInTimeConfidence,m.Role,m.Ordinal
            FROM MarketSequenceMembers m JOIN MarketSequenceInstances i ON i.SequenceInstanceId=m.SequenceInstanceId
            WHERE m.ObservationId=$id ORDER BY i.UpdatedAtUtc,i.SequenceInstanceId;
            """;command.Parameters.AddWithValue("$id",observationId);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))sequences.Add(new{sequenceInstanceId=reader.GetString(0),sequenceDefinitionId=reader.GetString(1),state=reader.GetString(2),currentStageIndex=reader.GetInt32(3),startedAtUtc=reader.GetString(4),updatedAtUtc=reader.GetString(5),pointInTimeConfidence=decimal.Parse(reader.GetString(6),CultureInfo.InvariantCulture),role=reader.GetString(7),ordinal=reader.GetInt32(8)});}
        var outcomes=new List<object>();await using(var command=connection.CreateCommand())
        {command.CommandText="SELECT OutcomeId,OutcomeVersion,EvaluatedThroughUtc,SamplesEvaluated,PayloadJson,QualityFlags FROM UniversalMarketOutcomes WHERE ObservationId=$id ORDER BY EvaluatedThroughUtc";command.Parameters.AddWithValue("$id",observationId);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))outcomes.Add(new{outcomeId=reader.GetString(0),outcomeVersion=reader.GetString(1),evaluatedThroughUtc=reader.GetString(2),samplesEvaluated=reader.GetInt32(3),payload=JsonDocument.Parse(reader.GetString(4)).RootElement.Clone(),qualityFlags=reader.GetInt32(5)});}
        return new{observation,scenarioCount=scenarios.Count,scenarios,sequences,outcomes,researchOnly=true};
    }

    private static string Classify(PatternTradeHypothesisSample sample)=>sample.Outcome switch
    {HypothesisExitOutcome.Ambiguous=>"Ambiguous",HypothesisExitOutcome.NoEntry or HypothesisExitOutcome.InvalidRisk=>"Unavailable",
     _ when sample.NetR>0=>"Good",_=>"Bad"};
}
