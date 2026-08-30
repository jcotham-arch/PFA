using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Intermarket;

namespace PFA_FVG_Scanner.Services;

public sealed class IntermarketContextService(PfaDatabase database)
{
    public const string ContextVersion="intermarket-context-1.0.0";
    public const string RadarVersion="structural-transition-radar-shadow-1.0.0";
    public const string EvaluatorVersion="structural-transition-outcome-1.0.0";

    public async Task SaveAsync(IntermarketObservationBatch batch,CancellationToken token=default)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        if(batch.Gamma is not null)await Insert(connection,transaction,"OptionsGammaObservations",batch.Gamma.ObservationId,
            batch.Gamma.AsOfUtc,batch.Gamma.KnownAtUtc,batch.Gamma.Ticker,batch.Gamma.Provider,JsonSerializer.Serialize(batch.Gamma),batch.Gamma.ContentHash,token);
        if(batch.Volatility is not null)await Insert(connection,transaction,"VolatilityObservations",batch.Volatility.ObservationId,
            batch.Volatility.AsOfUtc,batch.Volatility.KnownAtUtc,"VOL",batch.Volatility.Provider,JsonSerializer.Serialize(batch.Volatility),batch.Volatility.ContentHash,token);
        if(batch.Breadth is not null)await Insert(connection,transaction,"IntermarketBreadthObservations",batch.Breadth.ObservationId,
            batch.Breadth.AsOfUtc,batch.Breadth.KnownAtUtc,"MES",batch.Breadth.Provider,JsonSerializer.Serialize(batch.Breadth),batch.Breadth.ContentHash,token);
        await transaction.CommitAsync(token);
    }

    public async Task<IntermarketContextSnapshot> GetContextAsync(DateTime asOfUtc,decimal mesPrice,CancellationToken token=default)
    {
        var asOf=Utc(asOfUtc);await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        var gamma=await Latest<OptionsGammaObservation>(connection,"OptionsGammaObservations",asOf,token);
        var volatility=await Latest<VolatilityObservation>(connection,"VolatilityObservations",asOf,token);
        var breadth=await Latest<IntermarketBreadthObservation>(connection,"IntermarketBreadthObservations",asOf,token);
        decimal? basis=breadth?.FairValueBasis??(breadth?.EsPrice is not null&&breadth.SpxCash is not null?breadth.EsPrice-breadth.SpxCash:null);
        int? call=Distance(gamma?.CallWallStrike,basis,mesPrice);int? put=Distance(gamma?.PutWallStrike,basis,mesPrice);
        bool? negative=gamma?.TotalNetGamma is null?gamma?.ProviderGammaRegime<0:gamma.TotalNetGamma<0;
        bool? expanding=volatility?.VvixSpot is null?null:volatility.VvixSpot>=110m ||
            (volatility.VixSpot is not null&&volatility.Vix3Month is not null&&volatility.VixSpot/volatility.Vix3Month>=1m);
        bool? divergent=breadth?.EsNqRollingCorrelation is null?null:breadth.EsNqRollingCorrelation<.35m;
        var missing=new List<string>();if(gamma is null)missing.Add("options-gamma");if(volatility is null)missing.Add("volatility-term-structure");
        if(breadth is null)missing.Add("intermarket-breadth");
        return new(asOf,mesPrice,gamma,volatility,breadth,call,put,negative,expanding,divergent,missing,ContextVersion);
    }

    public async Task<StructuralTransitionRadarSnapshot> GetRadarAsync(DateTime? asOfUtc=null,CancellationToken token=default)
    {
        var requested=Utc(asOfUtc??DateTime.UtcNow);var bars=await RecentMesBars(requested,80,token);
        if(bars.Length<20)throw new InvalidOperationException("At least 20 completed MES one-minute bars are required for the radar.");
        var last=bars[^1];var prior=bars.TakeLast(21).SkipLast(1).ToArray();var recent=prior.TakeLast(5).ToArray();
        var earlier=prior.SkipLast(5).TakeLast(15).ToArray();var tick=.25m;
        var recentRange=recent.Average(x=>x.High-x.Low);var earlierRange=Math.Max(tick,earlier.Average(x=>x.High-x.Low));
        var compression=recentRange/earlierRange;var recentVolume=recent.Average(x=>x.Volume);
        var earlierVolume=Math.Max(1m,earlier.Average(x=>x.Volume));var volumeRatio=recentVolume/earlierVolume;
        var upper=prior.Max(x=>x.High);var lower=prior.Min(x=>x.Low);var upDistance=(upper-last.Close)/tick;var downDistance=(last.Close-lower)/tick;
        var momentum=(last.Close-recent[0].Open)/tick;var context=await GetContextAsync(last.CloseTimeUtc,last.Close,token);
        var evidence=new List<TransitionEvidence>();decimal probability=32m;
        if(compression<.75m){probability+=14;evidence.Add(new("range-compression","Elevated",14,$"Recent range is {compression:P0} of its prior baseline."));}
        else evidence.Add(new("range-compression","Normal",0,$"Recent range is {compression:P0} of its prior baseline."));
        if(volumeRatio>1.2m){probability+=8;evidence.Add(new("volume-acceleration","Expanding",8,$"Recent volume is {volumeRatio:P0} of baseline."));}
        if(Math.Min(upDistance,downDistance)<=4){probability+=10;evidence.Add(new("boundary-proximity","Near",10,$"Price is {Math.Min(upDistance,downDistance):F1} ticks from a 20-bar boundary."));}
        if(context.IsNegativeGammaRegime==true){probability+=10;evidence.Add(new("gamma-regime","Negative",10,"Negative gamma may amplify an existing move."));}
        if(context.IsVolatilityExpanding==true){probability+=8;evidence.Add(new("volatility","Expanding",8,"VVIX or the VIX term structure indicates expansion."));}
        if(context.IsBreadthDivergent==true){probability-=5;evidence.Add(new("intermarket-confirmation","Divergent",-5,"Related-market agreement is weak."));}
        var direction=momentum>2||upDistance<downDistance?"Bullish":momentum< -2||downDistance<upDistance?"Bearish":"Uncertain";
        decimal directional=Math.Clamp(50m+Math.Min(18m,Math.Abs(momentum)*2m)-(context.IsBreadthDivergent==true?8m:0m),35m,78m);
        probability=Math.Clamp(probability,10m,88m);var state=compression<.75m?"Compressed balance":Math.Abs(momentum)>=4?"Directional auction":"Balanced rotation";
        var transition=probability>=60?direction=="Uncertain"?"Volatility expansion":$"{direction} structural expansion":"No high-confidence transition";
        var seed=JsonSerializer.Serialize(new{last.CloseTimeUtc,state,transition,direction,probability,directional,Evidence=evidence,context.CalculationVersion});
        var hash=AgentTrainingDatasetBuilder.Hash(seed);return new($"STR-{hash[..32]}","MES",last.CloseTimeUtc,state,transition,direction,15,
            probability,directional,"UncalibratedShadow","Research only — cannot authorize or route a trade",evidence,context.MissingContext,context,RadarVersion,hash);
    }

    public async Task<StructuralTransitionRadarSnapshot> CaptureAsync(DateTime? asOfUtc=null,CancellationToken token=default)
    {
        var value=await GetRadarAsync(asOfUtc,token);await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="""
            INSERT OR IGNORE INTO StructuralTransitionPredictions
            (PredictionId,InstrumentId,AsOfUtc,HorizonMinutes,PredictedTransition,Probability,CalibrationStatus,EngineVersion,ContentHash,PredictionJson,CreatedAtUtc,CanRouteToRealBroker)
            VALUES($id,$instrument,$asOf,$horizon,$transition,$probability,$calibration,$version,$hash,$json,$created,0);
            """;Add(command,"$id",value.PredictionId);Add(command,"$instrument",value.InstrumentId);Add(command,"$asOf",value.AsOfUtc.ToString("O"));
        Add(command,"$horizon",value.HorizonMinutes);Add(command,"$transition",value.PredictedTransition);Add(command,"$probability",value.TransitionProbability.ToString(CultureInfo.InvariantCulture));
        Add(command,"$calibration",value.CalibrationStatus);Add(command,"$version",value.EngineVersion);Add(command,"$hash",value.ContentHash);Add(command,"$json",JsonSerializer.Serialize(value));Add(command,"$created",DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(token);return value;
    }

    public async Task<StructuralTransitionCalibration> EvaluateAsync(CancellationToken token=default)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT p.PredictionJson FROM StructuralTransitionPredictions p
            LEFT JOIN StructuralTransitionOutcomes o ON o.PredictionId=p.PredictionId
            WHERE o.PredictionId IS NULL ORDER BY p.AsOfUtc;
            """;var pending=new List<StructuralTransitionRadarSnapshot>();await using(var reader=await command.ExecuteReaderAsync(token))
        while(await reader.ReadAsync(token)){var value=JsonSerializer.Deserialize<StructuralTransitionRadarSnapshot>(reader.GetString(0));if(value is not null)pending.Add(value);}
        foreach(var prediction in pending)
        {
            var bars=await MesBarsBetween(prediction.AsOfUtc,prediction.AsOfUtc.AddMinutes(prediction.HorizonMinutes),token);
            if(bars.Length<prediction.HorizonMinutes)continue;var start=prediction.Context.MesPrice;
            var up=(bars.Max(x=>x.High)-start)/.25m;var down=(start-bars.Min(x=>x.Low))/.25m;
            var occurred=Math.Max(up,down)>=8m;var actual=up>=8m&&up>=down?"Bullish":down>=8m?"Bearish":"None";
            var predicted=prediction.PredictedTransition!="No high-confidence transition";
            var success=predicted?occurred&&(prediction.Direction==actual||prediction.Direction=="Uncertain"):!occurred;
            var probability=prediction.TransitionProbability/100m;var brier=(probability-(occurred?1m:0m))*(probability-(occurred?1m:0m));
            var seed=JsonSerializer.Serialize(new{prediction.PredictionId,occurred,actual,up,down,success,brier,EvaluatorVersion});var hash=AgentTrainingDatasetBuilder.Hash(seed);
            var outcome=new StructuralTransitionOutcome($"STO-{hash[..32]}",prediction.PredictionId,bars[^1].CloseTimeUtc,occurred,actual,up,down,success,brier,EvaluatorVersion,hash);
            await SaveOutcome(outcome,token);
        }
        return await GetCalibrationAsync(token);
    }

    public async Task<StructuralTransitionCalibration> GetCalibrationAsync(CancellationToken token=default)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        long predictions=await Scalar(connection,"SELECT COUNT(*) FROM StructuralTransitionPredictions",token);
        await using var command=connection.CreateCommand();command.CommandText="SELECT OutcomeJson FROM StructuralTransitionOutcomes ORDER BY EvaluatedAtUtc DESC";
        var outcomes=new List<StructuralTransitionOutcome>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))
        {var value=JsonSerializer.Deserialize<StructuralTransitionOutcome>(reader.GetString(0));if(value is not null)outcomes.Add(value);}
        var success=outcomes.Count(x=>x.PredictionSuccessful);return new((int)predictions,outcomes.Count,success,
            outcomes.Count==0?0:success/(decimal)outcomes.Count,outcomes.Count==0?0:outcomes.Average(x=>x.BrierScore),
            outcomes.Count<30?"AccumulatingShadowEvidence":"CalibrationReviewEligible",outcomes.Take(20).ToArray());
    }

    private static int? Distance(decimal? strike,decimal? basis,decimal price)=>strike is null||basis is null?null:(int)Math.Round(((strike.Value+basis.Value)-price)/.25m,MidpointRounding.AwayFromZero);
    private async Task<MarketChartBar[]> RecentMesBars(DateTime asOf,int limit,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="""
            WITH ranked AS
            (SELECT OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume,IsComplete,
             ROW_NUMBER() OVER(PARTITION BY OpenTimeUtc ORDER BY Id DESC) rank
             FROM Candles WHERE Symbol LIKE 'MES%' AND Timeframe='1m' AND OpenTimeUtc<$asOf)
            SELECT OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume,IsComplete FROM ranked
            WHERE rank=1 ORDER BY OpenTimeUtc DESC LIMIT $limit;
            """;command.Parameters.AddWithValue("$asOf",asOf.ToString("O"));command.Parameters.AddWithValue("$limit",limit);
        var values=new List<MarketChartBar>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))
        {var open=DateTime.Parse(reader.GetString(0),null,DateTimeStyles.RoundtripKind);var close=reader.IsDBNull(1)?open.AddMinutes(1):DateTime.Parse(reader.GetString(1),null,DateTimeStyles.RoundtripKind);
            values.Add(new(open,close,Parse(reader,2),Parse(reader,3),Parse(reader,4),Parse(reader,5),Parse(reader,6),reader.GetInt32(7)==1));}
        return values.OrderBy(x=>x.OpenTimeUtc).ToArray();
    }
    private async Task<MarketChartBar[]> MesBarsBetween(DateTime start,DateTime end,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="""
            WITH ranked AS
            (SELECT OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume,IsComplete,
             ROW_NUMBER() OVER(PARTITION BY OpenTimeUtc ORDER BY Id DESC) rank
             FROM Candles WHERE Symbol LIKE 'MES%' AND Timeframe='1m' AND OpenTimeUtc>=$start AND OpenTimeUtc<$end)
            SELECT OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume,IsComplete FROM ranked WHERE rank=1 ORDER BY OpenTimeUtc;
            """;command.Parameters.AddWithValue("$start",start.ToString("O"));command.Parameters.AddWithValue("$end",end.ToString("O"));
        var values=new List<MarketChartBar>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))
        {var open=DateTime.Parse(reader.GetString(0),null,DateTimeStyles.RoundtripKind);var close=reader.IsDBNull(1)?open.AddMinutes(1):DateTime.Parse(reader.GetString(1),null,DateTimeStyles.RoundtripKind);
            values.Add(new(open,close,Parse(reader,2),Parse(reader,3),Parse(reader,4),Parse(reader,5),Parse(reader,6),reader.GetInt32(7)==1));}return values.ToArray();
    }
    private async Task SaveOutcome(StructuralTransitionOutcome value,CancellationToken token)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();command.CommandText="""
        INSERT OR IGNORE INTO StructuralTransitionOutcomes
        (OutcomeId,PredictionId,EvaluatedAtUtc,TransitionOccurred,PredictionSuccessful,BrierScore,EvaluatorVersion,ContentHash,OutcomeJson)
        VALUES($id,$prediction,$evaluated,$occurred,$successful,$brier,$version,$hash,$json);
        """;Add(command,"$id",value.OutcomeId);Add(command,"$prediction",value.PredictionId);Add(command,"$evaluated",value.EvaluatedAtUtc.ToString("O"));
        Add(command,"$occurred",value.TransitionOccurred?1:0);Add(command,"$successful",value.PredictionSuccessful?1:0);Add(command,"$brier",value.BrierScore.ToString(CultureInfo.InvariantCulture));
        Add(command,"$version",value.EvaluatorVersion);Add(command,"$hash",value.ContentHash);Add(command,"$json",JsonSerializer.Serialize(value));await command.ExecuteNonQueryAsync(token);}
    private static async Task<long> Scalar(SqliteConnection connection,string sql,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText=sql;return Convert.ToInt64(await command.ExecuteScalarAsync(token));}
    private static decimal Parse(SqliteDataReader reader,int ordinal)=>decimal.Parse(reader.GetString(ordinal),CultureInfo.InvariantCulture);
    private static async Task<T?> Latest<T>(SqliteConnection connection,string table,DateTime asOf,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText=$"SELECT PayloadJson FROM {table} WHERE AsOfUtc<=$asOf AND KnownAtUtc<=$asOf ORDER BY AsOfUtc DESC,KnownAtUtc DESC LIMIT 1";
        command.Parameters.AddWithValue("$asOf",asOf.ToString("O"));var json=await command.ExecuteScalarAsync(token) as string;return json is null?default:JsonSerializer.Deserialize<T>(json);}
    private static async Task Insert(SqliteConnection connection,SqliteTransaction transaction,string table,string id,DateTime asOf,DateTime known,string symbol,string provider,string json,string hash,CancellationToken token)
    {await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText=$"INSERT OR IGNORE INTO {table}(ObservationId,AsOfUtc,KnownAtUtc,Symbol,Provider,PayloadJson,ContentHash) VALUES($id,$asOf,$known,$symbol,$provider,$json,$hash)";
        Add(command,"$id",id);Add(command,"$asOf",Utc(asOf).ToString("O"));Add(command,"$known",Utc(known).ToString("O"));Add(command,"$symbol",symbol);Add(command,"$provider",provider);Add(command,"$json",json);Add(command,"$hash",hash);await command.ExecuteNonQueryAsync(token);}
    private static void Add(SqliteCommand command,string name,object value)=>command.Parameters.AddWithValue(name,value);
    private static DateTime Utc(DateTime value)=>value.Kind switch{DateTimeKind.Utc=>value,DateTimeKind.Unspecified=>DateTime.SpecifyKind(value,DateTimeKind.Utc),_=>value.ToUniversalTime()};
}
