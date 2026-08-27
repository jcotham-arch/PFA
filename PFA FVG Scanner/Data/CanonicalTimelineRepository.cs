using System.Globalization;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Timeline;

namespace PFA_FVG_Scanner.Data;

public sealed class CanonicalTimelineRepository
{
    private readonly PfaDatabase _database;
    public CanonicalTimelineRepository(PfaDatabase database) => _database = database;

    public async Task<CanonicalBarWriteResult> WriteAsync(CanonicalizedBarCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var existing = await GetRevisionsAsync(connection, transaction, candidate.Bar.CanonicalBarId, cancellationToken);
        var equivalent = existing.FirstOrDefault(x => x.ContentHash == candidate.Bar.ContentHash);
        CanonicalBar bar;
        bool created;
        bool conflict;
        if (equivalent is not null)
        {
            bar = equivalent with { CorrectionState = CorrectionState.DuplicateEquivalent };
            created = false;
            conflict = false;
        }
        else
        {
            var revision = existing.Count == 0 ? 1 : existing.Max(x => x.Revision) + 1;
            var providers = await GetProvidersAsync(connection, transaction, candidate.Bar.CanonicalBarId, cancellationToken);
            conflict = existing.Count > 0 && providers.Any(x =>
                !string.Equals(x, candidate.Source.Provider, StringComparison.OrdinalIgnoreCase));
            var state = existing.Count == 0 ? CorrectionState.Original
                : conflict ? CorrectionState.ProviderConflict : CorrectionState.CorrectedRevision;
            var flags = candidate.Bar.QualityFlags
                | (conflict ? MarketDataQualityFlags.ProviderConflict : MarketDataQualityFlags.None)
                | (existing.Count > 0 && !conflict ? MarketDataQualityFlags.Corrected : MarketDataQualityFlags.None);
            bar = candidate.Bar with { Revision = revision, CorrectionState = state, QualityFlags = flags };
            await InsertBarAsync(connection, transaction, bar, cancellationToken);
            created = true;
        }
        var source = candidate.Source with { Revision = bar.Revision };
        var addedSource = await InsertSourceAsync(connection, transaction, source, cancellationToken) > 0;
        await transaction.CommitAsync(cancellationToken);
        return new(bar, created, addedSource, equivalent is not null, conflict);
    }

    public async Task<IReadOnlyList<CanonicalBar>> GetHistoryAsync(string canonicalBarId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await GetRevisionsAsync(connection, null, canonicalBarId, cancellationToken);
    }

    public async Task<IReadOnlyList<CanonicalBar>> GetCurrentBarsAsync(string instrumentId,
        string timeframe, CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.CanonicalBarId, b.Revision, b.InstrumentId, b.ContractId, b.ProviderSymbol,
                   b.Timeframe, b.OpenTimeUtc, b.CloseTimeUtc, b.Open, b.High, b.Low, b.Close,
                   b.Volume, b.IsComplete, b.TradingSessionId, b.TradingDate,
                   b.CanonicalizationVersion, b.TransformationVersion, b.CorrectionState,
                   b.QualityFlags, b.RevisionEffectiveUtc, b.ContentHash
            FROM CanonicalBars b
            INNER JOIN (
                SELECT CanonicalBarId, MAX(Revision) Revision
                FROM CanonicalBars GROUP BY CanonicalBarId
            ) latest ON latest.CanonicalBarId=b.CanonicalBarId AND latest.Revision=b.Revision
            WHERE b.InstrumentId=$instrument AND b.Timeframe=$timeframe AND b.IsComplete=1
            ORDER BY b.OpenTimeUtc;
            """;
        command.Parameters.AddWithValue("$instrument", instrumentId.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$timeframe", timeframe.Trim().ToLowerInvariant());
        var result = new List<CanonicalBar>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(ReadBar(reader));
        return result;
    }

    private static async Task<List<CanonicalBar>> GetRevisionsAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string id, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CanonicalBarId, Revision, InstrumentId, ContractId, ProviderSymbol, Timeframe,
                   OpenTimeUtc, CloseTimeUtc, Open, High, Low, Close, Volume, IsComplete,
                   TradingSessionId, TradingDate, CanonicalizationVersion, TransformationVersion,
                   CorrectionState, QualityFlags, RevisionEffectiveUtc, ContentHash
            FROM CanonicalBars WHERE CanonicalBarId=$id ORDER BY Revision;
            """;
        command.Parameters.AddWithValue("$id", id);
        var result = new List<CanonicalBar>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(ReadBar(reader));
        return result;
    }

