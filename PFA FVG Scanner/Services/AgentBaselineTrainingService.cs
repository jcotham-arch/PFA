using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;

namespace PFA_FVG_Scanner.Services;

public sealed class AgentBaselineTrainingService(PfaDatabase database)
{
    public const string Version = "grouped-mean-baseline-1.0.0";

    public async Task<AgentBaselineRun> TrainAsync(AgentBaselineTrainingRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DatasetId)) throw new ArgumentException("DatasetId is required.");
        if (request.TargetName != "directionalCloseTicks")
            throw new ArgumentException("The initial baseline supports directionalCloseTicks only.");
        var (datasetHash, rows) = await ReadAsync(request.DatasetId.Trim(), request.TargetName, token);
        var training = rows.Where(x => x.Split == "Train").ToArray();
        if (training.Length == 0) throw new InvalidOperationException("The dataset has no training examples.");
        var globalMean = training.Average(x => x.Actual);
        var groups = training.GroupBy(x => x.GroupKey, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Average(y => y.Actual), StringComparer.Ordinal);
        decimal Predict(Row row) => groups.TryGetValue(row.GroupKey, out var value) ? value : globalMean;
        var metrics = new[] { "Train", "Validation", "Test" }.Select(split =>
        {
            var population = rows.Where(x => x.Split == split).ToArray();
            if (population.Length == 0) return new AgentBaselineMetric(split, 0, 0, 0, 0, 0, 0);
            var predictions = population.Select(x => (Actual: x.Actual, Prediction: Predict(x))).ToArray();
            var mae = predictions.Average(x => Math.Abs(x.Actual - x.Prediction));
            var mse = predictions.Average(x => (x.Actual - x.Prediction) * (x.Actual - x.Prediction));
            var accuracy = predictions.Count(x => Math.Sign(x.Actual) == Math.Sign(x.Prediction)) /
                (decimal)predictions.Length;
            return new(split, population.Length, Round(mae), Round((decimal)Math.Sqrt((double)mse)),
                Round(accuracy), Round(predictions.Average(x => x.Actual)),
                Round(predictions.Average(x => x.Prediction)));
        }).ToArray();
        var seed = JsonSerializer.Serialize(new { Version,request.DatasetId,datasetHash,request.TargetName,
            Groups=groups.OrderBy(x=>x.Key),GlobalMean=globalMean,Metrics=metrics });
        var contentHash = AgentTrainingDatasetBuilder.Hash(seed);
        var run = new AgentBaselineRun($"ABR-{contentHash[..32]}", Version, request.DatasetId, datasetHash,
            request.TargetName, training.Length, groups.Count, metrics, DateTime.UtcNow, contentHash);
        await PersistAsync(run, token);
        return run;
    }

    public async Task<IReadOnlyList<AgentBaselineRun>> GetAllAsync(CancellationToken token = default)
    {
        await using var connection = database.CreateConnection(); await connection.OpenAsync(token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RunJson FROM AgentBaselineRuns ORDER BY TrainedAtUtc DESC";
        var values = new List<AgentBaselineRun>(); await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) values.Add(JsonSerializer.Deserialize<AgentBaselineRun>(reader.GetString(0))!);
        return values;
    }

    private async Task<(string DatasetHash,List<Row> Rows)> ReadAsync(string datasetId,string target,CancellationToken token)
    {
        await using var connection = database.CreateConnection(); await connection.OpenAsync(token);
        string? datasetHash;
        await using (var manifest = connection.CreateCommand())
        {
            manifest.CommandText = "SELECT ContentHash FROM AgentResearchDatasets WHERE DatasetId=$id";
            manifest.Parameters.AddWithValue("$id", datasetId); datasetHash = Convert.ToString(await manifest.ExecuteScalarAsync(token));
        }
        if (string.IsNullOrWhiteSpace(datasetHash)) throw new KeyNotFoundException($"Dataset '{datasetId}' was not found.");
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Split,InstrumentId,ModuleId,Direction,LabelJson
            FROM AgentResearchExamples WHERE DatasetId=$id ORDER BY EventTimeUtc,ExampleId;
            """;
        command.Parameters.AddWithValue("$id", datasetId); var values = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var labels = JsonSerializer.Deserialize<Dictionary<string,decimal>>(reader.GetString(4)) ?? [];
            if (!labels.TryGetValue(target, out var actual)) continue;
            values.Add(new(reader.GetString(0), $"{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetString(3)}", actual));
        }
        return (datasetHash, values);
    }

    private async Task PersistAsync(AgentBaselineRun run,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="""
            INSERT OR IGNORE INTO AgentBaselineRuns
            (RunId,ModelVersion,DatasetId,DatasetContentHash,TargetName,TrainingSamples,GroupCount,TrainedAtUtc,
             ContentHash,RunJson,CanActivateStrategy,CanRouteToRealBroker)
            VALUES($id,$version,$dataset,$datasetHash,$target,$samples,$groups,$trained,$hash,$json,0,0);
            """;
        command.Parameters.AddWithValue("$id",run.RunId);command.Parameters.AddWithValue("$version",run.ModelVersion);
        command.Parameters.AddWithValue("$dataset",run.DatasetId);command.Parameters.AddWithValue("$datasetHash",run.DatasetContentHash);
        command.Parameters.AddWithValue("$target",run.TargetName);command.Parameters.AddWithValue("$samples",run.TrainingSamples);
        command.Parameters.AddWithValue("$groups",run.GroupCount);command.Parameters.AddWithValue("$trained",run.TrainedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$hash",run.ContentHash);command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(run));
        await command.ExecuteNonQueryAsync(token);
    }

    private static decimal Round(decimal value)=>decimal.Round(value,6,MidpointRounding.AwayFromZero);
    private sealed record Row(string Split,string GroupKey,decimal Actual);
}
