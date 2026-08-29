using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed class TradeJournalMarketAlignmentService(PfaDatabase database,TradeJournalImportService journals,
    DailyMarketDiscoveryService dailyDiscovery)
{
    public const string Version="trade-journal-market-alignment-1.2.0";

    public async Task<TradeJournalAlignmentReport> BuildAsync(string importId,CancellationToken token=default)
    {
        var episodes=await journals.GetEpisodesAsync(importId,token);
        if(episodes.Count==0)throw new KeyNotFoundException($"Trade journal import '{importId}' was not found.");
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        var alignments=new List<TradeJournalEpisodeAlignment>();var studies=new Dictionary<DateOnly,DailyMarketDiscoveryStudy>();
        foreach(var episode in episodes)
        {
            var bar=await LatestBar(connection,episode,token);var matches=await PatternMatches(connection,episode,token);
            var structural=await StructuralMatches(episode,studies,token);
            var hash=AgentTrainingDatasetBuilder.Hash(JsonSerializer.Serialize(new{episode.EpisodeId,Bar=bar?.Id,
                Matches=matches.Select(x=>new{x.ObservationId,x.ObservationRevision,x.ObservationContentHash}),
                Structural=structural.Select(x=>x.EventId)}));
            alignments.Add(new(episode.EpisodeId,episode.InstrumentId,episode.ContractId,episode.Direction,
                episode.OpenedAtUtc,episode.NetProfit,bar is not null,bar?.Id,bar?.CloseTimeUtc,matches,structural,hash));
        }
        var metrics=alignments.SelectMany(x=>x.PatternMatches.Select(match=>new BehaviorRow(x,match)))
            .GroupBy(x=>new{x.Match.ModuleId,x.Match.PatternType})
            .Select(group=>Metric(group.Key.ModuleId,group.Key.PatternType,group
                .GroupBy(x=>x.Alignment.EpisodeId).Select(x=>x.OrderBy(y=>y.Match.MinutesBeforeEntry).First()).ToArray()))
            .OrderByDescending(x=>x.MatchedEpisodes).ThenBy(x=>x.ModuleId).ThenBy(x=>x.PatternType).ToArray();
        var structuralMetrics=alignments.SelectMany(x=>x.StructuralEventMatches.Select(match=>new StructuralRow(x,match)))
            .GroupBy(x=>x.Match.EventType).Select(group=>StructuralMetric(group.Key,group
                .GroupBy(x=>x.Alignment.EpisodeId).Select(x=>x.OrderBy(y=>y.Match.MinutesBeforeEntry).First()).ToArray()))
            .OrderByDescending(x=>x.MatchedEpisodes).ThenBy(x=>x.EventType).ToArray();
        var directionalSegments=alignments.SelectMany(x=>x.PatternMatches.Select(match=>new DirectionalRow(
                "RegisteredPattern",match.ModuleId,match.DirectionAgrees,x.NetProfit,x.EpisodeId)))
            .Concat(alignments.SelectMany(x=>x.StructuralEventMatches.Select(match=>new DirectionalRow(
                "StructuralEvent",match.EventType,match.DirectionAgrees,x.NetProfit,x.EpisodeId))))
            .GroupBy(x=>new{x.SourceKind,x.SignalType,x.DirectionAgrees})
            .Select(group=>DirectionalMetric(group.Key.SourceKind,group.Key.SignalType,group.Key.DirectionAgrees,
                group.GroupBy(x=>x.EpisodeId).Select(x=>x.First()).ToArray()))
            .OrderBy(x=>x.SourceKind).ThenBy(x=>x.SignalType).ThenBy(x=>x.DirectionRelationship).ToArray();
        var seed=JsonSerializer.Serialize(new{Version,importId,Alignments=alignments.Select(x=>x.ContentHash)});
        var contentHash=AgentTrainingDatasetBuilder.Hash(seed);var report=new TradeJournalAlignmentReport(
            $"TJAR-{contentHash[..32]}",Version,importId,DateTime.UtcNow,alignments.Count,
            alignments.Count(x=>x.CanonicalBarAvailable),alignments.Count(x=>x.PatternMatches.Count>0),
            alignments.Count(x=>x.StructuralEventMatches.Count>0),
            alignments.Count(x=>x.PatternMatches.Count==0&&x.StructuralEventMatches.Count==0),metrics,structuralMetrics,
            directionalSegments,
            ["Associations describe patterns known before the trader's entry; they do not prove the pattern caused the result.",
             "An episode may match multiple recent patterns, so metric rows overlap.",
             "Missing canonical or pattern coverage remains unmatched and is never treated as neutral evidence."],contentHash);
        await Persist(connection,report,alignments,token);return report;
    }

    public async Task<IReadOnlyList<TradeJournalAlignmentReport>> GetReportsAsync(CancellationToken token=default)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT ReportJson FROM TradeJournalAlignmentReports ORDER BY CreatedAtUtc DESC";
        var values=new List<TradeJournalAlignmentReport>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<TradeJournalAlignmentReport>(reader.GetString(0))!);return values;}

    public async Task<IReadOnlyList<TradeJournalEpisodeAlignment>> GetAlignmentsAsync(string reportId,CancellationToken token=default)
    {await using var connection=database.CreateConnection();await connection.OpenAsync(token);await using var command=connection.CreateCommand();
        command.CommandText="SELECT AlignmentJson FROM TradeJournalEpisodeAlignments WHERE ReportId=$id ORDER BY EntryTimeUtc,EpisodeId";
        command.Parameters.AddWithValue("$id",reportId);var values=new List<TradeJournalEpisodeAlignment>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<TradeJournalEpisodeAlignment>(reader.GetString(0))!);return values;}

    private static TradeJournalPatternBehaviorMetric Metric(string module,string pattern,
        IReadOnlyList<BehaviorRow> rows)
    {
        var profits=rows.Select(x=>x.Alignment.NetProfit).ToArray();var wins=profits.Count(x=>x>0);var losses=profits.Count(x=>x<0);
        var gain=profits.Where(x=>x>0).Sum();var loss=Math.Abs(profits.Where(x=>x<0).Sum());
        return new(module,pattern,rows.Count,wins,losses,profits.Sum(),wins/(decimal)rows.Count,
            loss==0?0:gain/loss,rows.Count(x=>x.Match.DirectionAgrees)/(decimal)rows.Count);
    }

    private static TradeJournalStructuralBehaviorMetric StructuralMetric(string eventType,IReadOnlyList<StructuralRow> rows)
    {var profits=rows.Select(x=>x.Alignment.NetProfit).ToArray();var wins=profits.Count(x=>x>0);var losses=profits.Count(x=>x<0);
        var gain=profits.Where(x=>x>0).Sum();var loss=Math.Abs(profits.Where(x=>x<0).Sum());
        return new(eventType,rows.Count,wins,losses,profits.Sum(),wins/(decimal)rows.Count,loss==0?0:gain/loss,
            rows.Count(x=>x.Match.DirectionAgrees)/(decimal)rows.Count);}

    private static TradeJournalDirectionalBehaviorMetric DirectionalMetric(string source,string signal,bool agrees,
        IReadOnlyList<DirectionalRow> rows)
    {var profits=rows.Select(x=>x.NetProfit).ToArray();var wins=profits.Count(x=>x>0);var losses=profits.Count(x=>x<0);
        var gain=profits.Where(x=>x>0).Sum();var loss=Math.Abs(profits.Where(x=>x<0).Sum());
        return new(source,signal,agrees?"Aligned":"Opposed",rows.Count,wins,losses,profits.Sum(),
            wins/(decimal)rows.Count,loss==0?0:gain/loss);}

    private async Task<TradeJournalStructuralEventMatch[]> StructuralMatches(TradeJournalEpisode episode,
        Dictionary<DateOnly,DailyMarketDiscoveryStudy> studies,CancellationToken token)
    {
        var date=DateOnly.FromDateTime(episode.OpenedAtUtc);if(!studies.TryGetValue(date,out var study))
            studies[date]=study=await dailyDiscovery.StudyAsync(date,token);
        return study.DiscoveredEvents.Where(x=>x.Symbol.Equals(episode.ContractId,StringComparison.OrdinalIgnoreCase)&&
                x.KnownAtUtc<=episode.OpenedAtUtc&&x.KnownAtUtc>=episode.OpenedAtUtc.AddMinutes(-30))
            .OrderByDescending(x=>x.KnownAtUtc).Take(50).Select(x=>new TradeJournalStructuralEventMatch(
                x.EventId,x.Type,x.Direction,x.KnownAtUtc,(decimal)(episode.OpenedAtUtc-x.KnownAtUtc).TotalMinutes,
                x.Strength,episode.Direction==TradeJournalDirection.Long?x.Direction.Equals("Bullish",StringComparison.OrdinalIgnoreCase):
                    x.Direction.Equals("Bearish",StringComparison.OrdinalIgnoreCase),x.Evidence)).ToArray();
    }

    private static async Task<Bar?> LatestBar(SqliteConnection connection,TradeJournalEpisode episode,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText="""
        SELECT CanonicalBarId,CloseTimeUtc FROM CanonicalResolvedResearchBars
        WHERE InstrumentId=$instrument AND Timeframe='1m' AND CloseTimeUtc<=$entry
          AND julianday(CloseTimeUtc)>=julianday($entry)-(2.0/1440.0)
        ORDER BY CloseTimeUtc DESC LIMIT 1;
        """;command.Parameters.AddWithValue("$instrument",episode.InstrumentId);command.Parameters.AddWithValue("$entry",episode.OpenedAtUtc.ToString("O"));
        await using var reader=await command.ExecuteReaderAsync(token);return await reader.ReadAsync(token)?
            new(reader.GetString(0),DateTime.Parse(reader.GetString(1),null,DateTimeStyles.RoundtripKind)):null;}

    private static async Task<TradeJournalPatternMatch[]> PatternMatches(SqliteConnection connection,
        TradeJournalEpisode episode,CancellationToken token)
    {await using var command=connection.CreateCommand();command.CommandText="""
        SELECT ObservationId,Revision,ModuleId,PatternType,Direction,KnownAtUtc,ContentHash FROM
        (SELECT o.*,ROW_NUMBER() OVER(PARTITION BY ObservationId ORDER BY Revision DESC) rn
         FROM UniversalMarketObservations o WHERE InstrumentId=$instrument AND KnownAtUtc<=$entry
           AND julianday(KnownAtUtc)>=julianday($entry)-(30.0/1440.0))
        WHERE rn=1 ORDER BY KnownAtUtc DESC,ObservationId LIMIT 50;
        """;command.Parameters.AddWithValue("$instrument",episode.InstrumentId);command.Parameters.AddWithValue("$entry",episode.OpenedAtUtc.ToString("O"));
        var values=new List<TradeJournalPatternMatch>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)){var known=DateTime.Parse(reader.GetString(5),null,DateTimeStyles.RoundtripKind).ToUniversalTime();
            var direction=reader.GetString(4);var agrees=episode.Direction==TradeJournalDirection.Long?
                direction.Equals("Bullish",StringComparison.OrdinalIgnoreCase):direction.Equals("Bearish",StringComparison.OrdinalIgnoreCase);
            values.Add(new(reader.GetString(0),reader.GetInt32(1),reader.GetString(2),reader.GetString(3),direction,known,
                (decimal)(episode.OpenedAtUtc-known).TotalMinutes,agrees,reader.GetString(6)));}
        return values.ToArray();}

    private static async Task Persist(SqliteConnection connection,TradeJournalAlignmentReport report,
        IReadOnlyList<TradeJournalEpisodeAlignment> alignments,CancellationToken token)
    {await using var transaction=(SqliteTransaction)await connection.BeginTransactionAsync(token);
        await using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO TradeJournalAlignmentReports
            (ReportId,AlignmentVersion,ImportId,EpisodeCount,PatternMatchedEpisodes,ContentHash,ReportJson,CreatedAtUtc,
             CanActivateStrategy,CanRouteToRealBroker)
            VALUES($id,$version,$import,$episodes,$matched,$hash,$json,$created,0,0);
            """;Add(command,"$id",report.ReportId);Add(command,"$version",report.AlignmentVersion);Add(command,"$import",report.ImportId);
            Add(command,"$episodes",report.Episodes);Add(command,"$matched",report.PatternMatchedEpisodes);Add(command,"$hash",report.ContentHash);
            Add(command,"$json",JsonSerializer.Serialize(report));Add(command,"$created",report.CreatedAtUtc.ToString("O"));await command.ExecuteNonQueryAsync(token);}
        foreach(var alignment in alignments){await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="""
            INSERT OR IGNORE INTO TradeJournalEpisodeAlignments
            (ReportId,EpisodeId,InstrumentId,EntryTimeUtc,NetProfit,PatternMatchCount,ContentHash,AlignmentJson)
            VALUES($report,$episode,$instrument,$entry,$profit,$matches,$hash,$json);
            """;Add(command,"$report",report.ReportId);Add(command,"$episode",alignment.EpisodeId);Add(command,"$instrument",alignment.InstrumentId);
            Add(command,"$entry",alignment.EntryTimeUtc.ToString("O"));Add(command,"$profit",alignment.NetProfit.ToString(CultureInfo.InvariantCulture));
            Add(command,"$matches",alignment.PatternMatches.Count);Add(command,"$hash",alignment.ContentHash);
            Add(command,"$json",JsonSerializer.Serialize(alignment));await command.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);}

    private static void Add(SqliteCommand command,string name,object value)=>command.Parameters.AddWithValue(name,value);
    private sealed record Bar(string Id,DateTime CloseTimeUtc);
    private sealed record BehaviorRow(TradeJournalEpisodeAlignment Alignment,TradeJournalPatternMatch Match);
    private sealed record StructuralRow(TradeJournalEpisodeAlignment Alignment,TradeJournalStructuralEventMatch Match);
    private sealed record DirectionalRow(string SourceKind,string SignalType,bool DirectionAgrees,decimal NetProfit,string EpisodeId);
}
