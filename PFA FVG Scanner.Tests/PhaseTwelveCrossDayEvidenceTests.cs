using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Evidence;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseTwelveCrossDayEvidenceTests
{
    [Fact]
    public void LegacyAdapterPreservesSignatureClassificationGatesAndMetrics()
    {
        var legacy=Legacy(FvgCrossDayEvidenceStatus.PersistentCandidate);
        var report=LegacyFvgCrossDayEvidenceAdapter.Map(legacy,Dates(),"legacy-utc-1.0.0",TestData.BaseTime);
        var evidence=Assert.Single(report.Signatures);
        Assert.Equal("RULE-SIGNATURE",evidence.Signature);
        Assert.Equal(CrossDayEvidenceClassification.PersistentCandidate,evidence.Classification);
        Assert.True(evidence.Gates["persistence"]);
        Assert.Equal(0.25m,evidence.AggregateMetrics["expectancy-r"]);
        Assert.True(evidence.CanAdvanceToFrozenValidation);
        Assert.False(evidence.CanActivateStrategy);
    }

    [Fact]
    public void MissingTradingDatesAreExplicitAndCalendarIsNeverInvented()
    {
        var report=LegacyFvgCrossDayEvidenceAdapter.Map(Legacy(FvgCrossDayEvidenceStatus.Watchlist),
            Dates(),"legacy-utc-1.0.0",TestData.BaseTime);
        Assert.Equal([new DateOnly(2025,1,7),new DateOnly(2025,1,8)],
            Assert.Single(report.Signatures).MissingTradingDates);
        Assert.DoesNotContain(new DateOnly(2025,1,11),report.ExpectedTradingDates);
    }

    [Fact]
    public async Task PersistentNegativeIsStoredAndReadWithoutPromotion()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();
        var report=LegacyFvgCrossDayEvidenceAdapter.Map(Legacy(FvgCrossDayEvidenceStatus.PersistentNegative),
            Dates(),"legacy-utc-1.0.0",TestData.BaseTime);
        ICrossDayEvidenceRepository repository=new CrossDayEvidenceRepository(factory.Database);
        await repository.SaveAsync(report,TestContext.Current.CancellationToken);
        await repository.SaveAsync(report,TestContext.Current.CancellationToken);
        var stored=await repository.FindAsync(report.ReportId,TestContext.Current.CancellationToken);
        Assert.Equal(CrossDayEvidenceClassification.PersistentNegative,Assert.Single(stored!.Signatures).Classification);
        Assert.False(stored.CanActivateAnyStrategy);
        Assert.Equal(1,await Count(factory.Database,"GeneralCrossDayEvidenceReports"));
    }

    [Fact]
    public async Task ReportsAreImmutableAndActivationIsRejected()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();
        var repository=new CrossDayEvidenceRepository(factory.Database);
        var report=LegacyFvgCrossDayEvidenceAdapter.Map(Legacy(FvgCrossDayEvidenceStatus.Watchlist),
            Dates(),"legacy-utc-1.0.0",TestData.BaseTime);
        await repository.SaveAsync(report,TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>repository.SaveAsync(
            report with{SourceReference="changed"},TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repository.SaveAsync(
            report with{ReportId="ACTIVE",CanActivateAnyStrategy=true},TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NonFvgFamilyUsesSamePersistenceContract()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();
        var generic=new CrossDaySignatureEvidence("SWEEP-SIG","liquidity-sweep-study","1.0.0","{}",
            CrossDayEvidenceClassification.InsufficientEvidence,2,1,[new DateOnly(2025,1,7)],0,1,0,4,3,
            new Dictionary<string,decimal>{{"expectancy-r",-0.1m}},new HashSet<string>{"trend"},
            new Dictionary<string,bool>{{"sample",false}},false,
            [new(new DateOnly(2025,1,6),4,3,new Dictionary<string,decimal>{{"net-r",-0.4m}},"Negative",new HashSet<string>{"trend"})]);
        var report=new GeneralCrossDayEvidenceReport("GENERIC","MES","1","legacy-utc-1.0.0",
            new DateOnly(2025,1,6),new DateOnly(2025,1,7),Dates().Take(2).ToArray(),[generic],"generic",TestData.BaseTime);
        var repository=new CrossDayEvidenceRepository(factory.Database);await repository.SaveAsync(report,TestContext.Current.CancellationToken);
        Assert.Equal("liquidity-sweep-study",Assert.Single((await repository.FindAsync("GENERIC",TestContext.Current.CancellationToken))!.Signatures).FamilyId);
    }

    private static FvgCrossDayEvidenceReport Legacy(FvgCrossDayEvidenceStatus status)
    {
        var day=new FvgCrossDayRuleDayResult{TradingDateUtc=new DateTime(2025,1,6,0,0,0,DateTimeKind.Utc),Trades=5,
            DistinctFvgs=4,NetR=status==FvgCrossDayEvidenceStatus.PersistentNegative?-1:1,ExpectancyR=.25m,
            ProfitFactorR=1.4m,MaximumDrawdownR=1,OriginalDailyStatus=CandidateRuleStatus.ResearchCandidate,WasPositive=true};
        var rule=new FvgCrossDayRuleEvidence{RuleSignature="RULE-SIGNATURE",RuleName="Rule",EntryModel=MesEntryModel.BoundaryTouch,
            TargetR=2,TotalDaysInDataset=3,DaysObserved=1,PositiveDays=1,TotalTrades=5,TotalDistinctFvgs=4,
            NetR=day.NetR,ExpectancyR=.25m,AverageDailyExpectancyR=.25m,ProfitFactorR=1.4m,
            CrossDayMaximumDrawdownR=1,ExpectancyStandardDeviation=0,PersistenceScore=70,Status=status,
            PassedDayCoverageGate=true,PassedSampleGate=true,PassedPositiveDaysGate=true,PassedExpectancyGate=true,
            PassedProfitFactorGate=true,PassedPersistenceGates=true,CanAdvanceToFrozenValidation=status==FvgCrossDayEvidenceStatus.PersistentCandidate,
            CanActivateStrategy=false,DailyResults=[day]};
        return new FvgCrossDayEvidenceReport{Symbol="MES",StartDateUtc=new DateTime(2025,1,6),EndDateUtc=new DateTime(2025,1,8),
            TradingDaysEvaluated=3,UniqueRulesObserved=1,AllRules=[rule],EngineVersion="1.0.0",CanActivateAnyStrategy=false};
    }
    private static DateOnly[] Dates()=>[new(2025,1,6),new(2025,1,7),new(2025,1,8)];
    private static async Task<int> Count(PfaDatabase db,string table){await using SqliteConnection c=db.CreateConnection();await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt32(await q.ExecuteScalarAsync());}
}
