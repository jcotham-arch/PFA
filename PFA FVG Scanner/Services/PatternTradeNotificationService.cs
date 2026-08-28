using System.Globalization;
using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class PatternTradeNotificationService(PfaDatabase database)
{
    public async Task<object> GetLatestAsync(DateTime asOfUtc,int limit,CancellationToken token=default)
    {
        var asOf=Utc(asOfUtc);await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var runCommand=connection.CreateCommand();runCommand.CommandText="SELECT RunId,AsOfUtc FROM PatternTradeResearchRuns WHERE EngineVersion='pattern-trade-hypothesis-engine-1.3.0' ORDER BY CreatedAtUtc DESC LIMIT 1";
        await using var runReader=await runCommand.ExecuteReaderAsync(token);if(!await runReader.ReadAsync(token))
            return new{AsOfUtc=asOf,SemanticsVersion=PatternTradeNotificationInterpreter.Version,IsResearchOnly=true,Notifications=Array.Empty<PatternTradeNotification>()};
        var runId=runReader.GetString(0);var evaluation=Parse(runReader.GetString(1));await runReader.DisposeAsync();
        await using var command=connection.CreateCommand();command.CommandText="""
            SELECT s.SampleJson FROM PatternTradeResearchSamples s
            JOIN UniversalMarketObservations o ON o.ObservationId=s.ObservationId
            WHERE s.RunId=$run AND o.KnownAtUtc<=$asOf
            ORDER BY o.KnownAtUtc DESC,s.SampleId LIMIT $limit;
            """;command.Parameters.AddWithValue("$run",runId);command.Parameters.AddWithValue("$asOf",asOf.ToString("O"));
        command.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,500));var values=new List<PatternTradeNotification>();
        await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))
        {var sample=JsonSerializer.Deserialize<PatternTradeHypothesisSample>(reader.GetString(0));if(sample is not null)values.Add(PatternTradeNotificationInterpreter.Interpret(sample,asOf,evaluation));}
        return new{AsOfUtc=asOf,SemanticsVersion=PatternTradeNotificationInterpreter.Version,IsResearchOnly=true,SourceRunId=runId,Notifications=values};
    }

    private static DateTime Parse(string value)=>DateTime.Parse(value,null,DateTimeStyles.RoundtripKind).ToUniversalTime();
    private static DateTime Utc(DateTime value)=>value.Kind switch{DateTimeKind.Utc=>value,DateTimeKind.Unspecified=>DateTime.SpecifyKind(value,DateTimeKind.Utc),_=>value.ToUniversalTime()};
}
