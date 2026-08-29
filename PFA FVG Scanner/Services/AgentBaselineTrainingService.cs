using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;

namespace PFA_FVG_Scanner.Services;

public sealed class AgentBaselineTrainingService(PfaDatabase database)
{
    public const string Version = "research-promotion-gate-2.0.0";

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
        var instrumentMeans = training.GroupBy(x => x.InstrumentId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Average(y => y.Actual), StringComparer.Ordinal);
        var moduleMeans = training.GroupBy(x => x.ModuleId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Average(y => y.Actual), StringComparer.Ordinal);
        var ridge = FitRidge(training, 1m);
        var ridgeBase = FitRidge(training,1m,name=>!IsResearchContextFeature(name));
        var ridgeContext = FitRidge(training,1m,IsResearchContextFeature);
        var familyModels=ContextFamilies.ToDictionary(x=>x.Key,x=>FitRidge(training,1m,
            name=>!IsResearchContextFeature(name)||name.StartsWith(x.Value,StringComparison.Ordinal)),StringComparer.Ordinal);
        var boostedStumps = FitBoostedStumps(training, 25, 0.10m);
        decimal Predict(Row row) => groups.TryGetValue(row.GroupKey, out var value) ? value : globalMean;
        AgentBaselineMetric Evaluate(string split, IReadOnlyList<Row> population, Func<Row,decimal> predictor)
        {
            if (population.Count == 0) return new AgentBaselineMetric(split, 0, 0, 0, 0, 0, 0);
            var predictions = population.Select(x => (Actual: x.Actual, Prediction: predictor(x))).ToArray();
            var mae = predictions.Average(x => Math.Abs(x.Actual - x.Prediction));
            var mse = predictions.Average(x => (x.Actual - x.Prediction) * (x.Actual - x.Prediction));
            var accuracy = predictions.Count(x => Math.Sign(x.Actual) == Math.Sign(x.Prediction)) /
                (decimal)predictions.Length;
            return new(split, population.Count, Round(mae), Round((decimal)Math.Sqrt((double)mse)),
                Round(accuracy), Round(predictions.Average(x => x.Actual)),
                Round(predictions.Average(x => x.Prediction)));
        }
        var metrics = new[] { "Train", "Validation", "Test" }
            .Select(split => Evaluate(split, rows.Where(x => x.Split == split).ToArray(), Predict)).ToArray();
        var segmentMetrics = rows.Select(x => x.InstrumentId).Distinct().Order()
            .SelectMany(instrument => new[] { "Validation", "Test" }.Select(split =>
            {
                var metric = Evaluate(split, rows.Where(x => x.Split == split && x.InstrumentId == instrument).ToArray(), Predict);
                return new AgentBaselineSegmentMetric(instrument, split, metric.SampleCount, metric.MeanAbsoluteError,
                    metric.RootMeanSquaredError, metric.DirectionalAccuracy, metric.MeanActual, metric.MeanPrediction);
            })).ToArray();
        var variants = new (string Name,Func<Row,decimal> Predict)[]
        {
            ("zero", _ => 0m),
            ("global-mean", _ => globalMean),
            ("instrument-mean", row => instrumentMeans.GetValueOrDefault(row.InstrumentId, globalMean)),
            ("module-mean", row => moduleMeans.GetValueOrDefault(row.ModuleId, globalMean)),
            ("instrument-module-direction-mean", Predict),
            ("ridge-base-only",ridgeBase.Predict),
            ("ridge-context-only",ridgeContext.Predict),
            ("ridge-linear", ridge.Predict),
            ("boosted-stumps", boostedStumps.Predict)
        };
        var variantMetrics = variants.SelectMany(variant => new[] { "Validation", "Test" }.Select(split =>
        {
            var metric = Evaluate(split, rows.Where(x => x.Split == split).ToArray(), variant.Predict);
            return new AgentBaselineVariantMetric(variant.Name, split, metric.SampleCount, metric.MeanAbsoluteError,
                metric.RootMeanSquaredError, metric.DirectionalAccuracy);
        })).ToArray();
        var contextAblations=rows.Select(x=>x.ModuleId).Distinct(StringComparer.Ordinal).Order()
            .Select(module=>
            {
                var population=rows.Where(x=>x.Split=="Test"&&x.ModuleId==module).ToArray();
                var baseMetric=Evaluate("Test",population,ridgeBase.Predict);
                var contextMetric=Evaluate("Test",population,ridgeContext.Predict);
                var combinedMetric=Evaluate("Test",population,ridge.Predict);
                return new AgentContextAblationMetric(module,population.Length,baseMetric.MeanAbsoluteError,
                    contextMetric.MeanAbsoluteError,combinedMetric.MeanAbsoluteError,baseMetric.DirectionalAccuracy,
                    contextMetric.DirectionalAccuracy,combinedMetric.DirectionalAccuracy,
                    Round(combinedMetric.DirectionalAccuracy-baseMetric.DirectionalAccuracy));
            }).ToArray();
        var contextFamilyAblations=rows.Select(x=>x.ModuleId).Distinct(StringComparer.Ordinal).Order()
            .SelectMany(module=>ContextFamilies.Keys.Select(family=>
            {
                var population=rows.Where(x=>x.Split=="Test"&&x.ModuleId==module).ToArray();
                var baseMetric=Evaluate("Test",population,ridgeBase.Predict);
                var familyMetric=Evaluate("Test",population,familyModels[family].Predict);
                return new AgentContextFamilyAblationMetric(module,family,population.Length,
                    baseMetric.MeanAbsoluteError,familyMetric.MeanAbsoluteError,baseMetric.DirectionalAccuracy,
                    familyMetric.DirectionalAccuracy,Round(familyMetric.DirectionalAccuracy-baseMetric.DirectionalAccuracy));
            })).ToArray();
        var walkForwardMetrics = BuildWalkForwardMetrics(rows, 15);
        var promotionGate = BuildPromotionGate(variantMetrics, segmentMetrics, walkForwardMetrics);
        var seed = JsonSerializer.Serialize(new { Version,request.DatasetId,datasetHash,request.TargetName,
            Groups=groups.OrderBy(x=>x.Key),GlobalMean=globalMean,Metrics=metrics,SegmentMetrics=segmentMetrics,
            VariantMetrics=variantMetrics,WalkForwardMetrics=walkForwardMetrics,PromotionGate=promotionGate,
            ContextAblations=contextAblations,ContextFamilyAblations=contextFamilyAblations });
        var contentHash = AgentTrainingDatasetBuilder.Hash(seed);
        var run = new AgentBaselineRun($"ABR-{contentHash[..32]}", Version, request.DatasetId, datasetHash,
            request.TargetName, training.Length, groups.Count, metrics, DateTime.UtcNow, contentHash,
            SegmentMetrics:segmentMetrics,VariantMetrics:variantMetrics,WalkForwardMetrics:walkForwardMetrics,
            PromotionGate:promotionGate,ContextAblations:contextAblations,
            ContextFamilyAblations:contextFamilyAblations);
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
            SELECT Split,InstrumentId,ModuleId,Direction,LabelJson,EventTimeUtc,OutcomeKnownAtUtc,FeatureJson
            FROM AgentResearchExamples WHERE DatasetId=$id ORDER BY EventTimeUtc,ExampleId;
            """;
        command.Parameters.AddWithValue("$id", datasetId); var values = new List<Row>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            var labels = JsonSerializer.Deserialize<Dictionary<string,decimal>>(reader.GetString(4)) ?? [];
            if (!labels.TryGetValue(target, out var actual)) continue;
            values.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                $"{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetString(3)}", actual,
                Parse(reader.GetString(5)),Parse(reader.GetString(6)),
                JsonSerializer.Deserialize<Dictionary<string,decimal>>(reader.GetString(7))??[]));
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
    private static bool IsResearchContextFeature(string name)=>name.StartsWith("time.",StringComparison.Ordinal)||
        name.StartsWith("context.session.",StringComparison.Ordinal)||
        name.StartsWith("context.volatility.",StringComparison.Ordinal)||
        name.StartsWith("context.volume.",StringComparison.Ordinal)||
        name.StartsWith("context.trend.",StringComparison.Ordinal)||
        name.StartsWith("context.momentum.",StringComparison.Ordinal);
    private static readonly IReadOnlyDictionary<string,string> ContextFamilies=new Dictionary<string,string>(StringComparer.Ordinal)
    {{"seasonality","time."},{"session","context.session."},{"volatility","context.volatility."},
     {"volume","context.volume."},{"trend","context.trend."},{"momentum","context.momentum."}};
    private static DateTime Parse(string value)=>DateTime.Parse(value,null,DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static RidgeModel FitRidge(IReadOnlyList<Row> training,decimal lambda,Func<string,bool>? include=null)
    {
        var names=training.SelectMany(x=>x.Features.Keys).Distinct(StringComparer.Ordinal)
            .Where(name=>include?.Invoke(name)??true).Order().ToArray();
        var means=names.Select(name=>training.Average(x=>x.Features.GetValueOrDefault(name))).ToArray();
        var scales=names.Select((name,index)=>
        {
            var variance=training.Average(x=>{var delta=x.Features.GetValueOrDefault(name)-means[index];return delta*delta;});
            var scale=(decimal)Math.Sqrt((double)variance);return scale==0?1m:scale;
        }).ToArray();
        var size=names.Length+1;var matrix=new decimal[size,size];var vector=new decimal[size];
        foreach(var row in training)
        {
            var x=new decimal[size];x[0]=1m;
            for(var i=0;i<names.Length;i++)x[i+1]=(row.Features.GetValueOrDefault(names[i])-means[i])/scales[i];
            for(var i=0;i<size;i++){vector[i]+=x[i]*row.Actual;for(var j=0;j<size;j++)matrix[i,j]+=x[i]*x[j];}
        }
        for(var i=1;i<size;i++)matrix[i,i]+=lambda;
        return new(names,means,scales,Solve(matrix,vector));
    }

    private static decimal[] Solve(decimal[,] matrix,decimal[] vector)
    {
        var size=vector.Length;
        for(var pivot=0;pivot<size;pivot++)
        {
            var best=pivot;for(var row=pivot+1;row<size;row++)if(Math.Abs(matrix[row,pivot])>Math.Abs(matrix[best,pivot]))best=row;
            if(best!=pivot){for(var column=0;column<size;column++)(matrix[pivot,column],matrix[best,column])=(matrix[best,column],matrix[pivot,column]);(vector[pivot],vector[best])=(vector[best],vector[pivot]);}
            if(Math.Abs(matrix[pivot,pivot])<0.000000000001m)continue;
            for(var row=pivot+1;row<size;row++)
            {var factor=matrix[row,pivot]/matrix[pivot,pivot];for(var column=pivot;column<size;column++)matrix[row,column]-=factor*matrix[pivot,column];vector[row]-=factor*vector[pivot];}
        }
        var result=new decimal[size];
        for(var row=size-1;row>=0;row--){var sum=vector[row];for(var column=row+1;column<size;column++)sum-=matrix[row,column]*result[column];result[row]=Math.Abs(matrix[row,row])<0.000000000001m?0m:sum/matrix[row,row];}
        return result;
    }

    private static BoostedStumpModel FitBoostedStumps(IReadOnlyList<Row> training,int iterations,decimal learningRate)
    {
        var names=training.SelectMany(x=>x.Features.Keys).Distinct(StringComparer.Ordinal).Order().ToArray();
        var thresholds=names.ToDictionary(name=>name,name=>
        {
            var values=training.Select(x=>x.Features.GetValueOrDefault(name)).Order().ToArray();
            return Enumerable.Range(1,5).Select(part=>values[(int)Math.Floor((values.Length-1)*(part/6m))])
                .Distinct().ToArray();
        },StringComparer.Ordinal);
        var initial=training.Average(x=>x.Actual);var predictions=Enumerable.Repeat(initial,training.Count).ToArray();
        var stumps=new List<DecisionStump>();
        for(var iteration=0;iteration<iterations;iteration++)
        {
            DecisionStump? best=null;decimal bestError=decimal.MaxValue;
            foreach(var name in names)foreach(var threshold in thresholds[name])
            {
                decimal leftSum=0,rightSum=0;var leftCount=0;var rightCount=0;
                for(var i=0;i<training.Count;i++)
                {var residual=training[i].Actual-predictions[i];if(training[i].Features.GetValueOrDefault(name)<=threshold){leftSum+=residual;leftCount++;}else{rightSum+=residual;rightCount++;}}
                if(leftCount==0||rightCount==0)continue;
                var left=leftSum/leftCount;var right=rightSum/rightCount;decimal error=0;
                for(var i=0;i<training.Count;i++)
                {var residual=training[i].Actual-predictions[i];var estimate=training[i].Features.GetValueOrDefault(name)<=threshold?left:right;var delta=residual-estimate;error+=delta*delta;}
                if(error>=bestError)continue;bestError=error;best=new(name,threshold,left*learningRate,right*learningRate);
            }
            if(best is null)break;stumps.Add(best);
            for(var i=0;i<training.Count;i++)predictions[i]+=best.Predict(training[i]);
        }
        return new(initial,stumps);
    }

    private static AgentResearchPromotionGate BuildPromotionGate(
        IReadOnlyList<AgentBaselineVariantMetric> variants,IReadOnlyList<AgentBaselineSegmentMetric> segments,
        IReadOnlyList<AgentWalkForwardMetric> folds)
    {
        var candidates=new[]{"instrument-module-direction-mean","ridge-linear","boosted-stumps"};
        var candidate=variants.Where(x=>x.Split=="Validation"&&candidates.Contains(x.Variant,StringComparer.Ordinal))
            .OrderBy(x=>x.MeanAbsoluteError).ThenByDescending(x=>x.DirectionalAccuracy).First();
        var test=variants.Single(x=>x.Variant==candidate.Variant&&x.Split=="Test");
        var global=variants.Single(x=>x.Variant=="global-mean"&&x.Split=="Test");
        var beatsMae=test.MeanAbsoluteError<global.MeanAbsoluteError;
        var beatsDirection=test.DirectionalAccuracy>=global.DirectionalAccuracy+0.02m;
        var stable=folds.Count>=3&&folds.All(x=>x.DirectionalAccuracy>=0.50m);
        var coverage=segments.Where(x=>x.Split=="Test").All(x=>x.SampleCount>=50);
        var reasons=new List<string>();
        if(!beatsMae)reasons.Add("Candidate test MAE does not beat the global-mean control.");
        if(!beatsDirection)reasons.Add("Candidate test directional accuracy lacks the required 2-point lift.");
        if(!stable)reasons.Add("One or more embargoed walk-forward folds are below 50% directional accuracy.");
        if(!coverage)reasons.Add("At least one instrument has fewer than 50 untouched test examples.");
        return new(candidate.Variant,reasons.Count==0?"EligibleForResearchReview":"Rejected",beatsMae,
            beatsDirection,stable,coverage,reasons);
    }

    private static IReadOnlyList<AgentWalkForwardMetric> BuildWalkForwardMetrics(IReadOnlyList<Row> rows,int embargoMinutes)
    {
        var development=rows.Where(x=>x.Split is "Train" or "Validation")
            .GroupBy(x=>x.InstrumentId,StringComparer.Ordinal)
            .ToDictionary(x=>x.Key,x=>x.OrderBy(y=>y.EventTimeUtc).ToArray(),StringComparer.Ordinal);
        var metrics=new List<AgentWalkForwardMetric>();
        for(var fold=0;fold<3;fold++)
        {
            var training=new List<Row>();var validation=new List<Row>();
            foreach(var instrument in development.Values)
            {
                var start=(int)Math.Floor(instrument.Length*(0.70m+fold*0.10m));
                var end=fold==2?instrument.Length:(int)Math.Floor(instrument.Length*(0.80m+fold*0.10m));
                if(start>=end)continue;
                var window=instrument[start..end];var cutoff=window[0].EventTimeUtc.AddMinutes(-embargoMinutes);
                training.AddRange(instrument[..start].Where(x=>x.OutcomeKnownAtUtc<=cutoff));validation.AddRange(window);
            }
            if(training.Count==0||validation.Count==0)continue;
            var global=training.Average(x=>x.Actual);
            var groups=training.GroupBy(x=>x.GroupKey,StringComparer.Ordinal)
                .ToDictionary(x=>x.Key,x=>x.Average(y=>y.Actual),StringComparer.Ordinal);
            decimal Predict(Row row)=>groups.GetValueOrDefault(row.GroupKey,global);
            var predictions=validation.Select(x=>(x.Actual,Prediction:Predict(x))).ToArray();
            var mae=predictions.Average(x=>Math.Abs(x.Actual-x.Prediction));
            var mse=predictions.Average(x=>(x.Actual-x.Prediction)*(x.Actual-x.Prediction));
            var accuracy=predictions.Count(x=>Math.Sign(x.Actual)==Math.Sign(x.Prediction))/(decimal)predictions.Length;
            metrics.Add(new(fold+1,embargoMinutes,training.Count,validation.Count,
                validation.Min(x=>x.EventTimeUtc),validation.Max(x=>x.EventTimeUtc),Round(mae),
                Round((decimal)Math.Sqrt((double)mse)),Round(accuracy)));
        }
        return metrics;
    }

    private sealed record Row(string Split,string InstrumentId,string ModuleId,string GroupKey,decimal Actual,
        DateTime EventTimeUtc,DateTime OutcomeKnownAtUtc,IReadOnlyDictionary<string,decimal> Features);
    private sealed record RidgeModel(string[] Names,decimal[] Means,decimal[] Scales,decimal[] Coefficients)
    {
        public decimal Predict(Row row)
        {var value=Coefficients[0];for(var i=0;i<Names.Length;i++)value+=Coefficients[i+1]*(row.Features.GetValueOrDefault(Names[i])-Means[i])/Scales[i];return value;}
    }
    private sealed record DecisionStump(string Feature,decimal Threshold,decimal LeftValue,decimal RightValue)
    {public decimal Predict(Row row)=>row.Features.GetValueOrDefault(Feature)<=Threshold?LeftValue:RightValue;}
    private sealed record BoostedStumpModel(decimal Initial,IReadOnlyList<DecisionStump> Stumps)
    {public decimal Predict(Row row)=>Initial+Stumps.Sum(x=>x.Predict(row));}
}
