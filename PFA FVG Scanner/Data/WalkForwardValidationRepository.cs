using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Validation;

namespace PFA_FVG_Scanner.Data;

public interface IWalkForwardValidationRepository
{
    Task SaveAsync(WalkForwardPlan plan,WalkForwardValidationReport report,CancellationToken token=default);
    Task<WalkForwardValidationReport?> FindReportAsync(string reportId,CancellationToken token=default);
    Task<WalkForwardPlan?> FindPlanAsync(string planId,CancellationToken token=default);
}

public sealed class WalkForwardValidationRepository:IWalkForwardValidationRepository
{
    private readonly PfaDatabase _database;public WalkForwardValidationRepository(PfaDatabase database)=>_database=database;
    public async Task SaveAsync(WalkForwardPlan plan,WalkForwardValidationReport report,CancellationToken token=default)
    {
        if(report.PlanId!=plan.PlanId)throw new ArgumentException("Report/plan identity mismatch.");
        if(report.CanActivateStrategy||report.Folds.Any(x=>x.CanActivateStrategy))throw new UnauthorizedAccessException("Walk-forward evidence cannot activate a strategy.");
        var planJson=JsonSerializer.Serialize(plan);var reportJson=JsonSerializer.Serialize(report);
        await using var connection=_database.CreateConnection();await connection.OpenAsync(token);await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using(var check=connection.CreateCommand()){check.Transaction=transaction;check.CommandText="SELECT ContentHash FROM WalkForwardReports WHERE ReportId=$id";Add(check,"$id",report.ReportId);var scalar=await check.ExecuteScalarAsync(token);if(scalar is not null&&Convert.ToString(scalar,CultureInfo.InvariantCulture)!=report.ContentHash)throw new InvalidOperationException("Walk-forward reports are immutable; use a new dataset revision or plan.");if(scalar is not null){await transaction.RollbackAsync(token);return;}}
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="""
            INSERT INTO WalkForwardPlans(PlanId,PlanVersion,FrozenSignature,FrozenParameterHash,DatasetId,DataRevision,PlanJson,CreatedAtUtc)
            VALUES($plan,$version,$signature,$parameters,$dataset,$revision,$json,$created)
            ON CONFLICT(PlanId) DO NOTHING;
            INSERT INTO WalkForwardReports(ReportId,PlanId,Status,ContentHash,ReportJson,CreatedAtUtc,CanActivateStrategy)
            VALUES($report,$plan,$status,$hash,$reportJson,$created,0);
            """;Add(command,"$plan",plan.PlanId);Add(command,"$version",plan.PlanVersion);Add(command,"$signature",plan.FrozenSignature);Add(command,"$parameters",plan.FrozenParameterHash);Add(command,"$dataset",plan.DatasetId);Add(command,"$revision",plan.DataRevision);Add(command,"$json",planJson);Add(command,"$created",report.CreatedAtUtc.ToString("O"));Add(command,"$report",report.ReportId);Add(command,"$status",report.Status.ToString());Add(command,"$hash",report.ContentHash);Add(command,"$reportJson",reportJson);await command.ExecuteNonQueryAsync(token);}
        foreach(var fold in plan.Folds){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO WalkForwardFolds(PlanId,FoldId,Ordinal,TrainingStartUtc,TrainingEndUtc,ValidationStartUtc,ValidationEndUtc,DatasetId,DataRevision)
            VALUES($plan,$fold,$ordinal,$trainStart,$trainEnd,$validationStart,$validationEnd,$dataset,$revision)
            """;Add(command,"$plan",plan.PlanId);Add(command,"$fold",fold.FoldId);Add(command,"$ordinal",fold.Ordinal);Add(command,"$trainStart",fold.TrainingStartUtc.ToString("O"));Add(command,"$trainEnd",fold.TrainingEndUtc.ToString("O"));Add(command,"$validationStart",fold.ValidationStartUtc.ToString("O"));Add(command,"$validationEnd",fold.ValidationEndUtc.ToString("O"));Add(command,"$dataset",fold.DatasetId);Add(command,"$revision",fold.DataRevision);await command.ExecuteNonQueryAsync(token);}
        foreach(var result in report.Folds){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT INTO WalkForwardFoldResults(ReportId,FoldId,Status,Samples,IndependentEvents,ExpectancyR,ProfitFactor,MaximumDrawdownR,ObservationContentHash,ParameterDriftDetected,CanActivateStrategy)
            VALUES($report,$fold,$status,$samples,$independent,$expectancy,$profit,$drawdown,$hash,$drift,0)
            """;Add(command,"$report",report.ReportId);Add(command,"$fold",result.FoldId);Add(command,"$status",result.Status.ToString());Add(command,"$samples",result.Samples);Add(command,"$independent",result.IndependentEvents);Add(command,"$expectancy",Format(result.ExpectancyR));Add(command,"$profit",Format(result.ProfitFactor));Add(command,"$drawdown",Format(result.MaximumDrawdownR));Add(command,"$hash",result.ObservationContentHash);Add(command,"$drift",result.ParameterDriftDetected?1:0);await command.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);
    }
    public async Task<WalkForwardValidationReport?> FindReportAsync(string reportId,CancellationToken token=default)=>await FindAsync<WalkForwardValidationReport>("SELECT ReportJson FROM WalkForwardReports WHERE ReportId=$id",reportId,token);
    public async Task<WalkForwardPlan?> FindPlanAsync(string planId,CancellationToken token=default)=>await FindAsync<WalkForwardPlan>("SELECT PlanJson FROM WalkForwardPlans WHERE PlanId=$id",planId,token);
    private async Task<T?> FindAsync<T>(string sql,string id,CancellationToken token){await using var connection=_database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText=sql;Add(command,"$id",id);var scalar=await command.ExecuteScalarAsync(token);return scalar is null or DBNull?default:JsonSerializer.Deserialize<T>(Convert.ToString(scalar,CultureInfo.InvariantCulture)!);}
    private static string Format(decimal value)=>value.ToString("G29",CultureInfo.InvariantCulture);private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}
