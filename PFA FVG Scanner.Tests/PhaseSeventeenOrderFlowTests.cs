using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.OrderFlow;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseSeventeenOrderFlowTests
{
    [Fact]
    public void CanonicalizationOrdersByKnowledgeDetectsSequenceIssuesAndDeduplicates()
    {
        var input=new[]{Trade("T2",2,101,1,receivedSecond:2),Trade("T1",1,100,1,receivedSecond:3),Trade("T2",2,101,1,receivedSecond:2)};
        var batch=new OrderFlowCanonicalizer().Canonicalize(input);Assert.Equal(2,batch.Accepted.Count);Assert.Equal(1,batch.EquivalentDuplicates);
        Assert.Equal("T2",batch.Accepted[0].ProviderEventId);Assert.True(batch.Accepted[1].QualityFlags.HasFlag(OrderFlowQualityFlags.OutOfSequence));
    }

    [Fact]
    public void AggressorClassificationUsesOnlyKnownNonFutureQuotesThenTickRule()
    {
        var source=new[]{Quote("Q1",1,99,101,10,5,1),Trade("T1",2,101,2,2),Trade("T2",3,100.5m,1,3),Trade("T3",4,100.25m,1,4),Quote("FUTURE-Q",5,100.25m,100.5m,1,1,8,eventSecond:2)};
        var events=new OrderFlowCanonicalizer().Canonicalize(source).Accepted;var trades=new TradeAggressorClassifier().Classify(events,Now.AddSeconds(5),"REV-1");
        Assert.Equal(TradeAggressorSide.Buy,trades[0].Side);Assert.Equal(AggressorClassificationMethod.AtAsk,trades[0].Method);Assert.Equal(TradeAggressorSide.Sell,trades[2].Side);Assert.Equal(AggressorClassificationMethod.TickDown,trades[2].Method);
    }

    [Fact]
    public void CorrectionsAndCancelsSupersedeOriginalsWithoutRewritingHistory()
    {
        var source=new[]{Trade("T1",1,101,2,1),Trade("T1-C",2,101,3,2,OrderFlowSourceOperation.Correction,"T1"),Trade("T1-X",3,101,0,3,OrderFlowSourceOperation.Cancel,"T1-C")};
        var batch=new OrderFlowCanonicalizer().Canonicalize(source);Assert.Equal(2,batch.Corrections);Assert.All(batch.Accepted.Skip(1),x=>Assert.NotNull(x.SupersedesCanonicalEventId));
        var classified=new TradeAggressorClassifier().Classify(batch.Accepted,Now.AddMinutes(1),"REV-1");Assert.Empty(classified);
    }

    [Fact]
    public void FeatureSnapshotsExcludeFutureKnowledgeAndRespectProfileBoundaries()
    {
        var events=new OrderFlowCanonicalizer().Canonicalize([Quote("Q",1,99,101,4,6,1),Trade("BUY",2,101,3,2),Trade("LATE",3,99,5,50,eventSecond:3)]).Accepted;
        var classifier=new TradeAggressorClassifier();var trades=classifier.Classify(events,Now.AddSeconds(10),"REV-1");var engine=new OrderFlowFeatureEngine(new LegacyUtcTradingSessionService());
        var snapshot=engine.Build("MES","MESU6",Now,Now.AddMinutes(1),.25m,Now.AddSeconds(10),"REV-1",events,trades);
        Assert.Equal(3,snapshot.TotalVolume);Assert.Equal(3,snapshot.Delta);Assert.Equal(101,snapshot.PointOfControlPrice);Assert.Equal(-.2m,snapshot.LastBidAskImbalance);Assert.Equal(OrderFlowFeatureEngine.Version,snapshot.FeatureSetVersion);
        Assert.Throws<ArgumentException>(()=>engine.Build("MES","MESU6",Now.Date.AddHours(23),Now.Date.AddDays(1).AddMinutes(1),.25m,Now.AddDays(1),"REV-1",events,trades));
    }

    [Fact]
    public async Task CrossBatchCorrectionResolvesPersistentLineageAndStorageIsIdempotent()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new OrderFlowRepository(factory.Database);var service=Service(repository);
        var original=await service.IngestAsync([Trade("T1",1,101,2,1)],TestContext.Current.CancellationToken);var correction=await service.IngestAsync([Trade("T1-C",2,101,3,2,OrderFlowSourceOperation.Correction,"T1")],TestContext.Current.CancellationToken);
        Assert.Equal(original.Accepted[0].CanonicalEventId,correction.Accepted[0].SupersedesCanonicalEventId);await service.IngestAsync([Trade("T1",1,101,2,1)],TestContext.Current.CancellationToken);Assert.Equal(2,await Count(factory.Database,"OrderFlowEvents"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.IngestAsync([Trade("T1",1,102,2,1)],TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SnapshotPersistenceIsVersionedImmutableAndRetentionCannotAutoDelete()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new OrderFlowRepository(factory.Database);var service=Service(repository);await service.IngestAsync([Quote("Q",1,99,101,10,10,1),Trade("T",2,101,2,2)],TestContext.Current.CancellationToken);
        var snapshot=await service.BuildSnapshotAsync("MES","MESU6",Now,Now.AddMinutes(1),.25m,Now.AddMinutes(1),"REV-1",TestContext.Current.CancellationToken);var stored=await repository.FindSnapshotAsync(snapshot.SnapshotId,TestContext.Current.CancellationToken);Assert.Equal(snapshot.ContentHash,stored!.ContentHash);
        Assert.Equal(1,await Count(factory.Database,"OrderFlowFeatureSnapshots"));Assert.Equal(1,await Count(factory.Database,"OrderFlowClassifiedTrades"));Assert.Equal(0,await Scalar(factory.Database,"SELECT AutomaticDeletionEnabled FROM OrderFlowRetentionPolicies LIMIT 1"));
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);await using var command=connection.CreateCommand();command.CommandText="DELETE FROM OrderFlowEvents";await Assert.ThrowsAsync<SqliteException>(()=>command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CoverageDistinguishesNoSourceDataFromResearchSnapshots()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new OrderFlowRepository(factory.Database);
        Assert.Equal("NoSourceData",(await repository.GetCoverageAsync(TestContext.Current.CancellationToken)).Status);
        var service=Service(repository);await service.IngestAsync([Quote("Q",1,99,101,10,10,1),Trade("T",2,101,2,2)],TestContext.Current.CancellationToken);
        Assert.Equal("EventsAwaitingSnapshots",(await repository.GetCoverageAsync(TestContext.Current.CancellationToken)).Status);
        await service.BuildSnapshotAsync("MES","MESU6",Now,Now.AddSeconds(5),.25m,Now.AddSeconds(10),"REV-1",TestContext.Current.CancellationToken);
        var coverage=await repository.GetCoverageAsync(TestContext.Current.CancellationToken);Assert.Equal("ResearchSnapshotsAvailable",coverage.Status);
        Assert.Equal(2,coverage.Events);Assert.Equal(1,coverage.FeatureSnapshots);Assert.Contains("TEST",coverage.Providers);
    }

    private static readonly DateTime Now=new(2026,8,27,14,0,0,DateTimeKind.Utc);
    private static ProviderOrderFlowEvent Trade(string id,long sequence,decimal price,decimal size,int receivedSecond,OrderFlowSourceOperation operation=OrderFlowSourceOperation.Original,string? corrects=null,int? eventSecond=null)=>new("TEST",id,"MES","MESU6","MESU6",OrderFlowEventKind.Trade,operation,sequence,Now.AddSeconds(eventSecond??receivedSecond),Now.AddSeconds(receivedSecond),price,size,CorrectsProviderEventId:corrects,SourceVersion:"test-1");
    private static ProviderOrderFlowEvent Quote(string id,long sequence,decimal bid,decimal ask,decimal bidSize,decimal askSize,int receivedSecond,int? eventSecond=null)=>new("TEST",id,"MES","MESU6","MESU6",OrderFlowEventKind.Quote,OrderFlowSourceOperation.Original,sequence,Now.AddSeconds(eventSecond??receivedSecond),Now.AddSeconds(receivedSecond),BidPrice:bid,AskPrice:ask,BidSize:bidSize,AskSize:askSize,SourceVersion:"test-1");
    private static OrderFlowService Service(OrderFlowRepository repository){var sessions=new LegacyUtcTradingSessionService();return new(new OrderFlowCanonicalizer(),new TradeAggressorClassifier(),new OrderFlowFeatureEngine(sessions),repository,sessions);}
    private static async Task<int> Count(PfaDatabase db,string table)=>await Scalar(db,$"SELECT COUNT(*) FROM {table}");private static async Task<int> Scalar(PfaDatabase db,string sql){await using var c=db.CreateConnection();await c.OpenAsync(TestContext.Current.CancellationToken);await using var q=c.CreateCommand();q.CommandText=sql;return Convert.ToInt32(await q.ExecuteScalarAsync(TestContext.Current.CancellationToken));}
}
