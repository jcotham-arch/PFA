using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Execution;

namespace PFA_FVG_Scanner.Data;

public sealed class ExecutionAmbiguityRepository:IExecutionAmbiguityRepository
{
    private readonly PfaDatabase _database;
    public ExecutionAmbiguityRepository(PfaDatabase database)=>_database=database;
    public async Task SaveAsync(ExecutionEvidenceRequest request,ExecutionAmbiguityResult result,CancellationToken token=default)
    {
        if(result.RequestId!=request.RequestId)throw new ArgumentException("Result/request identity mismatch.");
        if(result.UsedOptimisticFallback)throw new UnauthorizedAccessException("Optimistic ambiguity fallback is forbidden.");
        var requestJson=JsonSerializer.Serialize(request);var resultJson=JsonSerializer.Serialize(result);
        var hash=Hash(requestJson+resultJson);
        await using var connection=_database.CreateConnection();await connection.OpenAsync(token);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using(var check=connection.CreateCommand()){check.Transaction=transaction;check.CommandText="SELECT ContentHash FROM ExecutionAmbiguityResults WHERE ResultId=$id";check.Parameters.AddWithValue("$id",result.ResultId);var scalar=await check.ExecuteScalarAsync(token);if(scalar is not null&&Convert.ToString(scalar,CultureInfo.InvariantCulture)!=hash)throw new InvalidOperationException("Ambiguity results are immutable; use a new result version.");if(scalar is not null){await transaction.RollbackAsync(token);return;}}
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO ExecutionEvidenceRequests
                (RequestId,SubjectId,InstrumentId,Direction,WindowStartUtc,WindowEndUtc,StopPrice,
                 TargetPrice,OriginalResolution,ExecutionModelVersion,DataRevision,RequestJson,CreatedAtUtc)
            VALUES($id,$subject,$instrument,$direction,$start,$end,$stop,$target,$resolution,$model,$revision,$json,$created);
            INSERT INTO ExecutionAmbiguityResults
                (ResultId,RequestId,Chronology,ResolvedAtResolution,FirstEventTimeUtc,ResolutionEngineVersion,
                 ResultJson,ContentHash,CreatedAtUtc,UsedOptimisticFallback)
            VALUES($result,$id,$chronology,$resolvedAt,$firstEvent,$engine,$resultJson,$hash,$created,0);
            """;Add(command,"$id",request.RequestId);Add(command,"$subject",request.SubjectId);Add(command,"$instrument",request.InstrumentId);Add(command,"$direction",request.Direction.ToString());Add(command,"$start",request.WindowStartUtc.ToUniversalTime().ToString("O"));Add(command,"$end",request.WindowEndUtc.ToUniversalTime().ToString("O"));Add(command,"$stop",Format(request.StopPrice));Add(command,"$target",Format(request.TargetPrice));Add(command,"$resolution",request.OriginalResolution.ToString());Add(command,"$model",request.ExecutionModelVersion);Add(command,"$revision",request.DataRevision);Add(command,"$json",requestJson);Add(command,"$created",result.CreatedAtUtc.ToUniversalTime().ToString("O"));Add(command,"$result",result.ResultId);Add(command,"$chronology",result.Chronology.ToString());Add(command,"$resolvedAt",result.ResolvedAtResolution?.ToString());Add(command,"$firstEvent",result.FirstEventTimeUtc?.ToUniversalTime().ToString("O"));Add(command,"$engine",result.ResolutionEngineVersion);Add(command,"$resultJson",resultJson);Add(command,"$hash",hash);await command.ExecuteNonQueryAsync(token);}
        foreach(var attempt in result.Attempts.Select((value,index)=>(value,index))){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT INTO ExecutionResolutionAttempts
                (ResultId,Ordinal,Resolution,Result,Reason,SourceReferencesJson)
            VALUES($result,$ordinal,$resolution,$outcome,$reason,$sources);
            """;Add(command,"$result",result.ResultId);Add(command,"$ordinal",attempt.index+1);Add(command,"$resolution",attempt.value.Resolution.ToString());Add(command,"$outcome",attempt.value.Result.ToString());Add(command,"$reason",attempt.value.Reason);Add(command,"$sources",JsonSerializer.Serialize(attempt.value.SourceReferences));await command.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);
    }
    public async Task<ExecutionAmbiguityResult?> FindResultAsync(string resultId,CancellationToken token=default)
    {await using var connection=_database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT ResultJson FROM ExecutionAmbiguityResults WHERE ResultId=$id";Add(command,"$id",resultId);var json=Convert.ToString(await command.ExecuteScalarAsync(token),CultureInfo.InvariantCulture);return json is null?null:JsonSerializer.Deserialize<ExecutionAmbiguityResult>(json);}
    private static string Hash(string v)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(v)));private static string Format(decimal v)=>v.ToString("G29",CultureInfo.InvariantCulture);private static void Add(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);
}
