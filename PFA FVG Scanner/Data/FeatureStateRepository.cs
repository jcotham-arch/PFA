using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Features;
using PFA_FVG_Scanner.Domain.MarketState;

namespace PFA_FVG_Scanner.Data;

public sealed class FeatureStateRepository
{
    private readonly PfaDatabase _database;
    public FeatureStateRepository(PfaDatabase database) => _database = database;

    public async Task SaveDefinitionsAsync(IEnumerable<FeatureDefinition> definitions,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        foreach (var definition in definitions)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR IGNORE INTO FeatureDefinitions
                (FeatureDefinitionId,Version,Name,ValueType,Unit,Role,InputRequirement,LookbackTicks,Description)
                VALUES ($id,$version,$name,$type,$unit,$role,$input,$lookback,$description);
                """;
            Add(command, "$id", definition.FeatureDefinitionId); Add(command, "$version", definition.Version);
            Add(command, "$name", definition.Name); Add(command, "$type", definition.ValueType.ToString());
            Add(command, "$unit", definition.Unit); Add(command, "$role", definition.Role.ToString());
            Add(command, "$input", definition.InputRequirement); Add(command, "$lookback", definition.Lookback.Ticks);
            Add(command, "$description", definition.Description);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task SaveSnapshotAsync(MarketStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO MarketStateSnapshots
                (SnapshotId,InstrumentId,ContractId,AsOfUtc,KnownAtUtc,DataRevision,EngineVersion,
                 TradingSessionId,QualityFlags,SourceReferencesJson)
                VALUES ($id,$instrument,$contract,$asof,$known,$revision,$engine,$session,$quality,$sources);
                """;
            Add(command, "$id", snapshot.MarketStateSnapshotId); Add(command, "$instrument", snapshot.InstrumentId);
            Add(command, "$contract", snapshot.ContractId); Add(command, "$asof", snapshot.AsOfUtc.ToString("O"));
            Add(command, "$known", snapshot.KnownAtUtc.ToString("O")); Add(command, "$revision", snapshot.DataRevision);
            Add(command, "$engine", snapshot.EngineVersion); Add(command, "$session", snapshot.TradingSessionId);
            Add(command, "$quality", (int)snapshot.QualityFlags); Add(command, "$sources", JsonSerializer.Serialize(snapshot.SourceCanonicalBarIds));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var value in snapshot.Facts)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO FeatureValues
                (FeatureValueId,SnapshotId,FeatureDefinitionId,FeatureDefinitionVersion,SubjectId,InstrumentId,
                 AsOfUtc,KnownAtUtc,Value,EngineVersion,DataRevision,QualityFlags,SourceReferencesJson)
                VALUES ($id,$snapshot,$definition,$definitionVersion,$subject,$instrument,$asof,$known,$value,
                        $engine,$revision,$quality,$sources);
                """;
            Add(command, "$id", value.FeatureValueId); Add(command, "$snapshot", snapshot.MarketStateSnapshotId);
            Add(command, "$definition", value.FeatureDefinitionId); Add(command, "$definitionVersion", value.FeatureDefinitionVersion);
            Add(command, "$subject", value.SubjectId); Add(command, "$instrument", value.InstrumentId);
            Add(command, "$asof", value.AsOfUtc.ToString("O")); Add(command, "$known", value.KnownAtUtc.ToString("O"));
            Add(command, "$value", value.Value); Add(command, "$engine", value.EngineVersion);
            Add(command, "$revision", value.DataRevision); Add(command, "$quality", (int)value.QualityFlags);
            Add(command, "$sources", JsonSerializer.Serialize(value.SourceReferences));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
