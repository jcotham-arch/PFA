using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Historical;

namespace PFA_FVG_Scanner.Data;

public sealed class HistoricalPipelineRepository
{
    private readonly PfaDatabase _database;
    public HistoricalPipelineRepository(PfaDatabase database) => _database = database;

    public async Task<HistoricalJobSnapshot> CreateAsync(HistoricalDatasetPlan plan, DateTime nowUtc, CancellationToken token = default)
    {
        var jobId = $"HJOB-{plan.PlanId}"; var json = JsonSerializer.Serialize(plan); var now = Utc(nowUtc).ToString("O");
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction; existing.CommandText = "SELECT PlanJson FROM HistoricalPipelineJobs WHERE PlanId=$plan"; Add(existing, "$plan", plan.PlanId);
            var scalar = await existing.ExecuteScalarAsync(token);
            var stored = scalar is null or DBNull ? null : Convert.ToString(scalar, CultureInfo.InvariantCulture);
            if (stored is not null && !EquivalentPlan(stored,plan)) throw new InvalidOperationException("Historical plans are immutable; use a new plan identity.");
            if (stored is not null) { await transaction.RollbackAsync(token); return (await FindAsync(jobId, token))!; }
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction; command.CommandText = "INSERT INTO HistoricalPipelineJobs(JobId,PlanId,Status,PlanJson,CreatedAtUtc,UpdatedAtUtc) VALUES($job,$plan,$status,$json,$now,$now)";
            Add(command, "$job", jobId); Add(command, "$plan", plan.PlanId); Add(command, "$status", HistoricalJobStatus.Draft.ToString()); Add(command, "$json", json); Add(command, "$now", now); await command.ExecuteNonQueryAsync(token);
        }
        foreach (var window in plan.Windows)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = """
                INSERT INTO HistoricalPipelineCheckpoints(JobId,WorkId,InstrumentId,ProviderSymbol,WindowStartUtc,WindowEndUtc,Status,AttemptCount,UpdatedAtUtc)
                VALUES($job,$work,$instrument,$symbol,$start,$end,$status,0,$now)
                """;
            Add(command, "$job", jobId); Add(command, "$work", window.WorkId); Add(command, "$instrument", window.InstrumentId); Add(command, "$symbol", window.ProviderSymbol);
            Add(command, "$start", window.StartUtc.ToString("O")); Add(command, "$end", window.EndUtc.ToString("O")); Add(command, "$status", HistoricalWorkStatus.Pending.ToString()); Add(command, "$now", now); await command.ExecuteNonQueryAsync(token);
        }
        await transaction.CommitAsync(token); return (await FindAsync(jobId, token))!;
    }

    public async Task<HistoricalJobSnapshot?> FindAsync(string jobId, CancellationToken token = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT Status,PlanJson,CreatedAtUtc,UpdatedAtUtc FROM HistoricalPipelineJobs WHERE JobId=$job"; Add(command, "$job", jobId);
        await using var reader = await command.ExecuteReaderAsync(token); if (!await reader.ReadAsync(token)) return null;
        var status = Enum.Parse<HistoricalJobStatus>(reader.GetString(0)); var plan = JsonSerializer.Deserialize<HistoricalDatasetPlan>(reader.GetString(1))!;
        var created = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); var updated = DateTime.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); await reader.DisposeAsync();
        var checkpoints = await LoadCheckpointsAsync(connection, jobId, plan, token); var manifest = await LoadManifestAsync(connection, jobId, token);
        return new(jobId, status, plan, checkpoints, manifest, created, updated);
    }

    public Task SetJobStatusAsync(string jobId, HistoricalJobStatus status, DateTime nowUtc, CancellationToken token = default) => ExecuteAsync(
        "UPDATE HistoricalPipelineJobs SET Status=$status,UpdatedAtUtc=$now WHERE JobId=$job", jobId, null, status.ToString(), null, null, nowUtc, token);

    public Task MarkRunningAsync(string jobId, string workId, DateTime nowUtc, CancellationToken token = default) => ExecuteAsync(
        "UPDATE HistoricalPipelineCheckpoints SET Status=$status,AttemptCount=AttemptCount+1,LastError=NULL,UpdatedAtUtc=$now WHERE JobId=$job AND WorkId=$work",
        jobId, workId, HistoricalWorkStatus.Running.ToString(), null, null, nowUtc, token);

    public async Task CompleteAsync(string jobId,HistoricalWorkWindow window,string sourceResolution,string rebuildResolution,HistoricalWindowResult result,DateTime nowUtc,CancellationToken token=default)
    {
        await using var connection=_database.CreateConnection();await connection.OpenAsync(token);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="UPDATE HistoricalPipelineCheckpoints SET Status=$status,ResultJson=$result,LastError=NULL,UpdatedAtUtc=$now WHERE JobId=$job AND WorkId=$work";Add(command,"$status",HistoricalWorkStatus.Completed.ToString());Add(command,"$result",JsonSerializer.Serialize(result));Add(command,"$now",Utc(nowUtc).ToString("O"));Add(command,"$job",jobId);Add(command,"$work",window.WorkId);await command.ExecuteNonQueryAsync(token);}
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="""
            INSERT INTO HistoricalCoverageRecords(JobId,WorkId,InstrumentId,ProviderSymbol,InstrumentDefinitionVersion,SourceResolution,RebuildResolution,
                WindowStartUtc,WindowEndUtc,StartTradingSessionId,EndTradingSessionId,BarsReturned,BarsSaved,RebuiltCandles,QualityIssueCount,UpdatedAtUtc)
            VALUES($job,$work,$instrument,$symbol,$definition,$source,$rebuild,$start,$end,$startSession,$endSession,$returned,$saved,$rebuilt,$quality,$now)
            ON CONFLICT(JobId,WorkId) DO UPDATE SET BarsReturned=excluded.BarsReturned,BarsSaved=excluded.BarsSaved,
                RebuiltCandles=excluded.RebuiltCandles,QualityIssueCount=excluded.QualityIssueCount,UpdatedAtUtc=excluded.UpdatedAtUtc
            """;Add(command,"$job",jobId);Add(command,"$work",window.WorkId);Add(command,"$instrument",window.InstrumentId);Add(command,"$symbol",window.ProviderSymbol);Add(command,"$definition",window.InstrumentDefinitionVersion);Add(command,"$source",sourceResolution);Add(command,"$rebuild",rebuildResolution);Add(command,"$start",window.StartUtc.ToString("O"));Add(command,"$end",window.EndUtc.ToString("O"));Add(command,"$startSession",window.StartTradingSessionId);Add(command,"$endSession",window.EndTradingSessionId);Add(command,"$returned",result.BarsReturned);Add(command,"$saved",result.BarsSaved);Add(command,"$rebuilt",result.RebuiltCandles);Add(command,"$quality",result.QualityIssueCount);Add(command,"$now",Utc(nowUtc).ToString("O"));await command.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);
    }

    public Task FailAsync(string jobId, string workId, string error, DateTime nowUtc, CancellationToken token = default) => ExecuteAsync(
        "UPDATE HistoricalPipelineCheckpoints SET Status=$status,LastError=$error,UpdatedAtUtc=$now WHERE JobId=$job AND WorkId=$work",
        jobId, workId, HistoricalWorkStatus.Failed.ToString(), null, error, nowUtc, token);

    public async Task<string> BeginRunAsync(string jobId,DateTime nowUtc,CancellationToken token=default)
    {var runId=$"HRUN-{Guid.NewGuid():N}";await using var connection=_database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="INSERT INTO HistoricalPipelineRuns(RunId,JobId,Status,StartedAtUtc) VALUES($run,$job,$status,$now)";Add(command,"$run",runId);Add(command,"$job",jobId);Add(command,"$status",HistoricalJobStatus.Running.ToString());Add(command,"$now",Utc(nowUtc).ToString("O"));await command.ExecuteNonQueryAsync(token);return runId;}
    public async Task EndRunAsync(string runId,HistoricalJobStatus status,string? failureReason,DateTime nowUtc,CancellationToken token=default)
    {await using var connection=_database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="UPDATE HistoricalPipelineRuns SET Status=$status,CompletedAtUtc=$now,FailureReason=$error WHERE RunId=$run";Add(command,"$status",status.ToString());Add(command,"$now",Utc(nowUtc).ToString("O"));Add(command,"$error",failureReason);Add(command,"$run",runId);await command.ExecuteNonQueryAsync(token);}

    public async Task<HistoricalJobSnapshot> FinalizeAsync(string jobId, DateTime nowUtc, CancellationToken token = default)
    {
        var job = await FindAsync(jobId, token) ?? throw new KeyNotFoundException(jobId);
        var completed = job.Checkpoints.Count(x => x.Status == HistoricalWorkStatus.Completed); var failed = job.Checkpoints.Count(x => x.Status == HistoricalWorkStatus.Failed);
        var status = failed == 0 && completed == job.Checkpoints.Count ? HistoricalJobStatus.Completed : completed > 0 ? HistoricalJobStatus.PartiallyCompleted : HistoricalJobStatus.Failed;
        var results = job.Checkpoints.Where(x => x.Result is not null).Select(x => x.Result!).ToArray();
        var instruments = job.Plan.Windows.Select(x => x.InstrumentId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var identity = JsonSerializer.Serialize(new { job.JobId, job.Plan.PlanId, Status = status.ToString(), completed, failed,
            Bars = results.Sum(x => x.BarsSaved), Rebuilt = results.Sum(x => x.RebuiltCandles), Quality = results.Sum(x => x.QualityIssueCount), Instruments = instruments, job.Plan.SessionAssignmentVersion });
        var hash = HistoricalUniversePlanner.Hex(identity); var manifest = new HistoricalDatasetManifest($"HDM-{hash[..32]}", jobId, job.Plan.PlanId, status.ToString(), job.Checkpoints.Count,
            completed, failed, results.Sum(x => x.BarsSaved), results.Sum(x => x.RebuiltCandles), results.Sum(x => x.QualityIssueCount), instruments, job.Plan.SessionAssignmentVersion, hash, Utc(nowUtc));
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(token); await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using (var command = connection.CreateCommand()) { command.Transaction = transaction; command.CommandText = "UPDATE HistoricalPipelineJobs SET Status=$status,UpdatedAtUtc=$now WHERE JobId=$job"; Add(command,"$status",status.ToString());Add(command,"$now",Utc(nowUtc).ToString("O"));Add(command,"$job",jobId);await command.ExecuteNonQueryAsync(token); }
        await using (var command = connection.CreateCommand()) { command.Transaction = transaction; command.CommandText = "INSERT OR REPLACE INTO HistoricalDatasetManifests(ManifestId,JobId,PlanId,Status,ContentHash,ManifestJson,CreatedAtUtc) VALUES($id,$job,$plan,$status,$hash,$json,$now)";Add(command,"$id",manifest.ManifestId);Add(command,"$job",jobId);Add(command,"$plan",job.Plan.PlanId);Add(command,"$status",status.ToString());Add(command,"$hash",hash);Add(command,"$json",JsonSerializer.Serialize(manifest));Add(command,"$now",manifest.CreatedAtUtc.ToString("O"));await command.ExecuteNonQueryAsync(token); }
        await transaction.CommitAsync(token); return (await FindAsync(jobId, token))!;
    }

    private async Task ExecuteAsync(string sql, string jobId, string? workId, string status, string? result, string? error, DateTime nowUtc, CancellationToken token)
    { await using var connection=_database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText=sql;Add(command,"$job",jobId);Add(command,"$work",workId);Add(command,"$status",status);Add(command,"$result",result);Add(command,"$error",error);Add(command,"$now",Utc(nowUtc).ToString("O"));await command.ExecuteNonQueryAsync(token); }
    private static async Task<IReadOnlyList<HistoricalWorkCheckpoint>> LoadCheckpointsAsync(SqliteConnection connection,string jobId,HistoricalDatasetPlan plan,CancellationToken token)
    {var list=new List<HistoricalWorkCheckpoint>();await using var command=connection.CreateCommand();command.CommandText="SELECT WorkId,Status,AttemptCount,ResultJson,LastError,UpdatedAtUtc FROM HistoricalPipelineCheckpoints WHERE JobId=$job ORDER BY InstrumentId,WindowStartUtc";Add(command,"$job",jobId);await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var work=plan.Windows.Single(x=>x.WorkId==reader.GetString(0));var result=reader.IsDBNull(3)?null:JsonSerializer.Deserialize<HistoricalWindowResult>(reader.GetString(3));list.Add(new(jobId,work,Enum.Parse<HistoricalWorkStatus>(reader.GetString(1)),reader.GetInt32(2),result,reader.IsDBNull(4)?null:reader.GetString(4),DateTime.Parse(reader.GetString(5),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)));}return list;}
    private static async Task<HistoricalDatasetManifest?> LoadManifestAsync(SqliteConnection connection,string jobId,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText="SELECT ManifestJson FROM HistoricalDatasetManifests WHERE JobId=$job";Add(command,"$job",jobId);var scalar=await command.ExecuteScalarAsync(token);var json=scalar is null or DBNull?null:Convert.ToString(scalar,CultureInfo.InvariantCulture);return json is null?null:JsonSerializer.Deserialize<HistoricalDatasetManifest>(json);}
    private static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(value,DateTimeKind.Utc):value.ToUniversalTime();
    private static bool EquivalentPlan(string storedJson,HistoricalDatasetPlan plan)
    {var stored=JsonSerializer.Deserialize<HistoricalDatasetPlan>(storedJson);return stored is not null&&JsonSerializer.Serialize(stored with{CreatedAtUtc=plan.CreatedAtUtc})==JsonSerializer.Serialize(plan);}
    private static void Add(SqliteCommand command,string name,object? value){if(!command.Parameters.Contains(name))command.Parameters.AddWithValue(name,value??DBNull.Value);}
}
