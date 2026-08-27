using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Sequences;

namespace PFA_FVG_Scanner.Data;

public sealed class MarketSequenceRepository
{
    private readonly PfaDatabase _database;
    public MarketSequenceRepository(PfaDatabase database) => _database = database;

    public async Task SaveAsync(MarketSequenceDefinition definition, MarketSequenceInstance instance,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO MarketSequenceDefinitions
                    (SequenceDefinitionId, Version, DisplayName, MaximumTransitionSeconds,
                     RequireSameDirection, DefinitionJson, CreatedAtUtc)
                VALUES ($definitionId, $version, $name, $seconds, $sameDirection, $json, $createdAt);
                INSERT OR IGNORE INTO MarketSequenceInstances
                    (SequenceInstanceId, SequenceDefinitionId, SequenceDefinitionVersion,
                     InstrumentId, ContractId, Timeframe, TradingSessionId, TradingDate, State,
                     CurrentStageIndex, StartedAtUtc, UpdatedAtUtc, PointInTimeConfidence,
                     TerminationReason, CreatedAtUtc)
                VALUES ($instanceId, $definitionId, $version, $instrument, $contract, $timeframe,
                    $session, $date, $state, $stage, $started, $updated, $confidence, $reason, $createdAt);
                """;
            command.Parameters.AddWithValue("$definitionId", definition.SequenceDefinitionId);
            command.Parameters.AddWithValue("$version", definition.Version);
            command.Parameters.AddWithValue("$name", definition.DisplayName);
            command.Parameters.AddWithValue("$seconds", (long)definition.MaximumTransitionDuration.TotalSeconds);
            command.Parameters.AddWithValue("$sameDirection", definition.RequireSameDirection ? 1 : 0);
            command.Parameters.AddWithValue("$json", System.Text.Json.JsonSerializer.Serialize(definition));
            command.Parameters.AddWithValue("$instanceId", instance.SequenceInstanceId);
            command.Parameters.AddWithValue("$instrument", instance.InstrumentId);
            command.Parameters.AddWithValue("$contract", (object?)instance.ContractId ?? DBNull.Value);
            command.Parameters.AddWithValue("$timeframe", instance.Timeframe);
            command.Parameters.AddWithValue("$session", instance.TradingSessionId);
            command.Parameters.AddWithValue("$date", instance.TradingDate.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$state", instance.State.ToString());
            command.Parameters.AddWithValue("$stage", instance.CurrentStageIndex);
            command.Parameters.AddWithValue("$started", instance.StartedAtUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$updated", instance.UpdatedAtUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$confidence", instance.PointInTimeConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$reason", (object?)instance.TerminationReason ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var member in instance.Members)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO MarketSequenceMembers
                    (SequenceInstanceId, ObservationId, ObservationRevision, Role, Ordinal, JoinedAtUtc)
                VALUES ($id, $observation, $revision, $role, $ordinal, $joined);
                """;
            command.Parameters.AddWithValue("$id", instance.SequenceInstanceId);
            command.Parameters.AddWithValue("$observation", member.ObservationId);
            command.Parameters.AddWithValue("$revision", member.ObservationRevision);
            command.Parameters.AddWithValue("$role", member.Role);
            command.Parameters.AddWithValue("$ordinal", member.Ordinal);
            command.Parameters.AddWithValue("$joined", member.JoinedAtUtc.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var transition in instance.Transitions.Select((value, index) => (value, index)))
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO MarketSequenceTransitions
                    (SequenceInstanceId, Ordinal, FromRole, ToRole, OccurredAtUtc,
                     DurationMilliseconds, PointInTimeConfidence)
                VALUES ($id, $ordinal, $from, $to, $occurred, $duration, $confidence);
                """;
            command.Parameters.AddWithValue("$id", instance.SequenceInstanceId);
            command.Parameters.AddWithValue("$ordinal", transition.index + 1);
            command.Parameters.AddWithValue("$from", transition.value.FromRole);
            command.Parameters.AddWithValue("$to", transition.value.ToRole);
            command.Parameters.AddWithValue("$occurred", transition.value.OccurredAtUtc.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$duration", (long)transition.value.Duration.TotalMilliseconds);
            command.Parameters.AddWithValue("$confidence", transition.value.PointInTimeConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
