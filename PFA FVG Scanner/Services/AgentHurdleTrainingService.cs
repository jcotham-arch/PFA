using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;

namespace PFA_FVG_Scanner.Services;

public sealed class AgentHurdleTrainingService(PfaDatabase database)
{
    public const string Version="agent-hurdle-model-1.2.0";
    public async Task<AgentHurdleRun> TrainAsync(AgentHurdleTrainingRequest request,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(request.DatasetId))throw new ArgumentException("DatasetId is required.");
        var (hash,rows)=await Read(connectionFactory:database,request.DatasetId.Trim(),token);var train=rows.Where(x=>x.Split=="Train").ToArray();
        if(train.Length==0)throw new InvalidOperationException("The dataset has no training examples.");
        var sample=EvenSample(train,6000);var probability=Fit(sample,x=>x.NetR>0?1m:0m);
        var positive=Fit(EvenSample(sample.Where(x=>x.NetR>0).ToArray(),3000),x=>x.NetR);
        var negative=Fit(EvenSample(sample.Where(x=>x.NetR<=0).ToArray(),3000),x=>x.NetR);
        var calibration=Calibrate(sample,probability.Predict);var calibrationGroups=GroupCalibrations(sample,probability.Predict);
        decimal Calibrated(Row row)=>HierarchicalCalibration(calibration,calibrationGroups,row,probability.Predict(row));
        decimal Score(Row row){var p=Calibrated(row);return p*Math.Max(0,positive.Predict(row))+(1-p)*Math.Min(0,negative.Predict(row));}
        var validationRows=rows.Where(x=>x.Split=="Validation").ToArray();var testRows=rows.Where(x=>x.Split=="Test").ToArray();
        var thresholds=Enumerable.Range(2,18).Select(x=>x*5).Select(p=>validationRows.OrderBy(x=>Score(x)).ElementAt((int)Math.Floor((validationRows.Length-1)*(p/100m))) is var row?Score(row):0).Distinct().ToArray();
        var validationCandidates=thresholds.Select(x=>Metric("Validation",validationRows,x,Score,probability.Predict,Calibrated)).Where(x=>x.SelectedSamples>=100)
            .OrderByDescending(x=>x.MeanNetR).ThenByDescending(x=>x.ProfitFactor).ToArray();
        var selected=validationCandidates.FirstOrDefault()??Metric("Validation",validationRows,0,Score,probability.Predict,Calibrated);
        var test=Metric("Test",testRows,selected.ScoreThreshold,Score,probability.Predict,Calibrated);var reasons=new List<string>();
        if(selected.SelectedSamples<100||selected.MeanNetR<=0||selected.ProfitFactor<=1)reasons.Add("Validation-selected hurdle policy lacks 100 trades, positive expectancy, or profit factor above 1.0.");
        if(test.SelectedSamples<100||test.MeanNetR<=0||test.ProfitFactor<=1)reasons.Add("Untouched test hurdle policy lacks 100 trades, positive expectancy, or profit factor above 1.0.");
        var artifacts=new[]{Artifact("profitability-probability","profitable",probability),Artifact("positive-payoff","conditionalPositiveNetR",positive),Artifact("negative-payoff","conditionalNegativeNetR",negative)};
        var segments=Segments(rows.Where(x=>x.Split is "Validation" or "Test").ToArray(),probability.Predict,Calibrated);
        var seed=JsonSerializer.Serialize(new{Version,request.DatasetId,hash,Threshold=selected.ScoreThreshold,Validation=selected,Test=test,Artifacts=artifacts,Calibration=calibration,CalibrationGroups=calibrationGroups,Segments=segments});var contentHash=AgentTrainingDatasetBuilder.Hash(seed);
        var run=new AgentHurdleRun($"AHR-{contentHash[..32]}",Version,request.DatasetId,hash,train.Length,selected.ScoreThreshold,selected,test,artifacts,reasons.Count==0?"EligibleForResearchReview":"Rejected",reasons,DateTime.UtcNow,contentHash,CalibrationBins:calibration,SegmentMetrics:segments,CalibrationGroups:calibrationGroups);
        await Persist(run,token);return run;
    }
    public async Task<IReadOnlyList<AgentHurdleRun>> GetAllAsync(CancellationToken token=default)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="SELECT RunJson FROM AgentHurdleRuns ORDER BY TrainedAtUtc DESC";var output=new List<AgentHurdleRun>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))output.Add(JsonSerializer.Deserialize<AgentHurdleRun>(reader.GetString(0))!);return output;}
    private static AgentHurdleEconomicMetric Metric(string split,Row[] rows,decimal threshold,Func<Row,decimal> score,Func<Row,decimal> rawProbability,Func<Row,decimal> calibratedProbability)
    {var selected=rows.Where(x=>score(x)>=threshold).Select(x=>x.NetR).ToArray();var rawBrier=Brier(rows,rawProbability);var calibratedBrier=Brier(rows,calibratedProbability);if(selected.Length==0)return new(split,rows.Length,0,Round(threshold),0,0,0,0,calibratedBrier,rawBrier);var wins=selected.Where(x=>x>0).Sum();var losses=Math.Abs(selected.Where(x=>x<0).Sum());decimal equity=0,peak=0,dd=0;foreach(var value in selected){equity+=value;peak=Math.Max(peak,equity);dd=Math.Max(dd,peak-equity);}return new(split,rows.Length,selected.Length,Round(threshold),Round(selected.Average()),Round(selected.Count(x=>x>0)/(decimal)selected.Length),losses==0?decimal.MaxValue:Round(wins/losses),Round(dd),calibratedBrier,rawBrier);}
    private static decimal Brier(Row[] rows,Func<Row,decimal> probability)=>Round(rows.Average(x=>{var delta=Clamp(probability(x),0,1)-(x.NetR>0?1m:0m);return delta*delta;}));
    private static AgentProbabilityCalibrationBin[] Calibrate(Row[] rows,Func<Row,decimal> raw)
    {var global=rows.Count(x=>x.NetR>0)/(decimal)rows.Length;return Enumerable.Range(0,10).Select(bin=>{var members=rows.Where(x=>Bin(raw(x))==bin).ToArray();var mean=members.Length==0?(bin+.5m)/10m:members.Average(x=>Clamp(raw(x),0,1));var calibrated=(members.Count(x=>x.NetR>0)+20m*global)/(members.Length+20m);return new AgentProbabilityCalibrationBin(bin,bin/10m,(bin+1)/10m,members.Length,Round(mean),Round(calibrated));}).ToArray();}
    private static decimal CalibrationValue(IReadOnlyList<AgentProbabilityCalibrationBin> bins,decimal raw)=>bins[Bin(raw)].CalibratedProbability;
    private static AgentProbabilityCalibrationGroup[] GroupCalibrations(Row[] rows,Func<Row,decimal> raw)
    {return rows.SelectMany(x=>new[]{(Type:"Module",Id:x.Module,x),(Type:"Instrument",Id:x.Instrument,x)})
        .GroupBy(x=>(x.Type,x.Id)).Where(x=>x.Count()>=200).OrderBy(x=>x.Key.Type).ThenBy(x=>x.Key.Id)
        .Select(x=>{var values=x.Select(y=>y.x).ToArray();return new AgentProbabilityCalibrationGroup(x.Key.Type,x.Key.Id,values.Length,Calibrate(values,raw));}).ToArray();}
    private static decimal HierarchicalCalibration(IReadOnlyList<AgentProbabilityCalibrationBin> global,
        IReadOnlyList<AgentProbabilityCalibrationGroup> groups,Row row,decimal raw)
    {var values=new List<(decimal Value,int Samples)>{(CalibrationValue(global,raw),global[Bin(raw)].TrainingSamples)};
        foreach(var group in groups.Where(x=>(x.SegmentType=="Module"&&x.SegmentId==row.Module)||(x.SegmentType=="Instrument"&&x.SegmentId==row.Instrument)))
        {var bin=group.Bins[Bin(raw)];values.Add((bin.CalibratedProbability,bin.TrainingSamples));}
        var weighted=values.Sum(x=>x.Value*Math.Max(20,x.Samples));var weight=values.Sum(x=>Math.Max(20,x.Samples));return weight==0?Clamp(raw,0,1):weighted/weight;}
    private static int Bin(decimal probability)=>Math.Min(9,(int)(Clamp(probability,0,1)*10));
    private static AgentHurdleSegmentMetric[] Segments(Row[] rows,Func<Row,decimal> raw,Func<Row,decimal> calibrated)
    {return rows.SelectMany(x=>new[]{(Type:"Instrument",Id:x.Instrument,x),(Type:"Module",Id:x.Module,x)})
        .GroupBy(x=>(x.Type,x.Id,x.x.Split)).OrderBy(x=>x.Key.Type).ThenBy(x=>x.Key.Id).ThenBy(x=>x.Key.Split)
        .Select(group=>{var values=group.Select(x=>x.x).ToArray();return new AgentHurdleSegmentMetric(group.Key.Type,group.Key.Id,group.Key.Split,values.Length,Round(values.Count(x=>x.NetR>0)/(decimal)values.Length),Round(values.Average(raw)),Round(values.Average(calibrated)),Brier(values,raw),Brier(values,calibrated));}).ToArray();}
    private static Row[] EvenSample(Row[] rows,int maximum)=>rows.Length<=maximum?rows:Enumerable.Range(0,maximum).Select(i=>rows[(int)((long)i*rows.Length/maximum)]).ToArray();
    private static Model Fit(Row[] rows,Func<Row,decimal> target)
    {if(rows.Length==0)throw new InvalidOperationException("A hurdle head has no training examples.");var names=rows.SelectMany(x=>x.Features.Keys).Distinct(StringComparer.Ordinal).Order().ToArray();var means=names.Select(n=>rows.Average(x=>x.Features.GetValueOrDefault(n))).ToArray();var scales=names.Select((n,i)=>{var variance=rows.Average(x=>{var d=x.Features.GetValueOrDefault(n)-means[i];return d*d;});var s=(decimal)Math.Sqrt((double)variance);return s==0?1:s;}).ToArray();var size=names.Length+1;var matrix=new decimal[size,size];var vector=new decimal[size];foreach(var row in rows){var x=new decimal[size];x[0]=1;for(var i=0;i<names.Length;i++)x[i+1]=(row.Features.GetValueOrDefault(names[i])-means[i])/scales[i];for(var i=0;i<size;i++){vector[i]+=x[i]*target(row);for(var j=0;j<size;j++)matrix[i,j]+=x[i]*x[j];}}for(var i=1;i<size;i++)matrix[i,i]+=1;return new(names,means,scales,Solve(matrix,vector));}
    private static decimal[] Solve(decimal[,] matrix,decimal[] vector){var size=vector.Length;for(var pivot=0;pivot<size;pivot++){var best=pivot;for(var row=pivot+1;row<size;row++)if(Math.Abs(matrix[row,pivot])>Math.Abs(matrix[best,pivot]))best=row;if(best!=pivot){for(var column=0;column<size;column++)(matrix[pivot,column],matrix[best,column])=(matrix[best,column],matrix[pivot,column]);(vector[pivot],vector[best])=(vector[best],vector[pivot]);}if(Math.Abs(matrix[pivot,pivot])<.000000000001m)continue;for(var row=pivot+1;row<size;row++){var factor=matrix[row,pivot]/matrix[pivot,pivot];for(var column=pivot;column<size;column++)matrix[row,column]-=factor*matrix[pivot,column];vector[row]-=factor*vector[pivot];}}var result=new decimal[size];for(var row=size-1;row>=0;row--){var sum=vector[row];for(var column=row+1;column<size;column++)sum-=matrix[row,column]*result[column];result[row]=Math.Abs(matrix[row,row])<.000000000001m?0:sum/matrix[row,row];}return result;}
    private static AgentHurdleHeadArtifact Artifact(string head,string target,Model model){var hash=AgentTrainingDatasetBuilder.Hash(JsonSerializer.Serialize(new{Version,head,target,model.Names,model.Means,model.Scales,model.Coefficients}));return new(head,target,model.Names,model.Means,model.Scales,model.Coefficients,hash);}
    private static async Task<(string Hash,List<Row> Rows)> Read(PfaDatabase connectionFactory,string dataset,CancellationToken token){await using var connection=connectionFactory.CreateConnection();await connection.OpenAsync(token);await using var manifest=connection.CreateCommand();manifest.CommandText="SELECT ContentHash FROM AgentResearchDatasets WHERE DatasetId=$id";manifest.Parameters.AddWithValue("$id",dataset);var hash=Convert.ToString(await manifest.ExecuteScalarAsync(token));if(string.IsNullOrWhiteSpace(hash))throw new KeyNotFoundException("Dataset was not found.");await using var command=connection.CreateCommand();command.CommandText="SELECT Split,InstrumentId,ModuleId,LabelJson,FeatureJson,EventTimeUtc FROM AgentResearchExamples WHERE DatasetId=$id ORDER BY EventTimeUtc,ExampleId";command.Parameters.AddWithValue("$id",dataset);var output=new List<Row>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token)){var labels=JsonSerializer.Deserialize<Dictionary<string,decimal>>(reader.GetString(3))??[];if(labels.TryGetValue("netR",out var netR))output.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),netR,JsonSerializer.Deserialize<Dictionary<string,decimal>>(reader.GetString(4))??[],DateTime.Parse(reader.GetString(5),null,DateTimeStyles.RoundtripKind)));}return(hash,output);}
    private async Task Persist(AgentHurdleRun run,CancellationToken token){await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="INSERT OR IGNORE INTO AgentHurdleRuns(RunId,ModelVersion,DatasetId,DatasetContentHash,TrainedAtUtc,ContentHash,RunJson,CanActivateStrategy,CanRouteToRealBroker) VALUES($id,$version,$dataset,$hash,$trained,$content,$json,0,0)";command.Parameters.AddWithValue("$id",run.RunId);command.Parameters.AddWithValue("$version",run.ModelVersion);command.Parameters.AddWithValue("$dataset",run.DatasetId);command.Parameters.AddWithValue("$hash",run.DatasetContentHash);command.Parameters.AddWithValue("$trained",run.TrainedAtUtc.ToString("O"));command.Parameters.AddWithValue("$content",run.ContentHash);command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(run));await command.ExecuteNonQueryAsync(token);}
    private static decimal Clamp(decimal value,decimal minimum,decimal maximum)=>Math.Min(maximum,Math.Max(minimum,value));private static decimal Round(decimal value)=>decimal.Round(value,6,MidpointRounding.AwayFromZero);
    private sealed record Row(string Split,string Instrument,string Module,decimal NetR,IReadOnlyDictionary<string,decimal> Features,DateTime EventTimeUtc);
    private sealed record Model(string[] Names,decimal[] Means,decimal[] Scales,decimal[] Coefficients){public decimal Predict(Row row){var value=Coefficients[0];for(var i=0;i<Names.Length;i++)value+=Coefficients[i+1]*(row.Features.GetValueOrDefault(Names[i])-Means[i])/Scales[i];return value;}}
}
