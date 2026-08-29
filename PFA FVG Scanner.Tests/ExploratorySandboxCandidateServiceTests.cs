using System.Text.Json;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Research;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class ExploratorySandboxCandidateServiceTests
{
    private static readonly DateTime Now=new(2026,8,29,12,0,0,DateTimeKind.Utc);

    [Fact]
    public async Task MesLaneUsesDevelopmentOnlyAndNeverGrantsActivationAuthority()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();
        var summaries=new[]
        {
            Summary("H-GOOD","Train",40,.05m,1.10m),Summary("H-GOOD","Validation",15,.02m,1.05m),
            Summary("H-GOOD","Test",15,-.90m,0m),
            Summary("H-BAD","Train",40,-.01m,.95m),Summary("H-BAD","Validation",15,.10m,1.4m),
            Summary("H-BAD","Test",15,1m,999m)
        };
        var run=new PatternTradeResearchRun("PTR-MES",PatternTradeHypothesisEngine.Version,Now,["MES"],
            ["pullback-continuation"],70,2,210,summaries,"RUN-HASH",Now);
        await using(var connection=factory.Database.CreateConnection())
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);await using var command=connection.CreateCommand();
            command.CommandText="""
                INSERT INTO PatternTradeResearchRuns
                (RunId,EngineVersion,AsOfUtc,ObservationCount,HypothesisCount,SampleCount,ContentHash,RunJson,CreatedAtUtc,CanActivateStrategy,CanRouteToRealBroker)
                VALUES($id,$version,$asOf,70,2,210,$hash,$json,$created,0,0);
                """;
            command.Parameters.AddWithValue("$id",run.RunId);command.Parameters.AddWithValue("$version",run.EngineVersion);
            command.Parameters.AddWithValue("$asOf",run.AsOfUtc.ToString("O"));command.Parameters.AddWithValue("$hash",run.ContentHash);
            command.Parameters.AddWithValue("$json",JsonSerializer.Serialize(run));command.Parameters.AddWithValue("$created",run.CreatedAtUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var queue=await new ExploratorySandboxCandidateService(factory.Database).GetAsync("MES",TestContext.Current.CancellationToken);
        var candidate=Assert.Single(queue.Candidates);Assert.Equal("H-GOOD",candidate.HypothesisId);
        Assert.Equal("WithheldFromExploratorySelection",candidate.TestPartitionStatus);
        Assert.True(candidate.CanEnterExploratoryPaper);Assert.False(candidate.IsStatisticallyValidated);
        Assert.False(candidate.CanActivateStrategy);Assert.False(candidate.CanRouteToRealBroker);
        Assert.False(queue.Policy.TestPartitionMayInfluenceAdmission);Assert.Equal(1,queue.DevelopmentRejected);
    }

    [Fact]
    public async Task FirstExploratoryLaneRejectsOtherInstruments()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();var service=new ExploratorySandboxCandidateService(factory.Database);
        var error=await Assert.ThrowsAsync<ArgumentException>(()=>service.GetAsync("MYM",TestContext.Current.CancellationToken));
        Assert.Contains("MES-only",error.Message);
    }

    private static PatternTradeHypothesisSummary Summary(string id,string split,int samples,decimal mean,decimal profitFactor)=>
        new(id,"pullback-continuation","directional-confirmation-close","opposite-range-invalidation",
            HypothesisDirectionPolicy.PatternDirection,.5m,60,split,samples,5,2,samples-7,0,0,mean,.55m,profitFactor,2m,
            false,"fixed-target-or-time",0);
}