    private static async Task<HashSet<string>> GetProvidersAsync(SqliteConnection connection,
        SqliteTransaction transaction, string id, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT DISTINCT Provider FROM CanonicalBarSources WHERE CanonicalBarId=$id";
        command.Parameters.AddWithValue("$id", id);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task InsertBarAsync(SqliteConnection connection, SqliteTransaction transaction,
        CanonicalBar bar, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CanonicalBars VALUES
            ($id,$revision,$instrument,$contract,$providerSymbol,$timeframe,$openTime,$closeTime,
             $open,$high,$low,$close,$volume,$complete,$session,$date,$canonicalVersion,
             $transformationVersion,$correction,$quality,$effective,$hash);
            """;
        Add(command, "$id", bar.CanonicalBarId); Add(command, "$revision", bar.Revision);
        Add(command, "$instrument", bar.InstrumentId); Add(command, "$contract", bar.ContractId);
        Add(command, "$providerSymbol", bar.ProviderSymbol); Add(command, "$timeframe", bar.Timeframe);
        Add(command, "$openTime", bar.OpenTimeUtc.ToString("O")); Add(command, "$closeTime", bar.CloseTimeUtc.ToString("O"));
        Add(command, "$open", Format(bar.Open)); Add(command, "$high", Format(bar.High)); Add(command, "$low", Format(bar.Low));
        Add(command, "$close", Format(bar.Close)); Add(command, "$volume", Format(bar.Volume)); Add(command, "$complete", bar.IsComplete ? 1 : 0);
        Add(command, "$session", bar.TradingSessionId); Add(command, "$date", bar.TradingDate.ToString("yyyy-MM-dd"));
        Add(command, "$canonicalVersion", bar.CanonicalizationVersion); Add(command, "$transformationVersion", bar.TransformationVersion);
        Add(command, "$correction", bar.CorrectionState.ToString()); Add(command, "$quality", (int)bar.QualityFlags);
        Add(command, "$effective", bar.RevisionEffectiveUtc.ToString("O")); Add(command, "$hash", bar.ContentHash);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<int> InsertSourceAsync(SqliteConnection connection, SqliteTransaction transaction,
        CanonicalBarSource source, CancellationToken token)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO CanonicalBarSources
            (SourceId,CanonicalBarId,Revision,Provider,ProviderSymbol,SourceEventType,SourceResolution,
             SourceTimestampUtc,ReceivedTimestampUtc,SourceVersion,IngestionRunId,RawReference)
            VALUES ($source,$bar,$revision,$provider,$symbol,$type,$resolution,$sourceTime,$received,
                    $version,$run,$raw);
            """;
        Add(command, "$source", source.SourceId); Add(command, "$bar", source.CanonicalBarId);
        Add(command, "$revision", source.Revision); Add(command, "$provider", source.Provider);
        Add(command, "$symbol", source.ProviderSymbol); Add(command, "$type", source.SourceEventType);
        Add(command, "$resolution", source.SourceResolution); Add(command, "$sourceTime", source.SourceTimestampUtc.ToString("O"));
        Add(command, "$received", source.ReceivedTimestampUtc.ToString("O")); Add(command, "$version", source.SourceVersion);
        Add(command, "$run", source.IngestionRunId); Add(command, "$raw", source.RawReference);
        return await command.ExecuteNonQueryAsync(token);
    }

    private static CanonicalBar ReadBar(SqliteDataReader r) => new(
        r.GetString(0), r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5),
        DateTime.Parse(r.GetString(6), null, DateTimeStyles.RoundtripKind), DateTime.Parse(r.GetString(7), null, DateTimeStyles.RoundtripKind),
        Parse(r.GetString(8)), Parse(r.GetString(9)), Parse(r.GetString(10)), Parse(r.GetString(11)), Parse(r.GetString(12)), r.GetInt32(13) == 1,
        r.GetString(14), DateOnly.Parse(r.GetString(15), CultureInfo.InvariantCulture), r.GetString(16), r.GetString(17),
        Enum.Parse<CorrectionState>(r.GetString(18)), (MarketDataQualityFlags)r.GetInt32(19),
        DateTime.Parse(r.GetString(20), null, DateTimeStyles.RoundtripKind), r.GetString(21));
    private static void Add(SqliteCommand c, string name, object? value) => c.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
