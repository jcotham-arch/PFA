using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services;

public sealed record DailyDiscoveryEvent(string EventId,string Symbol,DateTime TimeUtc,DateTime KnownAtUtc,string Timeframe,
    string Type,string Direction,decimal Strength,string Evidence,decimal Open,decimal High,decimal Low,decimal Close,decimal Volume);
public sealed record DailyDiscoveryMethod(string Timeframe,int LookbackBars,bool TerminologyNeutral,string Warning);
public sealed record DailyMarketDiscoveryStudy(string Date,string Clock,DateTime GeneratedAtUtc,IReadOnlyList<string> Symbols,
    int OneMinuteBars,IReadOnlyList<Dictionary<string,object?>> NamedSetups,IReadOnlyList<Dictionary<string,object?>> Sequences,
    IReadOnlyList<DailyDiscoveryEvent> DiscoveredEvents,DailyDiscoveryMethod Method);

public sealed class DailyMarketDiscoveryService(PfaDatabase database)
{
    public async Task<DailyMarketDiscoveryStudy> StudyAsync(DateOnly date, CancellationToken token)
    {
        var start=date.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc); var end=start.AddDays(1);
        await using var connection=database.CreateConnection(); await connection.OpenAsync(token);
        var candles=await LoadCandles(connection,start,end,token);
        var events=new List<DailyDiscoveryEvent>();
        foreach(var symbolGroup in candles.GroupBy(x=>x.Symbol))
        {
            var bars=MarketChartService.Aggregate(symbolGroup.OrderBy(x=>x.OpenTimeUtc).ToArray(),5).ToArray();
            for(var i=20;i<bars.Length;i++)
            {
                var history=bars[(i-20)..i]; var ranges=history.Select(x=>x.High-x.Low).OrderBy(x=>x).ToArray();
                var volumes=history.Select(x=>x.Volume).OrderBy(x=>x).ToArray(); var medianRange=ranges[ranges.Length/2]; var medianVolume=volumes[volumes.Length/2];
                var bar=bars[i]; var range=bar.High-bar.Low; var body=Math.Abs(bar.Close-bar.Open); var upper=bar.High-Math.Max(bar.Open,bar.Close); var lower=Math.Min(bar.Open,bar.Close)-bar.Low;
                void Add(string type,string direction,decimal strength,string evidence)
                {var seed=$"{symbolGroup.Key}|{bar.OpenTimeUtc:O}|5m|{type}|{direction}";var id=$"DME-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..32]}";events.Add(new(id,symbolGroup.Key,bar.OpenTimeUtc,bar.CloseTimeUtc,"5m",type,direction,Math.Round(strength,2),evidence,bar.Open,bar.High,bar.Low,bar.Close,bar.Volume));}
                if(medianRange>0&&range>=medianRange*2) Add("RangeExpansion",bar.Close>=bar.Open?"Bullish":"Bearish",range/medianRange,$"5m range {range} is {range/medianRange:0.00}× its prior-20 median.");
                if(medianVolume>0&&bar.Volume>=medianVolume*2) Add("VolumeBurst",bar.Close>=bar.Open?"Bullish":"Bearish",bar.Volume/medianVolume,$"Volume is {bar.Volume/medianVolume:0.00}× its prior-20 median.");
                var priorFive=history[^5..]; var avgFive=priorFive.Average(x=>x.High-x.Low);
                if(medianRange>0&&avgFive<=medianRange*.65m&&range>=medianRange*1.35m) Add("CompressionRelease",bar.Close>=bar.Open?"Bullish":"Bearish",range/medianRange,"Five-bar compression released into range expansion.");
                if(body>0&&upper>=body*2&&bar.Close<bar.Open) Add("UpperRejection","Bearish",upper/body,"Upper wick is at least twice the real body.");
                if(body>0&&lower>=body*2&&bar.Close>bar.Open) Add("LowerRejection","Bullish",lower/body,"Lower wick is at least twice the real body.");
                if(i>=2&&bar.High>bars[i-1].High&&bars[i-1].High>bars[i-2].High&&bar.Low>bars[i-1].Low) Add("AscendingStructure","Bullish",1,"Three-bar higher-high progression with a higher low.");
                if(i>=2&&bar.Low<bars[i-1].Low&&bars[i-1].Low<bars[i-2].Low&&bar.High<bars[i-1].High) Add("DescendingStructure","Bearish",1,"Three-bar lower-low progression with a lower high.");
            }
        }
        var named=await Rows(connection,"SELECT InstrumentId,PatternType,Direction,COUNT(*) Count FROM UniversalMarketObservations WHERE FormationTimeUtc >= $start AND FormationTimeUtc < $end GROUP BY InstrumentId,PatternType,Direction ORDER BY Count DESC",start,end,token);
        var sequences=await Rows(connection,"SELECT InstrumentId,SequenceDefinitionId,State,COUNT(*) Count FROM MarketSequenceInstances WHERE StartedAtUtc >= $start AND StartedAtUtc < $end GROUP BY InstrumentId,SequenceDefinitionId,State ORDER BY Count DESC",start,end,token);
        return new(date.ToString("yyyy-MM-dd"),"UTC",DateTime.UtcNow,candles.Select(x=>x.Symbol).Distinct().Order().ToArray(),
            candles.Count,named,sequences,events.OrderBy(x=>x.TimeUtc).ToArray(),new("5m",20,true,
                "Events are descriptive research observations, not trade signals or proof of profitability."));
    }

    private static async Task<List<Candle>> LoadCandles(SqliteConnection c,DateTime start,DateTime end,CancellationToken token)
    {
        await using var cmd=c.CreateCommand(); cmd.CommandText="SELECT Symbol,OpenTimeUtc,Open,High,Low,Close,Volume,IsComplete FROM Candles WHERE Timeframe='1m' AND OpenTimeUtc >= $start AND OpenTimeUtc < $end ORDER BY Symbol,OpenTimeUtc"; cmd.Parameters.AddWithValue("$start",start.ToString("O"));cmd.Parameters.AddWithValue("$end",end.ToString("O"));
        var rows=new List<Candle>(); await using var r=await cmd.ExecuteReaderAsync(token); while(await r.ReadAsync(token)) rows.Add(new(){Symbol=r.GetString(0),Timeframe="1m",OpenTimeUtc=DateTime.Parse(r.GetString(1),null,DateTimeStyles.RoundtripKind),Open=decimal.Parse(r.GetString(2),CultureInfo.InvariantCulture),High=decimal.Parse(r.GetString(3),CultureInfo.InvariantCulture),Low=decimal.Parse(r.GetString(4),CultureInfo.InvariantCulture),Close=decimal.Parse(r.GetString(5),CultureInfo.InvariantCulture),Volume=decimal.Parse(r.GetString(6),CultureInfo.InvariantCulture),IsClosed=r.GetInt64(7)==1}); return rows;
    }
    private static async Task<List<Dictionary<string,object?>>> Rows(SqliteConnection c,string sql,DateTime start,DateTime end,CancellationToken token){await using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.Parameters.AddWithValue("$start",start.ToString("O"));cmd.Parameters.AddWithValue("$end",end.ToString("O"));var rows=new List<Dictionary<string,object?>>();await using var r=await cmd.ExecuteReaderAsync(token);while(await r.ReadAsync(token)){var row=new Dictionary<string,object?>();for(var i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);rows.Add(row);}return rows;}
}
