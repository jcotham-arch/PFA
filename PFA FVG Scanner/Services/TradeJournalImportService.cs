using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed partial class TradeJournalImportService(PfaDatabase database)
{
    public const string Version="trade-journal-import-1.0.0";

    public async Task<TradeJournalImportManifest> ImportAsync(Stream source,string fileName,
        CancellationToken token=default)
    {
        ArgumentNullException.ThrowIfNull(source);if(!source.CanRead)throw new ArgumentException("Trade journal stream must be readable.");
        await using var memory=new MemoryStream();await source.CopyToAsync(memory,token);var bytes=memory.ToArray();
        if(bytes.Length==0)throw new ArgumentException("Trade journal file is empty.");
        var sourceHash=Convert.ToHexString(SHA256.HashData(bytes));var importId=$"TJI-{Hash($"{Version}|{sourceHash}")[..32]}";
        var existing=await FindAsync(importId,token);if(existing is not null)return existing;
        var executions=Parse(bytes,importId);if(executions.Count==0)throw new InvalidOperationException("No valid trade executions were found.");
        var episodes=BuildEpisodes(executions);if(episodes.Count==0)throw new InvalidOperationException("No closed trade episodes could be reconstructed.");
        var gross=episodes.Sum(x=>x.GrossProfit);var costs=episodes.Sum(x=>x.EstimatedCosts);var net=episodes.Sum(x=>x.NetProfit);
        var wins=episodes.Count(x=>x.NetProfit>0);var losses=episodes.Count(x=>x.NetProfit<0);
        var gains=episodes.Where(x=>x.NetProfit>0).Sum(x=>x.NetProfit);var loss=Math.Abs(episodes.Where(x=>x.NetProfit<0).Sum(x=>x.NetProfit));
        var warnings=new List<string>();
        if(executions.Any(x=>x.InstrumentId is not("MES" or "ES")))warnings.Add("One or more instruments have no configured contract multiplier; gross profit and costs are unavailable for those episodes.");
        var manifest=new TradeJournalImportManifest(importId,Version,Path.GetFileName(fileName),sourceHash,DateTime.UtcNow,
            executions.Count,executions.Count,episodes.Count,executions.Min(x=>x.MovementTimeUtc),executions.Max(x=>x.MovementTimeUtc),
            gross,costs,net,wins,losses,episodes.Count==0?0:wins/(decimal)episodes.Count,loss==0?0:gains/loss,
            executions.Select(x=>x.InstrumentId).Distinct().Order().ToArray(),
            executions.Select(x=>x.ContractId).Distinct().Order().ToArray(),warnings);
        await Persist(manifest,executions,episodes,token);return manifest;
    }

    public async Task<IReadOnlyList<TradeJournalImportManifest>> GetImportsAsync(CancellationToken token=default)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT ManifestJson FROM TradeJournalImports ORDER BY ImportedAtUtc DESC";var values=new List<TradeJournalImportManifest>();
        await using var reader=await command.ExecuteReaderAsync(token);while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<TradeJournalImportManifest>(reader.GetString(0))!);return values;}

    public async Task<IReadOnlyList<TradeJournalEpisode>> GetEpisodesAsync(string importId,CancellationToken token=default)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT EpisodeJson FROM TradeJournalEpisodes WHERE ImportId=$id ORDER BY OpenedAtUtc,EpisodeId";
        command.Parameters.AddWithValue("$id",importId);var values=new List<TradeJournalEpisode>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<TradeJournalEpisode>(reader.GetString(0))!);return values;}

    private async Task<TradeJournalImportManifest?> FindAsync(string id,CancellationToken token)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT ManifestJson FROM TradeJournalImports WHERE ImportId=$id";command.Parameters.AddWithValue("$id",id);
        var json=await command.ExecuteScalarAsync(token) as string;return json is null?null:JsonSerializer.Deserialize<TradeJournalImportManifest>(json);}

    private static List<TradeJournalExecution> Parse(byte[] bytes,string importId)
    {
        using var stream=new MemoryStream(bytes);using var parser=new TextFieldParser(stream,Encoding.UTF8)
        {TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=false};
        parser.SetDelimiters(",");var headers=parser.ReadFields()??throw new InvalidOperationException("Trade journal header is missing.");
        var expected=new[]{"name","order_id","symbol","mov_time","mov_type","exec_qty","price_done","points","profit","created_on"};
        if(!headers.SequenceEqual(expected,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException($"Unsupported trade journal columns. Expected: {string.Join(',',expected)}.");
        var values=new List<TradeJournalExecution>();var row=1;
        while(!parser.EndOfData)
        {
            row++;var fields=parser.ReadFields();if(fields is null||fields.All(string.IsNullOrWhiteSpace))continue;
            if(fields.Length!=expected.Length)throw new InvalidOperationException($"Trade journal row {row} has {fields.Length} fields; expected {expected.Length}.");
            var movement=(TradeJournalMovement)RequiredInt(fields[4],row,"mov_type");
            if(!Enum.IsDefined(movement))throw new InvalidOperationException($"Trade journal row {row} has unsupported movement type {fields[4]}.");
            var quantity=RequiredInt(fields[5],row,"exec_qty");ValidateQuantity(movement,quantity,row);
            var symbol=NormalizeSymbol(fields[2]);var (instrument,contract)=ResolveContract(symbol);
            var movementTime=ParseMovementTime(fields[3],row);var accountHash=Hash(fields[0].Trim());var orderHash=Hash(fields[1].Trim());
            var sourceEpisodeKey=Hash($"{accountHash}|{symbol}|{DateTimeOffset.Parse(fields[9],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal).ToUniversalTime():O}");
            var pointContracts=OptionalDecimal(fields[7],row,"points");var net=OptionalDecimal(fields[8],row,"profit");
            var price=RequiredDecimal(fields[6],row,"price_done");
            var seed=JsonSerializer.Serialize(new{importId,row,accountHash,orderHash,symbol,movementTime,movement,quantity,price,pointContracts,net});
            var hash=Hash(seed);values.Add(new($"TJE-{hash[..32]}",importId,accountHash,orderHash,sourceEpisodeKey,symbol,instrument,contract,
                movementTime,movement,quantity,price,pointContracts,net,row,hash));
        }
        return values;
    }

    private static List<TradeJournalEpisode> BuildEpisodes(IReadOnlyList<TradeJournalExecution> executions)
    {
        var values=new List<TradeJournalEpisode>();
        foreach(var group in executions.GroupBy(x=>new{x.AccountHash,x.ProviderSymbol,x.SourceEpisodeKey}))
        {
            var ordered=group.OrderBy(x=>x.MovementTimeUtc).ThenBy(x=>x.SourceRow).ToArray();
            var openLong=ordered.Any(x=>x.Movement==TradeJournalMovement.OpenLong);var openShort=ordered.Any(x=>x.Movement==TradeJournalMovement.OpenShort);
            if(openLong==openShort)continue;var direction=openLong?TradeJournalDirection.Long:TradeJournalDirection.Short;
            var realized=ordered.Where(x=>x.Movement is TradeJournalMovement.CloseLong or TradeJournalMovement.CloseShort&&x.NetProfit is not null).ToArray();
            if(realized.Length==0)continue;var points=realized.Sum(x=>x.PointContracts??0);var net=realized.Sum(x=>x.NetProfit??0);
            var multiplier=Multiplier(ordered[0].InstrumentId);var gross=multiplier is null?net:points*multiplier.Value;var costs=gross-net;
            var opened=ordered.Min(x=>x.MovementTimeUtc);var closed=ordered.Max(x=>x.MovementTimeUtc);
            var seed=JsonSerializer.Serialize(new{group.Key.AccountHash,group.Key.ProviderSymbol,group.Key.SourceEpisodeKey,direction,
                ExecutionIds=ordered.Select(x=>x.ExecutionId)});var hash=Hash(seed);
            values.Add(new($"TJEP-{hash[..32]}",ordered[0].ImportId,group.Key.AccountHash,ordered[0].ProviderSymbol,
                ordered[0].InstrumentId,ordered[0].ContractId,direction,opened,closed,ordered.Max(x=>Math.Abs(x.SignedQuantity)),
                ordered.Length,realized.Length,points,gross,costs,net,(decimal)(closed-opened).TotalMinutes,
                net>0?"Win":net<0?"Loss":"Breakeven",hash));
        }
        return values.OrderBy(x=>x.OpenedAtUtc).ThenBy(x=>x.EpisodeId).ToList();
    }

    private async Task Persist(TradeJournalImportManifest manifest,IReadOnlyList<TradeJournalExecution> executions,
        IReadOnlyList<TradeJournalEpisode> episodes,CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO TradeJournalImports
            (ImportId,ImporterVersion,SourceFileName,SourceContentHash,SourceRows,ExecutionCount,EpisodeCount,
             EarliestExecutionUtc,LatestExecutionUtc,NetProfit,ManifestJson,ImportedAtUtc,CanActivateStrategy,CanRouteToRealBroker)
            VALUES($id,$version,$file,$sourceHash,$rows,$executions,$episodes,$earliest,$latest,$profit,$json,$imported,0,0);
            """;Add(command,"$id",manifest.ImportId);Add(command,"$version",manifest.ImporterVersion);Add(command,"$file",manifest.SourceFileName);
            Add(command,"$sourceHash",manifest.SourceContentHash);Add(command,"$rows",manifest.SourceRows);Add(command,"$executions",manifest.ExecutionCount);
            Add(command,"$episodes",manifest.EpisodeCount);Add(command,"$earliest",manifest.EarliestExecutionUtc.ToString("O"));Add(command,"$latest",manifest.LatestExecutionUtc.ToString("O"));
            Add(command,"$profit",manifest.NetProfit.ToString(CultureInfo.InvariantCulture));Add(command,"$json",JsonSerializer.Serialize(manifest));Add(command,"$imported",manifest.ImportedAtUtc.ToString("O"));await command.ExecuteNonQueryAsync(token);}
        foreach(var execution in executions){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO TradeJournalExecutions
            (ImportId,ExecutionId,AccountHash,InstrumentId,ContractId,MovementTimeUtc,Movement,SourceRow,ContentHash,ExecutionJson)
            VALUES($import,$id,$account,$instrument,$contract,$time,$movement,$row,$hash,$json);
            """;Add(command,"$import",manifest.ImportId);Add(command,"$id",execution.ExecutionId);Add(command,"$account",execution.AccountHash);
            Add(command,"$instrument",execution.InstrumentId);Add(command,"$contract",execution.ContractId);Add(command,"$time",execution.MovementTimeUtc.ToString("O"));
            Add(command,"$movement",execution.Movement.ToString());Add(command,"$row",execution.SourceRow);Add(command,"$hash",execution.ContentHash);
            Add(command,"$json",JsonSerializer.Serialize(execution));await command.ExecuteNonQueryAsync(token);}
        foreach(var episode in episodes){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO TradeJournalEpisodes
            (ImportId,EpisodeId,InstrumentId,ContractId,Direction,OpenedAtUtc,ClosedAtUtc,NetProfit,Outcome,ContentHash,EpisodeJson)
            VALUES($import,$id,$instrument,$contract,$direction,$opened,$closed,$profit,$outcome,$hash,$json);
            """;Add(command,"$import",manifest.ImportId);Add(command,"$id",episode.EpisodeId);Add(command,"$instrument",episode.InstrumentId);
            Add(command,"$contract",episode.ContractId);Add(command,"$direction",episode.Direction.ToString());Add(command,"$opened",episode.OpenedAtUtc.ToString("O"));
            Add(command,"$closed",episode.ClosedAtUtc.ToString("O"));Add(command,"$profit",episode.NetProfit.ToString(CultureInfo.InvariantCulture));
            Add(command,"$outcome",episode.Outcome);Add(command,"$hash",episode.ContentHash);Add(command,"$json",JsonSerializer.Serialize(episode));await command.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);
    }

    private static DateTime ParseMovementTime(string value,int row)
    {var match=MovementRegex().Match(value.Trim());if(!match.Success)throw new InvalidOperationException($"Trade journal row {row} has invalid mov_time.");
        var normalized=$"{match.Groups[1].Value} {match.Groups[2].Value.Insert(3,":")}";
        return DateTimeOffset.ParseExact(normalized,"ddd MMM dd yyyy HH:mm:ss zzz",CultureInfo.InvariantCulture,DateTimeStyles.None).UtcDateTime;}
    private static string NormalizeSymbol(string value)=>value.Trim().StartsWith("CM.",StringComparison.OrdinalIgnoreCase)?value.Trim()[3..].ToUpperInvariant():value.Trim().ToUpperInvariant();
    private static (string Instrument,string Contract) ResolveContract(string symbol)
    {var match=ContractRegex().Match(symbol);return match.Success?(match.Groups[1].Value,symbol):(symbol,symbol);}
    private static decimal? Multiplier(string instrument)=>instrument switch{"MES"=>5m,"ES"=>50m,_=>null};
    private static void ValidateQuantity(TradeJournalMovement movement,int quantity,int row)
    {var positive=movement is TradeJournalMovement.OpenLong or TradeJournalMovement.CloseShort;if((positive&&quantity<=0)||(!positive&&quantity>=0))throw new InvalidOperationException($"Trade journal row {row} quantity sign conflicts with movement type.");}
    private static int RequiredInt(string value,int row,string field)=>int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var result)?result:throw new InvalidOperationException($"Trade journal row {row} has invalid {field}.");
    private static decimal RequiredDecimal(string value,int row,string field)=>decimal.TryParse(value,NumberStyles.Number,CultureInfo.InvariantCulture,out var result)?result:throw new InvalidOperationException($"Trade journal row {row} has invalid {field}.");
    private static decimal? OptionalDecimal(string value,int row,string field)=>string.IsNullOrWhiteSpace(value)?null:RequiredDecimal(value,row,field);
    private static string Hash(string value)=>AgentTrainingDatasetBuilder.Hash(value);
    private static void Add(SqliteCommand command,string name,object value)=>command.Parameters.AddWithValue(name,value);
    [GeneratedRegex(@"^(.+) GMT([+-]\d{4})(?: \(.+\))?$")]private static partial Regex MovementRegex();
    [GeneratedRegex(@"^(.+?)([FGHJKMNQUVXZ]\d{1,2})$")]private static partial Regex ContractRegex();
}
