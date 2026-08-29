using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;

namespace PFA_FVG_Scanner.Services;

public sealed class AgentBaselineTrainingService(PfaDatabase database)
{
    public const string Version = "research-promotion-gate-2.7.0";

    public async Task<AgentBaselineRun> TrainAsync(AgentBaselineTrainingRequest request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DatasetId)) throw new ArgumentException("DatasetId is required.");
        if (request.TargetName is not ("directionalCloseTicks" or "netR"))
            throw new ArgumentException("The baseline supports directionalCloseTicks and finalized netR targets only.");
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
        var ridgeInteractions=FitInteractionRidge(training,8);
        var familyModels=ContextFamilies.ToDictionary(x=>x.Key,x=>FitRidge(training,1m,
            name=>!IsResearchContextFeature(name)||x.Value.Any(prefix=>name.StartsWith(prefix,StringComparison.Ordinal))),StringComparer.Ordinal);
        var boostedStumps = FitBoostedStumps(training, 25, 0.10m);
        var moduleBoostedStumps=training.GroupBy(x=>x.ModuleId,StringComparer.Ordinal)
            .ToDictionary(x=>x.Key,x=>FitBoostedStumps(x.ToArray(),25,.10m),StringComparer.Ordinal);
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
            ("ridge-context-interactions",ridgeInteractions.Predict),
            ("boosted-stumps-capped", boostedStumps.Predict),
            ("module-boosted-stumps-capped",row=>moduleBoostedStumps.GetValueOrDefault(row.ModuleId,boostedStumps).Predict(row))
        };
        var variantMetrics = variants.SelectMany(variant => new[] { "Validation", "Test" }.Select(split =>
        {
            var metric = Evaluate(split, rows.Where(x => x.Split == split).ToArray(), variant.Predict);
            return new AgentBaselineVariantMetric(variant.Name, split, metric.SampleCount, metric.MeanAbsoluteError,
                metric.RootMeanSquaredError, metric.DirectionalAccuracy);
        })).ToArray();
        var economicPolicyMetrics=request.TargetName=="netR"?variants.SelectMany(variant=>new[]{"Validation","Test"}.Select(split=>
            EconomicPolicy(variant.Name,split,rows.Where(x=>x.Split==split).ToArray(),variant.Predict))).ToArray():[];
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
        var economicWalkForward=request.TargetName=="netR"?BuildEconomicWalkForwardMetrics(rows,15):[];
        var artifacts=new[]{Artifact("ridge-linear",request.TargetName,ridge),
            Artifact("ridge-context-interactions",request.TargetName,ridgeInteractions)};
        var stumpArtifacts=new[]{StumpArtifact("boosted-stumps-capped",request.TargetName,"*",boostedStumps)}
            .Concat(moduleBoostedStumps.OrderBy(x=>x.Key,StringComparer.Ordinal)
                .Select(x=>StumpArtifact("module-boosted-stumps-capped",request.TargetName,x.Key,x.Value))).ToArray();
        var promotionGate = BuildPromotionGate(request.TargetName,variantMetrics, segmentMetrics, walkForwardMetrics,
            economicPolicyMetrics,economicWalkForward);
        var seed = JsonSerializer.Serialize(new { Version,request.DatasetId,datasetHash,request.TargetName,
            Groups=groups.OrderBy(x=>x.Key),GlobalMean=globalMean,Metrics=metrics,SegmentMetrics=segmentMetrics,
            VariantMetrics=variantMetrics,WalkForwardMetrics=walkForwardMetrics,PromotionGate=promotionGate,
            ContextAblations=contextAblations,ContextFamilyAblations=contextFamilyAblations,
            EconomicPolicyMetrics=economicPolicyMetrics,EconomicWalkForwardMetrics=economicWalkForward,
            ModelArtifacts=artifacts,StumpArtifacts=stumpArtifacts });
        var contentHash = AgentTrainingDatasetBuilder.Hash(seed);
        var run = new AgentBaselineRun($"ABR-{contentHash[..32]}", Version, request.DatasetId, datasetHash,
            request.TargetName, training.Length, groups.Count, metrics, DateTime.UtcNow, contentHash,
            SegmentMetrics:segmentMetrics,VariantMetrics:variantMetrics,WalkForwardMetrics:walkForwardMetrics,
            PromotionGate:promotionGate,ContextAblations:contextAblations,
            ContextFamilyAblations:contextFamilyAblations,EconomicPolicyMetrics:economicPolicyMetrics,
            EconomicWalkForwardMetrics:economicWalkForward,ModelArtifacts:artifacts,StumpArtifacts:stumpArtifacts);
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

    public async Task<AgentResearchScore> ScoreAsync(string runId,DateTime featuresKnownAtUtc,
        IReadOnlyDictionary<string,decimal> features,CancellationToken token=default)
        =>await ScoreAsync(runId,featuresKnownAtUtc,features,"ridge-linear",token);

    public async Task<AgentResearchScore> ScoreAsync(string runId,DateTime featuresKnownAtUtc,
        IReadOnlyDictionary<string,decimal> features,string variant,CancellationToken token=default)
    {
        var run=(await GetAllAsync(token)).FirstOrDefault(x=>x.RunId==runId)??throw new KeyNotFoundException("Agent baseline run was not found.");
        var artifact=run.ModelArtifacts?.FirstOrDefault(x=>x.Variant==variant);
        if(artifact is not null)
        {if(artifact.Coefficients.Count!=artifact.FeatureNames.Count+1)throw new InvalidOperationException("Frozen model artifact is malformed.");
            var prediction=artifact.Coefficients[0];for(var i=0;i<artifact.FeatureNames.Count;i++)prediction+=artifact.Coefficients[i+1]*(FeatureValue(features,artifact.FeatureNames[i])-artifact.Means[i])/artifact.Scales[i];
            prediction=Round(prediction);return Score(run,artifact.ArtifactId,artifact.ContentHash,featuresKnownAtUtc,prediction);}
        var module=features.Where(x=>x.Key.StartsWith("context.module.",StringComparison.Ordinal)&&x.Value==1)
            .Select(x=>x.Key["context.module.".Length..]).FirstOrDefault()??"*";
        var stump=run.StumpArtifacts?.FirstOrDefault(x=>x.Variant==variant&&(x.ModuleId==module||x.ModuleId=="*"));
        if(stump is null)throw new InvalidOperationException($"Run has no frozen '{variant}' artifact for module '{module}'.");
        var value=stump.InitialPrediction+stump.Stumps.Sum(x=>features.GetValueOrDefault(x.Feature)<=x.Threshold?x.LeftValue:x.RightValue);
        return Score(run,stump.ArtifactId,stump.ContentHash,featuresKnownAtUtc,Round(value));
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
    private static AgentLinearModelArtifact Artifact(string variant,string target,RidgeModel model)
    {var hash=AgentTrainingDatasetBuilder.Hash(JsonSerializer.Serialize(new{Version,variant,target,model.Names,model.Means,model.Scales,model.Coefficients}));return new($"AMA-{hash[..32]}",variant,target,model.Names,model.Means,model.Scales,model.Coefficients,hash);}
    private static AgentBoostedStumpArtifact StumpArtifact(string variant,string target,string module,BoostedStumpModel model)
    {var stumps=model.Stumps.Select(x=>new AgentDecisionStumpArtifact(x.Feature,x.Threshold,x.LeftValue,x.RightValue)).ToArray();var hash=AgentTrainingDatasetBuilder.Hash(JsonSerializer.Serialize(new{Version,variant,target,module,model.Initial,Stumps=stumps}));return new($"ASA-{hash[..32]}",variant,target,module,model.Initial,stumps,hash);}
    private static AgentResearchScore Score(AgentBaselineRun run,string artifactId,string artifactHash,DateTime known,decimal prediction)=>
        new(run.RunId,artifactId,run.TargetName,DateTime.UtcNow,Utc(known),prediction,prediction>0?"PredictedPositive":prediction<0?"PredictedNegative":"Neutral",artifactHash);
    private static DateTime Utc(DateTime value)=>value.Kind==DateTimeKind.Utc?value:value.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(value,DateTimeKind.Utc):value.ToUniversalTime();
    private static bool IsResearchContextFeature(string name)=>name.StartsWith("time.",StringComparison.Ordinal)||
        name.StartsWith("context.session.",StringComparison.Ordinal)||
        name.StartsWith("context.volatility.",StringComparison.Ordinal)||
        name.StartsWith("context.volume.",StringComparison.Ordinal)||
        name.StartsWith("context.trend.",StringComparison.Ordinal)||
        name.StartsWith("context.momentum.",StringComparison.Ordinal)||
        name.StartsWith("context.regime.",StringComparison.Ordinal)||
        name.StartsWith("context.interaction.",StringComparison.Ordinal)||
        name.StartsWith("context.availability.",StringComparison.Ordinal);
    private static readonly IReadOnlyDictionary<string,string[]> ContextFamilies=new Dictionary<string,string[]>(StringComparer.Ordinal)
    {{"seasonality",["time."]},{"session",["context.session."]},{"volatility",["context.volatility."]},
     {"volume",["context.volume."]},{"trend",["context.trend."]},{"momentum",["context.momentum."]},
     {"regime-state",["context.regime."]},{"regime-interactions",["context.interaction."]},
     {"source-availability",["context.availability."]}};
    private static DateTime Parse(string value)=>DateTime.Parse(value,null,DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static RidgeModel FitRidge(IReadOnlyList<Row> training,decimal lambda,Func<string,bool>? include=null)
        =>FitRidge(training,lambda,include,[]);

    private static RidgeModel FitRidge(IReadOnlyList<Row> training,decimal lambda,Func<string,bool>? include,
        IReadOnlyList<string> additionalNames)
    {
        const int ridgeTrainingCap=6000;
        if(training.Count>ridgeTrainingCap)training=Enumerable.Range(0,ridgeTrainingCap)
            .Select(index=>training[(int)((long)index*training.Count/ridgeTrainingCap)]).ToArray();
        var names=training.SelectMany(x=>x.Features.Keys).Distinct(StringComparer.Ordinal)
            .Where(name=>include?.Invoke(name)??true).Concat(additionalNames).Distinct(StringComparer.Ordinal).Order().ToArray();
        var means=names.Select(name=>training.Average(x=>FeatureValue(x.Features,name))).ToArray();
        var scales=names.Select((name,index)=>
        {
            var variance=training.Average(x=>{var delta=FeatureValue(x.Features,name)-means[index];return delta*delta;});
            var scale=(decimal)Math.Sqrt((double)variance);return scale==0?1m:scale;
        }).ToArray();
        var size=names.Length+1;var matrix=new decimal[size,size];var vector=new decimal[size];
        foreach(var row in training)
        {
            var x=new decimal[size];x[0]=1m;
            for(var i=0;i<names.Length;i++)x[i+1]=(FeatureValue(row.Features,names[i])-means[i])/scales[i];
            for(var i=0;i<size;i++){vector[i]+=x[i]*row.Actual;for(var j=0;j<size;j++)matrix[i,j]+=x[i]*x[j];}
        }
        for(var i=1;i<size;i++)matrix[i,i]+=lambda;
        return new(names,means,scales,Solve(matrix,vector));
    }

    private static RidgeModel FitInteractionRidge(IReadOnlyList<Row> training,int selectedFeatureCount)
    {
        const int interactionTrainingCap=6000;
        if(training.Count>interactionTrainingCap)training=Enumerable.Range(0,interactionTrainingCap)
            .Select(index=>training[(int)((long)index*training.Count/interactionTrainingCap)]).ToArray();
        var meanTarget=training.Average(x=>x.Actual);
        var selected=training.SelectMany(x=>x.Features.Keys).Distinct(StringComparer.Ordinal)
            .Where(name=>IsResearchContextFeature(name)||name.StartsWith("policy.",StringComparison.Ordinal)||
                name.StartsWith("market.",StringComparison.Ordinal))
            .Select(name=>new{Name=name,Signal=Math.Abs(training.Average(x=>(x.Features.GetValueOrDefault(name))*
                (x.Actual-meanTarget)))})
            .OrderByDescending(x=>x.Signal).ThenBy(x=>x.Name,StringComparer.Ordinal).Take(selectedFeatureCount)
            .Select(x=>x.Name).ToArray();
        var interactions=selected.SelectMany((left,index)=>selected.Skip(index+1)
            .Select(right=>$"interaction::{left}::{right}")).ToArray();
        return FitRidge(training,1m,null,interactions);
    }

    private static decimal FeatureValue(IReadOnlyDictionary<string,decimal> features,string name)
    {
        if(!name.StartsWith("interaction::",StringComparison.Ordinal))return features.GetValueOrDefault(name);
        var parts=name[13..].Split("::",2,StringSplitOptions.None);
        return parts.Length==2?features.GetValueOrDefault(parts[0])*features.GetValueOrDefault(parts[1]):0m;
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
        const int stumpTrainingCap=6000;
        if(training.Count>stumpTrainingCap)training=Enumerable.Range(0,stumpTrainingCap)
            .Select(index=>training[(int)((long)index*training.Count/stumpTrainingCap)]).ToArray();
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

    private static AgentResearchPromotionGate BuildPromotionGate(string target,
        IReadOnlyList<AgentBaselineVariantMetric> variants,IReadOnlyList<AgentBaselineSegmentMetric> segments,
        IReadOnlyList<AgentWalkForwardMetric> folds,IReadOnlyList<AgentEconomicPolicyMetric> economic,
        IReadOnlyList<AgentEconomicWalkForwardMetric> economicFolds)
    {
        var candidates=new[]{"instrument-module-direction-mean","ridge-linear","boosted-stumps-capped","module-boosted-stumps-capped"};
        if(target=="netR")return BuildEconomicPromotionGate(variants,segments,economic,economicFolds,candidates);
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

    private static AgentResearchPromotionGate BuildEconomicPromotionGate(IReadOnlyList<AgentBaselineVariantMetric> variants,
        IReadOnlyList<AgentBaselineSegmentMetric> segments,IReadOnlyList<AgentEconomicPolicyMetric> economic,
        IReadOnlyList<AgentEconomicWalkForwardMetric> economicFolds,string[] candidates)
    {
        var validation=economic.Where(x=>x.Split=="Validation"&&candidates.Contains(x.Variant,StringComparer.Ordinal)&&x.SelectedSamples>=100)
            .OrderByDescending(x=>x.MeanNetR).ThenByDescending(x=>x.ProfitFactor).FirstOrDefault();
        var candidate=validation?.Variant??"ridge-linear";var test=economic.Single(x=>x.Variant==candidate&&x.Split=="Test");
        var candidateTest=variants.Single(x=>x.Variant==candidate&&x.Split=="Test");var global=variants.Single(x=>x.Variant=="global-mean"&&x.Split=="Test");
        var reasons=new List<string>();if(validation is null||validation.MeanNetR<=0)reasons.Add("No validation-selected policy has positive net-R expectancy with at least 100 trades.");
        if(test.SelectedSamples<100)reasons.Add("The predicted-positive untouched test policy has fewer than 100 trades.");
        if(test.MeanNetR<=0)reasons.Add("Predicted-positive untouched test trades have non-positive expectancy.");
        if(test.ProfitFactor<=1)reasons.Add("Predicted-positive untouched test trades have profit factor at or below 1.0.");
        var stable=economicFolds.Count>=3&&economicFolds.All(x=>x.SelectedSamples>=100&&x.MeanNetR>0&&x.ProfitFactor>1);
        if(!stable)reasons.Add("One or more economic walk-forward folds lacks 100 trades, positive expectancy, or profit factor above 1.0.");
        var coverage=segments.Where(x=>x.Split=="Test").All(x=>x.SampleCount>=50);
        return new(candidate,reasons.Count==0?"EligibleForResearchReview":"Rejected",candidateTest.MeanAbsoluteError<global.MeanAbsoluteError,
            false,stable,coverage,reasons);
    }

    private static AgentEconomicPolicyMetric EconomicPolicy(string variant,string split,IReadOnlyList<Row> rows,Func<Row,decimal> predictor)
    {
        var selected=rows.Where(x=>predictor(x)>0).Select(x=>x.Actual).ToArray();if(selected.Length==0)return new(variant,split,0,0,0,0,0);
        var grossWins=selected.Where(x=>x>0).Sum();var grossLoss=Math.Abs(selected.Where(x=>x<0).Sum());decimal equity=0,peak=0,drawdown=0;
        foreach(var result in selected){equity+=result;peak=Math.Max(peak,equity);drawdown=Math.Max(drawdown,peak-equity);}
        return new(variant,split,selected.Length,Round(selected.Average()),Round(selected.Count(x=>x>0)/(decimal)selected.Length),
            grossLoss==0?decimal.MaxValue:Round(grossWins/grossLoss),Round(drawdown));
    }

    private static IReadOnlyList<AgentEconomicWalkForwardMetric> BuildEconomicWalkForwardMetrics(IReadOnlyList<Row> rows,int embargoMinutes)
    {
        var development=rows.Where(x=>x.Split is "Train" or "Validation").OrderBy(x=>x.EventTimeUtc).ToArray();
        var output=new List<AgentEconomicWalkForwardMetric>();
        for(var fold=0;fold<3;fold++)
        {
            var start=(int)Math.Floor(development.Length*(0.70m+fold*.10m));var end=fold==2?development.Length:(int)Math.Floor(development.Length*(.80m+fold*.10m));
            if(start>=end)continue;var validation=development[start..end];var cutoff=validation[0].EventTimeUtc.AddMinutes(-embargoMinutes);
            var training=development[..start].Where(x=>x.OutcomeKnownAtUtc<=cutoff).ToArray();if(training.Length==0)continue;
            var model=FitRidge(training,1m);var metric=EconomicPolicy("ridge-linear",$"Fold{fold+1}",validation,model.Predict);
            output.Add(new(fold+1,embargoMinutes,training.Length,validation.Length,metric.SelectedSamples,
                validation[0].EventTimeUtc,validation[^1].EventTimeUtc,metric.MeanNetR,metric.WinRate,metric.ProfitFactor,metric.MaximumDrawdownR));
        }
        return output;
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
        {var value=Coefficients[0];for(var i=0;i<Names.Length;i++)value+=Coefficients[i+1]*(FeatureValue(row.Features,Names[i])-Means[i])/Scales[i];return value;}
    }
    private sealed record DecisionStump(string Feature,decimal Threshold,decimal LeftValue,decimal RightValue)
    {public decimal Predict(Row row)=>row.Features.GetValueOrDefault(Feature)<=Threshold?LeftValue:RightValue;}
    private sealed record BoostedStumpModel(decimal Initial,IReadOnlyList<DecisionStump> Stumps)
    {public decimal Predict(Row row)=>Initial+Stumps.Sum(x=>x.Predict(row));}
}
