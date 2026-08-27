using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Strategies;

namespace PFA_FVG_Scanner.Data;

public sealed class StrategyRegistryRepository : IStrategyRegistry
{
    private static readonly IReadOnlyDictionary<StrategyRegistryStatus, IReadOnlySet<StrategyRegistryStatus>> Allowed =
        new Dictionary<StrategyRegistryStatus, IReadOnlySet<StrategyRegistryStatus>>
        {
            [StrategyRegistryStatus.Draft] = Set(StrategyRegistryStatus.FrozenResearch, StrategyRegistryStatus.Rejected),
            [StrategyRegistryStatus.FrozenResearch] = Set(StrategyRegistryStatus.ValidationPending, StrategyRegistryStatus.Rejected),
            [StrategyRegistryStatus.ValidationPending] = Set(StrategyRegistryStatus.ValidationComplete, StrategyRegistryStatus.Rejected),
            [StrategyRegistryStatus.ValidationComplete] = Set(StrategyRegistryStatus.Rejected),
            [StrategyRegistryStatus.Rejected] = Set()
        };
    private readonly PfaDatabase _database;
    public StrategyRegistryRepository(PfaDatabase database) => _database = database;

    public async Task<StrategyRegistryEntry> RegisterAsync(ImmutableStrategyDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var hash = definition.ContentHash();
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = "SELECT ContentHash FROM StrategyDefinitions WHERE StrategyId=$id AND StrategyVersion=$version";
            existing.Parameters.AddWithValue("$id", definition.StrategyId);
            existing.Parameters.AddWithValue("$version", definition.StrategyVersion);
            var scalar = await existing.ExecuteScalarAsync(cancellationToken);
            var current = scalar is null or DBNull ? null : Convert.ToString(scalar, CultureInfo.InvariantCulture);
            if (current is not null && current != hash)
                throw new InvalidOperationException("A material strategy change requires a new StrategyVersion.");
            if (current == hash)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (await FindAsync(definition.StrategyId, definition.StrategyVersion, cancellationToken))!;
            }
        }
        var created = Utc(definition.CreatedAtUtc);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO StrategyDefinitions
                    (StrategyId, StrategyVersion, FamilyId, DisplayName, Environment, ContentHash,
                     DefinitionJson, EngineManifestJson, DiscoveryDatasetId, ValidationDatasetId,
                     Author, CompatibilitySource, CreatedAtUtc)
                VALUES ($id,$version,$family,$name,$environment,$hash,$definition,$manifest,
                    $discovery,$validation,$author,$source,$created);
                INSERT INTO StrategyLifecycleEvents
                    (LifecycleEventId, StrategyId, StrategyVersion, FromStatus, ToStatus,
                     Reason, Actor, OccurredAtUtc)
                VALUES ($eventId,$id,$version,NULL,'Draft','initial-registration',$author,$created);
                """;
            Add(command, "$id", definition.StrategyId); Add(command, "$version", definition.StrategyVersion);
            Add(command, "$family", definition.FamilyId); Add(command, "$name", definition.DisplayName);
            Add(command, "$environment", definition.Environment); Add(command, "$hash", hash);
            Add(command, "$definition", JsonSerializer.Serialize(definition));
            Add(command, "$manifest", JsonSerializer.Serialize(definition.EngineManifest));
            Add(command, "$discovery", definition.DiscoveryDatasetId); Add(command, "$validation", definition.ValidationDatasetId);
            Add(command, "$author", definition.Author); Add(command, "$source", definition.CompatibilitySource);
            Add(command, "$created", created.ToString("O"));
            Add(command, "$eventId", $"{definition.StrategyId}|{definition.StrategyVersion}|Draft");
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var requirement in definition.Requirements)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO StrategyRequirements
                    (StrategyId, StrategyVersion, RequirementType, ReferenceId, ReferenceVersion, Role, IsRequired)
                VALUES ($id,$version,$type,$reference,$referenceVersion,$role,$required);
                """;
            Add(command, "$id", definition.StrategyId); Add(command, "$version", definition.StrategyVersion);
            Add(command, "$type", requirement.RequirementType); Add(command, "$reference", requirement.ReferenceId);
            Add(command, "$referenceVersion", requirement.ReferenceVersion); Add(command, "$role", requirement.Role);
            Add(command, "$required", requirement.IsRequired ? 1 : 0); await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var evidence in definition.EvidenceLinks)
        {
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO StrategyEvidenceLinks
                    (StrategyId, StrategyVersion, EvidenceType, EvidenceId, DatasetId, KnownAtUtc)
                VALUES ($id,$version,$type,$evidence,$dataset,$known);
                """;
            Add(command, "$id", definition.StrategyId); Add(command, "$version", definition.StrategyVersion);
            Add(command, "$type", evidence.EvidenceType); Add(command, "$evidence", evidence.EvidenceId);
            Add(command, "$dataset", evidence.DatasetId); Add(command, "$known", Utc(evidence.KnownAtUtc).ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new(definition, hash, StrategyRegistryStatus.Draft, created, "initial-registration");
    }

    public async Task<StrategyRegistryEntry> TransitionAsync(string strategyId, string strategyVersion,
        StrategyRegistryStatus target, string reason, string actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Lifecycle transitions require a reason and actor.");
        var existing = await FindAsync(strategyId, strategyVersion, cancellationToken)
            ?? throw new KeyNotFoundException("Strategy version was not found.");
        if (!Allowed.TryGetValue(existing.Status, out var targets) || !targets.Contains(target))
            throw new UnauthorizedAccessException($"Transition {existing.Status} -> {target} is not authorized in Phase 10.");
        var occurred = DateTime.UtcNow;
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StrategyLifecycleEvents
                (LifecycleEventId,StrategyId,StrategyVersion,FromStatus,ToStatus,Reason,Actor,OccurredAtUtc)
            VALUES ($event,$id,$version,$from,$to,$reason,$actor,$occurred);
            """;
        Add(command, "$event", Guid.NewGuid().ToString("N")); Add(command, "$id", strategyId);
        Add(command, "$version", strategyVersion); Add(command, "$from", existing.Status.ToString());
        Add(command, "$to", target.ToString()); Add(command, "$reason", reason); Add(command, "$actor", actor);
        Add(command, "$occurred", occurred.ToString("O")); await command.ExecuteNonQueryAsync(cancellationToken);
        return existing with { Status = target, StatusChangedAtUtc = occurred, StatusReason = reason };
    }

    public async Task<StrategyRegistryEntry?> FindAsync(string strategyId, string strategyVersion,
        CancellationToken cancellationToken = default)
    {
        var all = await ReadAsync(strategyId, strategyVersion, cancellationToken);
        return all.SingleOrDefault();
    }
    public Task<IReadOnlyList<StrategyRegistryEntry>> GetAllAsync(CancellationToken cancellationToken = default) =>
        ReadAsync(null, null, cancellationToken);

    private async Task<IReadOnlyList<StrategyRegistryEntry>> ReadAsync(string? id, string? version,
        CancellationToken cancellationToken)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.DefinitionJson,d.ContentHash,e.ToStatus,e.OccurredAtUtc,e.Reason
            FROM StrategyDefinitions d JOIN StrategyLifecycleEvents e ON e.LifecycleEventId =
                (SELECT e2.LifecycleEventId FROM StrategyLifecycleEvents e2
                 WHERE e2.StrategyId=d.StrategyId AND e2.StrategyVersion=d.StrategyVersion
                 ORDER BY e2.OccurredAtUtc DESC, e2.rowid DESC LIMIT 1)
            WHERE ($id IS NULL OR d.StrategyId=$id) AND ($version IS NULL OR d.StrategyVersion=$version)
            ORDER BY d.FamilyId,d.StrategyId,d.StrategyVersion;
            """;
        Add(command, "$id", id); Add(command, "$version", version);
        var values = new List<StrategyRegistryEntry>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(new(JsonSerializer.Deserialize<ImmutableStrategyDefinition>(reader.GetString(0))!, reader.GetString(1),
                Enum.Parse<StrategyRegistryStatus>(reader.GetString(2)), DateTime.Parse(reader.GetString(3), null,
                    DateTimeStyles.RoundtripKind), reader.GetString(4)));
        return values;
    }
    private static IReadOnlySet<StrategyRegistryStatus> Set(params StrategyRegistryStatus[] values) => values.ToHashSet();
    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static DateTime Utc(DateTime value) => value.Kind switch
    { DateTimeKind.Utc => value, DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc), _ => value.ToUniversalTime() };
}
