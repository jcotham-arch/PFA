using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Domain.Evidence;

namespace PFA_FVG_Scanner.Data;

public sealed class CrossMarketEvidenceRepository : ICrossMarketEvidenceRepository
{
    private readonly PfaDatabase _database;
    public CrossMarketEvidenceRepository(PfaDatabase database) => _database = database;
    public async Task SaveAsync(CrossMarketEvidenceResult result,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if(result.InvalidatesSourceHypothesis||result.CanActivateStrategy)
            throw new UnauthorizedAccessException("Cross-market evidence cannot invalidate or activate a strategy.");
        var json=JsonSerializer.Serialize(result);var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        await using var connection=_database.CreateConnection();await connection.OpenAsync(cancellationToken);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using(var check=connection.CreateCommand()){check.Transaction=transaction;check.CommandText="SELECT ContentHash FROM CrossMarketEvidenceResults WHERE ResultId=$id";check.Parameters.AddWithValue("$id",result.ResultId);var scalar=await check.ExecuteScalarAsync(cancellationToken);if(scalar is not null&&Convert.ToString(scalar,CultureInfo.InvariantCulture)!=hash)throw new InvalidOperationException("Cross-market results are immutable; use a new ResultId.");if(scalar is not null){await transaction.RollbackAsync(cancellationToken);return;}}
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="""
            INSERT INTO CrossMarketEvidenceResults
                (ResultId,PlanId,PlanVersion,FrozenSignature,SourceInstrumentId,DatasetManifestId,
                 Classification,ComparableMarkets,PositiveComparableMarkets,NegativeComparableMarkets,
                 Summary,PlanJson,ContentHash,CreatedAtUtc,InvalidatesSourceHypothesis,CanActivateStrategy)
            VALUES($id,$plan,$version,$signature,$source,$dataset,$classification,$comparable,$positive,
                $negative,$summary,$planJson,$hash,$created,0,0);
            """;Add(command,"$id",result.ResultId);Add(command,"$plan",result.Plan.PlanId);Add(command,"$version",result.Plan.PlanVersion);Add(command,"$signature",result.Plan.FrozenSignature);Add(command,"$source",result.Plan.SourceInstrumentId);Add(command,"$dataset",result.Plan.DatasetManifestId);Add(command,"$classification",result.Classification.ToString());Add(command,"$comparable",result.ComparableMarkets);Add(command,"$positive",result.PositiveComparableMarkets);Add(command,"$negative",result.NegativeComparableMarkets);Add(command,"$summary",result.Summary);Add(command,"$planJson",JsonSerializer.Serialize(result.Plan));Add(command,"$hash",hash);Add(command,"$created",result.CreatedAtUtc.ToUniversalTime().ToString("O"));await command.ExecuteNonQueryAsync(cancellationToken);}
        foreach(var market in result.Markets){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT INTO CrossMarketInstrumentEvidence
                (ResultId,InstrumentId,Comparability,ComparabilityNotesJson,Samples,IndependentEvents,
                 ExpectancyR,NetR,AverageMovePoints,AverageMoveTicks,AverageMoveDollarsPerContract,
                 InstrumentDefinitionVersion,EvidenceReference)
            VALUES($result,$instrument,$comparability,$notes,$samples,$events,$expectancy,$net,$points,
                $ticks,$dollars,$definition,$evidence);
            """;Add(command,"$result",result.ResultId);Add(command,"$instrument",market.InstrumentId);Add(command,"$comparability",market.Comparability.ToString());Add(command,"$notes",JsonSerializer.Serialize(market.ComparabilityNotes));Add(command,"$samples",market.Samples);Add(command,"$events",market.IndependentEvents);Add(command,"$expectancy",Format(market.ExpectancyR));Add(command,"$net",Format(market.NetR));Add(command,"$points",Format(market.AverageMovePoints));Add(command,"$ticks",market.AverageMoveTicks.HasValue?Format(market.AverageMoveTicks.Value):null);Add(command,"$dollars",market.AverageMoveDollarsPerContract.HasValue?Format(market.AverageMoveDollarsPerContract.Value):null);Add(command,"$definition",market.InstrumentDefinitionVersion);Add(command,"$evidence",market.EvidenceReference);await command.ExecuteNonQueryAsync(cancellationToken);}
        await transaction.CommitAsync(cancellationToken);
    }
    public async Task<CrossMarketEvidenceResult?> FindAsync(string resultId,CancellationToken cancellationToken=default)
    {
        await using var connection=_database.CreateConnection();await connection.OpenAsync(cancellationToken);await using var command=connection.CreateCommand();command.CommandText="SELECT PlanJson,Classification,ComparableMarkets,PositiveComparableMarkets,NegativeComparableMarkets,Summary,CreatedAtUtc FROM CrossMarketEvidenceResults WHERE ResultId=$id";Add(command,"$id",resultId);await using var reader=await command.ExecuteReaderAsync(cancellationToken);if(!await reader.ReadAsync(cancellationToken))return null;var header=(Plan:JsonSerializer.Deserialize<CrossMarketEvidencePlan>(reader.GetString(0))!,Classification:Enum.Parse<CrossMarketClassification>(reader.GetString(1)),Comparable:reader.GetInt32(2),Positive:reader.GetInt32(3),Negative:reader.GetInt32(4),Summary:reader.GetString(5),Created:DateTime.Parse(reader.GetString(6),null,DateTimeStyles.RoundtripKind));await reader.DisposeAsync();var markets=await ReadMarkets(connection,resultId,cancellationToken);return new(resultId,header.Plan,header.Classification,markets,header.Comparable,header.Positive,header.Negative,header.Summary,header.Created,false,false);
    }
    private static async Task<IReadOnlyList<NormalizedMarketEvidence>> ReadMarkets(SqliteConnection connection,string result,CancellationToken token){await using var command=connection.CreateCommand();command.CommandText="SELECT InstrumentId,Comparability,ComparabilityNotesJson,Samples,IndependentEvents,ExpectancyR,NetR,AverageMovePoints,AverageMoveTicks,AverageMoveDollarsPerContract,InstrumentDefinitionVersion,EvidenceReference FROM CrossMarketInstrumentEvidence WHERE ResultId=$result ORDER BY rowid";Add(command,"$result",result);var values=new List<NormalizedMarketEvidence>();await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))values.Add(new(reader.GetString(0),Enum.Parse<MarketComparability>(reader.GetString(1)),JsonSerializer.Deserialize<string[]>(reader.GetString(2))??[],reader.GetInt32(3),reader.GetInt32(4),Parse(reader.GetString(5)),Parse(reader.GetString(6)),Parse(reader.GetString(7)),reader.IsDBNull(8)?null:Parse(reader.GetString(8)),reader.IsDBNull(9)?null:Parse(reader.GetString(9)),reader.GetString(10),reader.GetString(11)));return values;}
    private static void Add(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value);private static string Format(decimal v)=>v.ToString("G29",CultureInfo.InvariantCulture);private static decimal Parse(string v)=>decimal.Parse(v,CultureInfo.InvariantCulture);
}
