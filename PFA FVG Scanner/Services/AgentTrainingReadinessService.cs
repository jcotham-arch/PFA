using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;

namespace PFA_FVG_Scanner.Services;

public sealed record AgentTrainingReadiness(
    long Observations,long Outcomes,long SequenceInstances,long SequenceMembers,long PointInTimeLabels,
    DateTime? EarliestObservationUtc,DateTime? LatestObservationUtc,bool SupervisedTrainingReady,
    string Status,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed class AgentTrainingReadinessService(PfaDatabase database)
{
    public async Task<AgentTrainingReadiness> GetAsync(CancellationToken token=default)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        var observations=await Count(connection,"UniversalMarketObservations",token);
        var outcomes=await Count(connection,"UniversalMarketOutcomes",token);
        var sequences=await Count(connection,"MarketSequenceInstances",token);
        var members=await Count(connection,"MarketSequenceMembers",token);
        await using var labels=connection.CreateCommand();labels.CommandText="""
            SELECT COUNT(DISTINCT o.OutcomeId) FROM UniversalMarketOutcomes o
            JOIN UniversalOutcomeMetrics m ON m.OutcomeId=o.OutcomeId
            JOIN UniversalMarketObservations x ON x.ObservationId=o.ObservationId
            WHERE lower(m.Unit)='r' AND x.KnownAtUtc<o.EvaluatedThroughUtc;
            """;var pointInTimeLabels=Convert.ToInt64(await labels.ExecuteScalarAsync(token));
        await using var range=connection.CreateCommand();range.CommandText="SELECT MIN(FormationTimeUtc),MAX(FormationTimeUtc) FROM UniversalMarketObservations";
        await using var reader=await range.ExecuteReaderAsync(token);await reader.ReadAsync(token);
        DateTime? earliest=reader.IsDBNull(0)?null:DateTime.Parse(reader.GetString(0),null,System.Globalization.DateTimeStyles.RoundtripKind);
        DateTime? latest=reader.IsDBNull(1)?null:DateTime.Parse(reader.GetString(1),null,System.Globalization.DateTimeStyles.RoundtripKind);
        var ready=pointInTimeLabels>=100&&earliest.HasValue&&latest.HasValue&&(latest.Value-earliest.Value).TotalDays>=90;
        return new(observations,outcomes,sequences,members,pointInTimeLabels,earliest,latest,ready,
            ready?"Eligible for temporally split supervised research.":"Corpus collection is active; supervised training remains gated until at least 100 R-labeled examples span 90 days.",false,false);
    }
    private static async Task<long> Count(SqliteConnection connection,string table,CancellationToken token){await using var command=connection.CreateCommand();command.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt64(await command.ExecuteScalarAsync(token));}
}
