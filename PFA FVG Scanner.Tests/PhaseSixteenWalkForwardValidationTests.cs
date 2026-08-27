using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Validation;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseSixteenWalkForwardValidationTests
{
    [Fact]
    public void PlannerBuildsRollingEmbargoedNonOverlappingValidationFolds()
    {
        var plan=Plan();Assert.True(plan.Folds.Count>=3);Assert.Equal(Enumerable.Range(1,plan.Folds.Count),plan.Folds.Select(x=>x.Ordinal));
        Assert.All(plan.Folds,x=>Assert.True(x.TrainingEndUtc<x.ValidationStartUtc));
        for(var i=1;i<plan.Folds.Count;i++)Assert.True(plan.Folds[i-1].ValidationEndUtc<=plan.Folds[i].ValidationStartUtc);
        Assert.Throws<ArgumentException>(()=>Planner().Create(Request() with{StepDays=4},Now));
    }

    [Fact]
    public void PlanIdentityIncludesDatasetCorrectionRevision()
    {var a=Plan("REV-1");var b=Plan("REV-2");Assert.NotEqual(a.PlanId,b.PlanId);Assert.All(a.Folds,x=>Assert.Equal("REV-1",x.DataRevision));}

    [Fact]
    public void EvaluationAggregatesFoldsWithoutFutureLeakage()
    {
        var plan=Plan();var observations=Rows(plan,Enumerable.Repeat(.5m,plan.Folds.Count).ToArray());
        observations.Add(new("FUTURE","SIG","PARAM","DATASET","REV-1",plan.Folds[^1].ValidationEndUtc,100m));
        var report=new WalkForwardValidationEngine().Evaluate(plan,observations,Now);Assert.Equal(WalkForwardAggregateStatus.Stable,report.Status);Assert.Equal(plan.Folds.Count,report.PassedFolds);Assert.Equal(.5m,report.WeightedExpectancyR);Assert.False(report.CanActivateStrategy);Assert.All(report.Folds,x=>Assert.False(x.CanActivateStrategy));
    }

    [Fact]
    public void MixedCorrectionRevisionIsRejectedAndParameterDriftIsRetained()
    {
        var plan=Plan();var rows=Rows(plan,Enumerable.Repeat(.5m,plan.Folds.Count).ToArray());rows[0]=rows[0] with{DataRevision="REV-2"};Assert.Throws<InvalidOperationException>(()=>new WalkForwardValidationEngine().Evaluate(plan,rows,Now));
        rows=Rows(plan,Enumerable.Repeat(.5m,plan.Folds.Count).ToArray());rows[0]=rows[0] with{ParameterHash="CHANGED"};var report=new WalkForwardValidationEngine().Evaluate(plan,rows,Now);Assert.True(report.ParameterDriftDetected);Assert.Equal(WalkForwardAggregateStatus.Unstable,report.Status);
    }

    [Fact]
    public void PerformanceDegradationAcrossUnseenFoldsIsVisible()
    {
        var plan=Plan();var values=Enumerable.Range(0,plan.Folds.Count).Select(i=>i==0?1m:-.25m).ToArray();var report=new WalkForwardValidationEngine().Evaluate(plan,Rows(plan,values),Now);
        Assert.Equal(WalkForwardAggregateStatus.Degraded,report.Status);Assert.True(report.ExpectancyDegradationPercentage>100m);Assert.True(report.FailedFolds>report.PassedFolds);
    }

    [Fact]
    public async Task ReportsAreImmutableIdempotentAndDatabaseForbidsActivation()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var repository=new WalkForwardValidationRepository(factory.Database);var plan=Plan();var report=new WalkForwardValidationEngine().Evaluate(plan,Rows(plan,Enumerable.Repeat(.5m,plan.Folds.Count).ToArray()),Now);
        await repository.SaveAsync(plan,report,TestContext.Current.CancellationToken);await repository.SaveAsync(plan,report,TestContext.Current.CancellationToken);var stored=await repository.FindReportAsync(report.ReportId,TestContext.Current.CancellationToken);
        Assert.Equal(report.ContentHash,stored!.ContentHash);Assert.Equal(1,await Count(factory.Database,"WalkForwardReports"));Assert.Equal(plan.Folds.Count,await Count(factory.Database,"WalkForwardFoldResults"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>repository.SaveAsync(plan,report with{ReportId="UNSAFE",CanActivateStrategy=true},TestContext.Current.CancellationToken));
        await using var connection=factory.Database.CreateConnection();await connection.OpenAsync(TestContext.Current.CancellationToken);await using var command=connection.CreateCommand();command.CommandText="UPDATE WalkForwardReports SET Status='Changed',CanActivateStrategy=1";var error=await Assert.ThrowsAsync<SqliteException>(()=>command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));Assert.Contains("immutable",error.Message,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyValidatorMapsOnlyAsAConservativeSingleFoldReference()
    {
        var legacy=new FvgOutOfSampleValidationReport{ValidationStartUtc=Now.AddDays(10),ValidationEndUtc=Now.AddDays(15),MatchingTrades=25,DistinctFvgs=22,ExpectancyR=.2m,WinRate=55,ProfitFactorR=1.3m,MaximumDrawdownR=2,RequiredDistinctFvgs=20,Decision=ValidationDecision.PassedValidation,CanActivateStrategy=false};
        var mapped=LegacyFvgWalkForwardAdapter.MapSingleFold(legacy,"SIG","PARAM","DATASET","REV-1",Now,Now.AddDays(9),Now);
        Assert.Single(mapped.Plan.Folds);Assert.Equal(WalkForwardAggregateStatus.Stable,mapped.Report.Status);Assert.False(mapped.Report.CanActivateStrategy);
        legacy.CanActivateStrategy=true;Assert.Throws<UnauthorizedAccessException>(()=>LegacyFvgWalkForwardAdapter.MapSingleFold(legacy,"SIG","PARAM","DATASET","REV-1",Now,Now.AddDays(9),Now));
    }

    private static readonly DateTime Now=new(2026,1,1,0,0,0,DateTimeKind.Utc);
    private static WalkForwardPlanner Planner()=>new();
    private static WalkForwardPlanRequest Request(string revision="REV-1")=>new("Frozen general hypothesis","SIG","PARAM","DATASET",revision,Now,Now.AddDays(40),10,5,5,1,2);
    private static WalkForwardPlan Plan(string revision="REV-1")=>Planner().Create(Request(revision),Now);
    private static List<WalkForwardObservation> Rows(WalkForwardPlan plan,decimal[] expectancy)
    {var rows=new List<WalkForwardObservation>();foreach(var fold in plan.Folds){for(var i=0;i<2;i++)rows.Add(new($"{fold.FoldId}-{i}","SIG","PARAM","DATASET",plan.DataRevision,fold.ValidationStartUtc.AddHours(i+1),expectancy[fold.Ordinal-1]));}return rows;}
    private static async Task<int> Count(PfaDatabase db,string table){await using var c=db.CreateConnection();await c.OpenAsync();await using var q=c.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {table}";return Convert.ToInt32(await q.ExecuteScalarAsync());}
}
