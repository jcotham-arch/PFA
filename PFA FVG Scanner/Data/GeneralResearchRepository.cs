using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Data;

public sealed class GeneralResearchRepository : IGeneralResearchRepository
{
    private readonly PfaDatabase _database;
    public GeneralResearchRepository(PfaDatabase database) => _database = database;
    public async Task SaveAsync(GeneralResearchRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.CanActivateStrategy || run.Hypotheses.Any(x => x.CanActivateStrategy))
            throw new UnauthorizedAccessException("Research cannot activate strategies.");
        if (run.SearchSpace.DeclaredCandidateCount != run.Hypotheses.Count)
            throw new InvalidOperationException("Every declared candidate must be retained as a hypothesis.");
        var hash = run.ContentHash();
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction; check.CommandText = "SELECT ContentHash FROM GeneralResearchRuns WHERE ResearchRunId=$id";
            check.Parameters.AddWithValue("$id", run.ResearchRunId);
            var scalar = await check.ExecuteScalarAsync(cancellationToken);
            if (scalar is not null && Convert.ToString(scalar, CultureInfo.InvariantCulture) != hash)
                throw new InvalidOperationException("Research runs are immutable; use a new ResearchRunId.");
            if (scalar is not null) { await transaction.RollbackAsync(cancellationToken); return; }
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO GeneralResearchRuns
                    (ResearchRunId,ResearchEngineVersion,Status,DatasetId,DatasetManifestJson,
                     SearchSpaceId,SearchSpaceVersion,SearchSpaceJson,DeclaredCandidateCount,
                     MultipleComparisonMethod,RandomSeed,PopulationJson,InputManifestJson,
                     ContentHash,CreatedAtUtc,CompletedAtUtc,FailureReason,CanActivateStrategy)
                VALUES ($id,$engine,$status,$dataset,$datasetJson,$space,$spaceVersion,$spaceJson,
                    $count,$comparison,$seed,$population,$input,$hash,$created,$completed,$failure,0);
                """;
            Add(command,"$id",run.ResearchRunId); Add(command,"$engine",run.ResearchEngineVersion);
            Add(command,"$status",run.Status.ToString()); Add(command,"$dataset",run.Dataset.DatasetId);
            Add(command,"$datasetJson",JsonSerializer.Serialize(run.Dataset)); Add(command,"$space",run.SearchSpace.SearchSpaceId);
            Add(command,"$spaceVersion",run.SearchSpace.Version); Add(command,"$spaceJson",JsonSerializer.Serialize(run.SearchSpace));
            Add(command,"$count",run.SearchSpace.DeclaredCandidateCount); Add(command,"$comparison",run.SearchSpace.MultipleComparisonMethod);
            Add(command,"$seed",run.SearchSpace.RandomSeed); Add(command,"$population",JsonSerializer.Serialize(run.Population));
            Add(command,"$input",run.InputManifestJson); Add(command,"$hash",hash);
            Add(command,"$created",run.CreatedAtUtc.ToUniversalTime().ToString("O"));
            Add(command,"$completed",run.CompletedAtUtc?.ToUniversalTime().ToString("O")); Add(command,"$failure",run.FailureReason);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var hypothesis in run.Hypotheses)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO GeneralResearchHypotheses
                    (HypothesisId,ResearchRunId,Signature,FamilyId,DefinitionJson,Status,
                     SampleSize,IndependentEvents,MetricsJson,SourceReference,CanActivateStrategy)
                VALUES ($id,$run,$signature,$family,$definition,$status,$sample,$events,$metrics,$source,0);
                """;
            Add(command,"$id",hypothesis.HypothesisId); Add(command,"$run",run.ResearchRunId);
            Add(command,"$signature",hypothesis.Signature); Add(command,"$family",hypothesis.FamilyId);
            Add(command,"$definition",hypothesis.DefinitionJson); Add(command,"$status",hypothesis.Status.ToString());
            Add(command,"$sample",hypothesis.SampleSize); Add(command,"$events",hypothesis.IndependentEvents);
            Add(command,"$metrics",JsonSerializer.Serialize(hypothesis.Metrics)); Add(command,"$source",hypothesis.SourceReference);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task<GeneralResearchRun?> FindAsync(string researchRunId, CancellationToken cancellationToken = default) =>
        (await ReadAsync(researchRunId, 1, cancellationToken)).SingleOrDefault();
    public Task<IReadOnlyList<GeneralResearchRun>> GetRecentAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        ReadAsync(null, Math.Clamp(limit,1,200), cancellationToken);
    private async Task<IReadOnlyList<GeneralResearchRun>> ReadAsync(string? id, int limit, CancellationToken token)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ResearchRunId,ResearchEngineVersion,Status,DatasetManifestJson,SearchSpaceJson,PopulationJson,InputManifestJson,CreatedAtUtc,CompletedAtUtc,FailureReason FROM GeneralResearchRuns WHERE ($id IS NULL OR ResearchRunId=$id) ORDER BY CreatedAtUtc DESC LIMIT $limit";
        Add(command,"$id",id); Add(command,"$limit",limit);
        var headers = new List<(string Id,string Engine,ResearchRunStatus Status,ResearchDatasetManifest Dataset,
            ResearchSearchSpace Search,ResearchPopulation Population,string Input,DateTime Created,DateTime? Completed,string? Failure)>();
        await using (var reader = await command.ExecuteReaderAsync(token))
        while (await reader.ReadAsync(token))
        {
            headers.Add((reader.GetString(0),reader.GetString(1),Enum.Parse<ResearchRunStatus>(reader.GetString(2)),
                JsonSerializer.Deserialize<ResearchDatasetManifest>(reader.GetString(3))!,JsonSerializer.Deserialize<ResearchSearchSpace>(reader.GetString(4))!,
                JsonSerializer.Deserialize<ResearchPopulation>(reader.GetString(5))!,reader.GetString(6),
                DateTime.Parse(reader.GetString(7),null,DateTimeStyles.RoundtripKind),reader.IsDBNull(8)?null:DateTime.Parse(reader.GetString(8),null,DateTimeStyles.RoundtripKind),reader.IsDBNull(9)?null:reader.GetString(9)));
        }
        var runs = new List<GeneralResearchRun>();
        foreach (var header in headers)
            runs.Add(new(header.Id,header.Engine,header.Status,header.Dataset,header.Search,header.Population,
                await ReadHypothesesAsync(connection,header.Id,token),header.Input,header.Created,header.Completed,header.Failure));
        return runs;
    }
    private static async Task<IReadOnlyList<ResearchHypothesis>> ReadHypothesesAsync(SqliteConnection connection,string runId,CancellationToken token)
    {
        await using var command=connection.CreateCommand(); command.CommandText="SELECT HypothesisId,Signature,FamilyId,DefinitionJson,Status,SampleSize,IndependentEvents,MetricsJson,SourceReference FROM GeneralResearchHypotheses WHERE ResearchRunId=$run ORDER BY rowid"; Add(command,"$run",runId);
        var values=new List<ResearchHypothesis>(); await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)) values.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),Enum.Parse<ResearchHypothesisStatus>(reader.GetString(4)),reader.GetInt32(5),reader.GetInt32(6),JsonSerializer.Deserialize<ResearchMetric[]>(reader.GetString(7))??[],reader.GetString(8),false)); return values;
    }
    private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}
