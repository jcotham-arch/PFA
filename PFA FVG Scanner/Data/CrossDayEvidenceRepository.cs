using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Evidence;

namespace PFA_FVG_Scanner.Data;

public sealed class CrossDayEvidenceRepository : ICrossDayEvidenceRepository
{
    private readonly PfaDatabase _database;
    public CrossDayEvidenceRepository(PfaDatabase database) => _database = database;

    public async Task SaveAsync(GeneralCrossDayEvidenceReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.CanActivateAnyStrategy || report.Signatures.Any(x => x.CanActivateStrategy))
            throw new UnauthorizedAccessException("Cross-day evidence cannot activate strategies.");
        var hash = report.ContentHash();
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT ContentHash FROM GeneralCrossDayEvidenceReports WHERE ReportId=$id";
            check.Parameters.AddWithValue("$id", report.ReportId);
            var scalar = await check.ExecuteScalarAsync(cancellationToken);
            if (scalar is not null && Convert.ToString(scalar, CultureInfo.InvariantCulture) != hash)
                throw new InvalidOperationException("Cross-day reports are immutable; use a new ReportId.");
            if (scalar is not null) { await transaction.RollbackAsync(cancellationToken); return; }
        }
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO GeneralCrossDayEvidenceReports
                    (ReportId,InstrumentId,EvidenceEngineVersion,SessionAssignmentVersion,
                     StartTradingDate,EndTradingDate,ExpectedTradingDatesJson,SourceReference,
                     ContentHash,CreatedAtUtc,CanActivateAnyStrategy)
                VALUES ($id,$instrument,$engine,$session,$start,$end,$dates,$source,$hash,$created,0);
                """;
            Add(command,"$id",report.ReportId); Add(command,"$instrument",report.InstrumentId);
            Add(command,"$engine",report.EvidenceEngineVersion); Add(command,"$session",report.SessionAssignmentVersion);
            Add(command,"$start",report.StartTradingDate.ToString("yyyy-MM-dd")); Add(command,"$end",report.EndTradingDate.ToString("yyyy-MM-dd"));
            Add(command,"$dates",JsonSerializer.Serialize(report.ExpectedTradingDates)); Add(command,"$source",report.SourceReference);
            Add(command,"$hash",hash); Add(command,"$created",report.CreatedAtUtc.ToUniversalTime().ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var signature in report.Signatures)
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO GeneralCrossDaySignatureEvidence
                        (ReportId,Signature,FamilyId,DefinitionVersion,DefinitionJson,Classification,
                         TotalTradingDays,ObservedDays,MissingTradingDatesJson,PositiveDays,NegativeDays,
                         FlatDays,TotalSamples,IndependentEvents,AggregateMetricsJson,RegimeIdsJson,
                         GatesJson,CanAdvanceToFrozenValidation,CanActivateStrategy)
                    VALUES ($report,$signature,$family,$version,$definition,$classification,$total,$observed,
                        $missing,$positive,$negative,$flat,$samples,$events,$metrics,$regimes,$gates,$advance,0);
                    """;
                Add(command,"$report",report.ReportId); Add(command,"$signature",signature.Signature);
                Add(command,"$family",signature.FamilyId); Add(command,"$version",signature.DefinitionVersion);
                Add(command,"$definition",signature.DefinitionJson); Add(command,"$classification",signature.Classification.ToString());
                Add(command,"$total",signature.TotalTradingDays); Add(command,"$observed",signature.ObservedDays);
                Add(command,"$missing",JsonSerializer.Serialize(signature.MissingTradingDates)); Add(command,"$positive",signature.PositiveDays);
                Add(command,"$negative",signature.NegativeDays); Add(command,"$flat",signature.FlatDays);
                Add(command,"$samples",signature.TotalSamples); Add(command,"$events",signature.IndependentEvents);
                Add(command,"$metrics",JsonSerializer.Serialize(signature.AggregateMetrics)); Add(command,"$regimes",JsonSerializer.Serialize(signature.RegimeIds));
                Add(command,"$gates",JsonSerializer.Serialize(signature.Gates)); Add(command,"$advance",signature.CanAdvanceToFrozenValidation?1:0);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach (var day in signature.DailyEvidence)
            {
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO GeneralCrossDayDailyEvidence
                        (ReportId,Signature,TradingDate,Samples,IndependentEvents,MetricsJson,DailyStatus,RegimeIdsJson)
                    VALUES ($report,$signature,$date,$samples,$events,$metrics,$status,$regimes);
                    """;
                Add(command,"$report",report.ReportId); Add(command,"$signature",signature.Signature);
                Add(command,"$date",day.TradingDate.ToString("yyyy-MM-dd")); Add(command,"$samples",day.Samples);
                Add(command,"$events",day.IndependentEvents); Add(command,"$metrics",JsonSerializer.Serialize(day.Metrics));
                Add(command,"$status",day.DailyStatus); Add(command,"$regimes",JsonSerializer.Serialize(day.RegimeIds));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<GeneralCrossDayEvidenceReport?> FindAsync(string reportId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection(); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT InstrumentId,EvidenceEngineVersion,SessionAssignmentVersion,StartTradingDate,EndTradingDate,ExpectedTradingDatesJson,SourceReference,CreatedAtUtc FROM GeneralCrossDayEvidenceReports WHERE ReportId=$id";
        Add(command,"$id",reportId); await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken)) return null;
        var header=(Instrument:reader.GetString(0),Engine:reader.GetString(1),Session:reader.GetString(2),Start:DateOnly.Parse(reader.GetString(3)),End:DateOnly.Parse(reader.GetString(4)),Dates:JsonSerializer.Deserialize<DateOnly[]>(reader.GetString(5))??[],Source:reader.GetString(6),Created:DateTime.Parse(reader.GetString(7),null,DateTimeStyles.RoundtripKind));
        await reader.DisposeAsync();
        var signatures=await ReadSignaturesAsync(connection,reportId,cancellationToken);
        return new(reportId,header.Instrument,header.Engine,header.Session,header.Start,header.End,header.Dates,signatures,header.Source,header.Created,false);
    }

    private static async Task<IReadOnlyList<CrossDaySignatureEvidence>> ReadSignaturesAsync(SqliteConnection connection,string reportId,CancellationToken token)
    {
        await using var command=connection.CreateCommand(); command.CommandText="SELECT Signature,FamilyId,DefinitionVersion,DefinitionJson,Classification,TotalTradingDays,ObservedDays,MissingTradingDatesJson,PositiveDays,NegativeDays,FlatDays,TotalSamples,IndependentEvents,AggregateMetricsJson,RegimeIdsJson,GatesJson,CanAdvanceToFrozenValidation FROM GeneralCrossDaySignatureEvidence WHERE ReportId=$report ORDER BY Signature"; Add(command,"$report",reportId);
        var raw=new List<object[]>(); await using(var reader=await command.ExecuteReaderAsync(token)) while(await reader.ReadAsync(token)){var row=new object[17];reader.GetValues(row);raw.Add(row);} var result=new List<CrossDaySignatureEvidence>();
        foreach(var r in raw){var signature=(string)r[0];result.Add(new(signature,(string)r[1],(string)r[2],(string)r[3],Enum.Parse<CrossDayEvidenceClassification>((string)r[4]),Convert.ToInt32(r[5]),Convert.ToInt32(r[6]),JsonSerializer.Deserialize<DateOnly[]>((string)r[7])??[],Convert.ToInt32(r[8]),Convert.ToInt32(r[9]),Convert.ToInt32(r[10]),Convert.ToInt32(r[11]),Convert.ToInt32(r[12]),JsonSerializer.Deserialize<Dictionary<string,decimal>>((string)r[13])!,JsonSerializer.Deserialize<HashSet<string>>((string)r[14])!,JsonSerializer.Deserialize<Dictionary<string,bool>>((string)r[15])!,Convert.ToInt32(r[16])==1,await ReadDaysAsync(connection,reportId,signature,token),false));} return result;
    }
    private static async Task<IReadOnlyList<CrossDayDailyEvidence>> ReadDaysAsync(SqliteConnection connection,string report,string signature,CancellationToken token){await using var command=connection.CreateCommand();command.CommandText="SELECT TradingDate,Samples,IndependentEvents,MetricsJson,DailyStatus,RegimeIdsJson FROM GeneralCrossDayDailyEvidence WHERE ReportId=$report AND Signature=$signature ORDER BY TradingDate";Add(command,"$report",report);Add(command,"$signature",signature);var values=new List<CrossDayDailyEvidence>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))values.Add(new(DateOnly.Parse(reader.GetString(0)),reader.GetInt32(1),reader.GetInt32(2),JsonSerializer.Deserialize<Dictionary<string,decimal>>(reader.GetString(3))!,reader.GetString(4),JsonSerializer.Deserialize<HashSet<string>>(reader.GetString(5))!));return values;}
    private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}
