using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Certification;

namespace PFA_FVG_Scanner.Data;

public sealed class CertificationCampaignRepository(PfaDatabase database)
{
    public async Task SaveAsync(CertificationCampaignRequest request,CertificationCampaignResult result,
        CancellationToken token=default)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var tx=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using(var existing=connection.CreateCommand())
        {
            existing.Transaction=tx;existing.CommandText="SELECT ContentHash FROM CertificationCampaigns WHERE CampaignId=$id";
            Add(existing,"$id",result.CampaignId);var hash=await existing.ExecuteScalarAsync(token) as string;
            if(hash is not null&&hash!=result.ContentHash)throw new InvalidOperationException("Campaign identity conflicts with an existing immutable campaign.");
        }
        await using(var command=connection.CreateCommand())
        {
            command.Transaction=tx;command.CommandText="""
                INSERT OR IGNORE INTO CertificationCampaigns
                (CampaignId,StrategyId,StrategyVersion,EvidenceRevision,CreatedAtUtc,ContentHash,CampaignJson,CanPromoteStrategy,CanRouteToRealBroker)
                VALUES($id,$strategy,$version,$evidence,$created,$hash,$json,0,0);
                """;
            Add(command,"$id",result.CampaignId);Add(command,"$strategy",result.StrategyId);Add(command,"$version",result.StrategyVersion);
            Add(command,"$evidence",result.EvidenceRevision);Add(command,"$created",result.CreatedAtUtc.ToUniversalTime().ToString("O"));
            Add(command,"$hash",result.ContentHash);Add(command,"$json",JsonSerializer.Serialize(request));await command.ExecuteNonQueryAsync(token);
        }
        foreach(var pack in request.RulePacks)
        {await using var command=connection.CreateCommand();command.Transaction=tx;command.CommandText="""
            INSERT OR IGNORE INTO CertificationRulePacks
            (CampaignId,RulePackHash,FirmId,ProgramId,RuleVersion,SourceReference,SourceContentHash,IsOfficiallyVerified,RulePackJson)
            VALUES($campaign,$hash,$firm,$program,$version,$source,$sourceHash,$verified,$json);
            """;Add(command,"$campaign",result.CampaignId);Add(command,"$hash",pack.ContentHash());Add(command,"$firm",pack.FirmId);Add(command,"$program",pack.ProgramId);Add(command,"$version",pack.RuleVersion);Add(command,"$source",pack.SourceReference);Add(command,"$sourceHash",pack.SourceContentHash);Add(command,"$verified",pack.IsOfficiallyVerified?1:0);Add(command,"$json",JsonSerializer.Serialize(pack));await command.ExecuteNonQueryAsync(token);}
        foreach(var item in result.Results)
        {await using var command=connection.CreateCommand();command.Transaction=tx;command.CommandText="""
            INSERT OR IGNORE INTO CertificationResults
            (CampaignId,ResultId,RulePackHash,Status,EvaluatedAtUtc,ContentHash,ResultJson,CanPromoteStrategy,CanRouteToRealBroker)
            VALUES($campaign,$id,$pack,$status,$evaluated,$hash,$json,0,0);
            """;Add(command,"$campaign",result.CampaignId);Add(command,"$id",item.ResultId);Add(command,"$pack",item.RulePackHash);Add(command,"$status",item.Status.ToString());Add(command,"$evaluated",item.EvaluatedAtUtc.ToUniversalTime().ToString("O"));Add(command,"$hash",item.ContentHash);Add(command,"$json",JsonSerializer.Serialize(item));await command.ExecuteNonQueryAsync(token);}
        await tx.CommitAsync(token);
    }

    private static void Add(SqliteCommand command,string name,object value)=>command.Parameters.AddWithValue(name,value);
}
