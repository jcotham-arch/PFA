using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PFA_FVG_Scanner.Services;

public sealed class PointInTimeCrossMarketIndex
{
    public const string Version="point-in-time-cross-market-1.0.0";
    public const int MaximumSourceBars=2_000_000;
    private readonly IReadOnlyDictionary<string,Bar[]> _bars;

    private PointInTimeCrossMarketIndex(IReadOnlyDictionary<string,Bar[]> bars)=>_bars=bars;

    public static async Task<PointInTimeCrossMarketIndex> LoadAsync(SqliteConnection connection,DateTime asOfUtc,
        CancellationToken token=default)
    {
        await using var command=connection.CreateCommand();command.CommandText=$"""
            SELECT CanonicalBarId,InstrumentId,CloseTimeUtc,Close
            FROM CanonicalResolvedResearchBars
            WHERE Timeframe='1m' AND CloseTimeUtc<=$asOf
            ORDER BY InstrumentId,CloseTimeUtc
            LIMIT {MaximumSourceBars+1};
            """;command.Parameters.AddWithValue("$asOf",asOfUtc.ToUniversalTime().ToString("O"));
        var values=new Dictionary<string,List<Bar>>(StringComparer.Ordinal);
        var count=0;await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))
        {
            if(++count>MaximumSourceBars)throw new InvalidOperationException(
                $"Cross-market source exceeds the {MaximumSourceBars:N0}-bar in-memory safety limit.");
            var instrument=reader.GetString(1);if(!values.TryGetValue(instrument,out var bars))values[instrument]=bars=[];
            bars.Add(new(reader.GetString(0),DateTime.Parse(reader.GetString(2),null,DateTimeStyles.RoundtripKind),
                decimal.Parse(reader.GetString(3),CultureInfo.InvariantCulture)));
        }
        return new(values.ToDictionary(x=>x.Key,x=>x.Value.ToArray(),StringComparer.Ordinal));
    }

    public string SnapshotJson(string targetInstrument,DateTime decisionTimeUtc)
    {
        var clock=decisionTimeUtc.ToUniversalTime();var peers=new List<object>();
        foreach(var pair in _bars.OrderBy(x=>x.Key,StringComparer.Ordinal))
        {
            if(pair.Key.Equals(targetInstrument,StringComparison.OrdinalIgnoreCase))continue;
            var index=LastAtOrBefore(pair.Value,clock);if(index<5)continue;
            var latest=pair.Value[index];var prior=pair.Value[index-5];
            if(clock-latest.CloseTimeUtc>TimeSpan.FromMinutes(2)||
               latest.CloseTimeUtc-prior.CloseTimeUtc>TimeSpan.FromMinutes(10)||prior.Close==0)continue;
            peers.Add(new{instrumentId=pair.Key,return5Fraction=(latest.Close-prior.Close)/prior.Close,
                latestCloseTimeUtc=latest.CloseTimeUtc,latestSourceId=latest.Id,priorSourceId=prior.Id});
        }
        return JsonSerializer.Serialize(peers);
    }

    private static int LastAtOrBefore(Bar[] bars,DateTime clock)
    {
        var low=0;var high=bars.Length-1;var found=-1;
        while(low<=high){var middle=low+(high-low)/2;if(bars[middle].CloseTimeUtc<=clock){found=middle;low=middle+1;}else high=middle-1;}
        return found;
    }

    private readonly record struct Bar(string Id,DateTime CloseTimeUtc,decimal Close);
}
