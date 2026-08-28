using PFA_FVG_Scanner.Domain.LivePilot;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseTwentyTwoDesignGateTests
{
    private static readonly DateTime Now=new(2026,8,27,12,0,0,DateTimeKind.Utc);
    private static readonly LivePilotReadinessAuditor Auditor=new();

    [Fact]
    public void EmptyReviewFailsClosedAndNeverRoutesOrActivates()
    {
        var result=Auditor.Evaluate(new("LPR-1","1.0.0",[],null,Now));
        Assert.Equal(LivePilotReadinessStatus.DesignReviewRequired,result.Status);
        Assert.Equal(LivePilotReadinessAuditor.RequiredTopics,result.MissingOrRejectedTopics);
        Assert.False(result.CanBuildInfrastructure);Assert.False(result.CanRouteToRealBroker);Assert.False(result.CanActivateStrategy);
    }

    [Fact]
    public void ProposedRejectedUnsignedAndFutureDecisionsRemainUnapproved()
    {
        var decisions=Approved();decisions[0]=decisions[0] with{Status=LivePilotDecisionStatus.Proposed};decisions[1]=decisions[1] with{Status=LivePilotDecisionStatus.Rejected};decisions[2]=decisions[2] with{DecidedBy=""};decisions[3]=decisions[3] with{DecidedAtUtc=Now.AddMinutes(1)};
        var result=Auditor.Evaluate(new("LPR-2","1.0.0",decisions,Evidence(),Now));
        Assert.Equal(LivePilotReadinessStatus.DesignReviewRequired,result.Status);Assert.Equal(4,result.MissingOrRejectedTopics.Count);Assert.False(result.CanBuildInfrastructure);
    }

    [Fact]
    public void ApprovedDesignStillRequiresStableImmutableEvidence()
    {
        var noEvidence=Auditor.Evaluate(new("LPR-3","1.0.0",Approved(),null,Now));Assert.Equal(LivePilotReadinessStatus.EvidenceRequired,noEvidence.Status);
        var unstable=Auditor.Evaluate(new("LPR-4","1.0.0",Approved(),Evidence() with{ForwardStable=false,ForwardTrades=0},Now));Assert.Equal(LivePilotReadinessStatus.EvidenceRequired,unstable.Status);Assert.NotEmpty(unstable.EvidenceFailures);
    }

    [Fact]
    public void CompleteReviewOnlyAuthorizesInfrastructureDesignNotTrading()
    {
        var result=Auditor.Evaluate(new("LPR-5","1.0.0",Approved(),Evidence(),Now));
        Assert.Equal(LivePilotReadinessStatus.ReadyForInfrastructureBuild,result.Status);Assert.True(result.CanBuildInfrastructure);
        Assert.False(result.CanRouteToRealBroker);Assert.False(result.CanActivateStrategy);
    }

    [Fact]
    public void ReviewIsOrderIndependentAndDuplicateTopicsAreRejected()
    {
        var decisions=Approved();var a=Auditor.Evaluate(new("LPR-6","1.0.0",decisions,Evidence(),Now));var b=Auditor.Evaluate(new("LPR-6","1.0.0",decisions.AsEnumerable().Reverse().ToArray(),Evidence(),Now));Assert.Equal(a.ReviewContentHash,b.ReviewContentHash);
        Assert.Throws<InvalidOperationException>(()=>Auditor.Evaluate(new("LPR-7","1.0.0",decisions.Append(decisions[0]).ToArray(),Evidence(),Now)));
    }

    [Fact]
    public async Task EmptyDatabaseProjectionReportsEveryDecisionAndEvidenceGate()
    {
        using var factory=await TestDatabaseFactory.CreateAsync();
        var projection=await new LivePilotReadinessProjectionService(factory.Database,Auditor)
            .GetAsync(TestContext.Current.CancellationToken);
        Assert.Equal(LivePilotReadinessStatus.DesignReviewRequired,projection.Gate.Status);
        Assert.Equal(11,projection.RequiredDecisionCount);Assert.Equal(0,projection.ApprovedDecisionCount);
        Assert.Null(projection.EvidenceCandidate);Assert.False(projection.Gate.CanRouteToRealBroker);
    }

    private static List<LivePilotDesignDecision> Approved()=>LivePilotReadinessAuditor.RequiredTopics.Select((topic,i)=>new LivePilotDesignDecision(topic,"1.0.0",LivePilotDecisionStatus.Approved,$"{{\"boundedDecision\":{i}}}","design-authority",Now.AddMinutes(-10),$"decision-record:{topic}")).ToList();
    private static LivePilotEvidenceSnapshot Evidence()=>new("STRATEGY","1.0.0","WFR-1","WALK-HASH",true,"FW-1","FWC-1","FORWARD-HASH",true,100,Now.AddMinutes(-1));
}
