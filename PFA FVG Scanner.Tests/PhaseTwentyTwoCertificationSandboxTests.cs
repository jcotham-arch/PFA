using PFA_FVG_Scanner.Domain.Certification;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sandbox;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseTwentyTwoCertificationSandboxTests
{
    private static readonly DateTime Now=new(2026,8,27,14,0,0,DateTimeKind.Utc);private static readonly CertificationExecutionEngine Execution=new();private static readonly PropFirmCertificationEngine Rules=new();

    [Fact]
    public void ExecutionIsDeterministicSeededAndNeverRoutesLive()
    {var a=Execution.Execute(Order(),Events(),Profile(),Instrument(),Now.AddMinutes(2));var b=Execution.Execute(Order(),Events().Reverse().ToArray(),Profile(),Instrument(),Now.AddMinutes(2));Assert.Equal(a.State,b.State);Assert.Equal(a.Fills.ToArray(),b.Fills.ToArray());Assert.Equal(a.AuditReasons.ToArray(),b.AuditReasons.ToArray());Assert.False(a.CanRouteToRealBroker);Assert.All(a.Fills,x=>Assert.Equal(Profile().ContentHash(),x.ProfileHash));}

    [Fact]
    public void LatencyStalenessAndVenueOutageFailConservatively()
    {var late=Execution.Execute(Order(),[Event("EARLY",1,Now.AddMilliseconds(10),true)],Profile() with{BaseLatencyMilliseconds=1000,JitterMilliseconds=0},Instrument(),Now.AddSeconds(2));Assert.Empty(late.Fills);
     var stale=Execution.Execute(Order(),[Event("STALE",1,Now.AddSeconds(1),true) with{EventTimeUtc=Now.AddSeconds(-10)}],Profile(),Instrument(),Now.AddSeconds(2));Assert.Equal(CertificationRejectReason.StaleMarket,stale.RejectReason);
     var outage=Execution.Execute(Order(),[Event("DOWN",1,Now.AddSeconds(1),false)],Profile(),Instrument(),Now.AddSeconds(2));Assert.Equal(CertificationRejectReason.VenueUnavailable,outage.RejectReason);}

    [Fact]
    public void QueueParticipationCreatesPartialFillsAndAdverseCosts()
    {var result=Execution.Execute(Order(quantity:5),[Event("E1",1,Now.AddSeconds(1),true,lastSize:4) with{BidSize=0,AskSize=0},Event("E2",2,Now.AddSeconds(2),true,lastSize:4) with{BidSize=0,AskSize=0}],Profile() with{MaximumParticipationRate=.5m,QueueAheadContracts=0,BaseSlippageTicks=1,QuantityImpactTicks=.5m},Instrument(),Now.AddSeconds(3));Assert.Equal(CertificationOrderState.PartiallyFilled,result.State);Assert.Equal(4,result.FilledQuantity);Assert.Equal(1,result.RemainingQuantity);Assert.All(result.Fills,x=>{Assert.True(x.Price>5000m);Assert.True(x.Commission>0);Assert.True(x.SlippageTicks>=1);});}

    [Fact]
    public void LimitTouchCanMissAndStopBecomesAdverseMarketOrder()
    {var limit=Order(type:SandboxOrderType.Limit,limit:5000m);var missed=Execution.Execute(limit,[Event("TOUCH",1,Now.AddSeconds(1),true) with{Last=5000m,Bid=4999.75m,Ask=5000m}],Profile() with{TouchFillProbability=0},Instrument(),Now.AddSeconds(2));Assert.Empty(missed.Fills);Assert.Contains(missed.AuditReasons,x=>x.Contains("queue"));
     var stop=Order(type:SandboxOrderType.Stop,stop:5000.25m);var filled=Execution.Execute(stop,[Event("TRIGGER",1,Now.AddSeconds(1),true) with{Last=5000.5m,Ask=5000.75m}],Profile(),Instrument(),Now.AddSeconds(2));Assert.NotEmpty(filled.Fills);Assert.True(filled.Fills[0].Price>=5000.75m);}

    [Fact]
    public void ReconciliationHoldsOnUnknownOrdersMissingOrdersAndPositions()
    {var local=new InternalAccountSnapshot("I","A",Now,new Dictionary<string,int>{{"MESU6",1}},new HashSet<string>{"ORDER-A"},50000);var venue=new VenueAccountSnapshot("V","A",Now,new Dictionary<string,int>{{"MESU6",0}},new HashSet<string>{"ORDER-B"},49999);var report=new CertificationReconciliationEngine().Reconcile(local,venue,Now);Assert.True(report.TradingHeld);Assert.Equal(4,report.Breaks.Count);Assert.False(report.CanRouteToRealBroker);}

    [Fact]
    public void ConservativePropPackTracksIntradayTrailingDrawdownAndCosts()
    {var pack=PropFirmRulePackCatalog.PfaConservative50K(Now.AddDays(-1));var days=WinningDays(pack,5,600m);days[2]=days[2] with{IntradayHighEquity=days[2].StartBalance+1500,IntradayLowEquity=days[2].StartBalance-600};var result=Rules.Evaluate("A",pack,days,Now.AddDays(20));Assert.Equal(PropAccountCertificationStatus.Failed,result.Status);Assert.Contains(result.Violations,x=>x.Code==PropRuleViolationCode.TrailingDrawdown);Assert.False(result.CanRouteToRealBroker);}

    [Fact]
    public void ChallengeRequiresProfitDaysAndConsistencyBeforePassing()
    {var pack=PropFirmRulePackCatalog.PfaConservative50K(Now.AddDays(-1));var concentrated=WinningDays(pack,5,600m);concentrated[0]=concentrated[0] with{GrossProfitLoss=2200,EndBalance=concentrated[0].StartBalance+2200,EndEquity=concentrated[0].StartBalance+2200,IntradayHighEquity=concentrated[0].StartBalance+2200};Rechain(concentrated);var first=Rules.Evaluate("A",pack,concentrated,Now.AddDays(20));Assert.False(first.PassedProfitTarget);Assert.Contains(first.Violations,x=>x.Code==PropRuleViolationCode.Consistency);
     var balanced=WinningDays(pack,9,350m);var passed=Rules.Evaluate("A",pack,balanced,Now.AddDays(20));Assert.True(passed.PassedProfitTarget);Assert.Equal(PropAccountCertificationStatus.PassedChallenge,passed.Status);Assert.False(passed.PayoutRulesSatisfied);}

    [Fact]
    public void PayoutNeedsBufferAndTimeWhileRuleAndAutomationBreachesFail()
    {var pack=PropFirmRulePackCatalog.PfaConservative50K(Now.AddDays(-1));var days=WinningDays(pack,10,350m);var payout=Rules.Evaluate("A",pack,days,Now.AddDays(20));Assert.True(payout.PayoutRulesSatisfied);Assert.Equal(PropAccountCertificationStatus.PayoutEligible,payout.Status);
     days[3]=days[3] with{ExecutionMode=PropAutomationMode.Unsupported};var failed=Rules.Evaluate("A",pack,days,Now.AddDays(20));Assert.Equal(PropAccountCertificationStatus.Failed,failed.Status);Assert.Contains(failed.Violations,x=>x.Code==PropRuleViolationCode.AutomationProhibited);}

    [Fact]
    public void IncompleteOrFutureEvidenceCannotProduceFantasyPass()
    {var pack=PropFirmRulePackCatalog.PfaConservative50K(Now.AddDays(-1));var days=WinningDays(pack,10,350m);days[0]=days[0] with{OperationalDataComplete=false};Assert.Equal(PropAccountCertificationStatus.OperationallyInvalid,Rules.Evaluate("A",pack,days,Now.AddDays(20)).Status);days=WinningDays(pack,10,350m);days[0]=days[0] with{KnownAtUtc=Now.AddDays(30)};Assert.Throws<InvalidOperationException>(()=>Rules.Evaluate("A",pack,days,Now.AddDays(20)));}

    private static ExecutionRealismProfile Profile()=>new("PFA-CERT","1.0.0",42,100,0,2000,1,1,.25m,.25m,.5m,0,.5m,1.25m,false,true,false);
    private static CertificationOrderRequest Order(int quantity=1,SandboxOrderType type=SandboxOrderType.Market,decimal? limit=null,decimal? stop=null)=>new("ORDER","A","S","1","MES","MESU6",SandboxOrderSide.Buy,type,quantity,limit,stop,Now,"SIGNAL","GOVERNANCE");
    private static CertificationMarketEvent Event(string id,long sequence,DateTime known,bool available,int lastSize=10)=>new(id,"MES","MESU6",sequence,known.AddMilliseconds(-10),known,4999.75m,5000.00m,10,10,5000m,lastSize,1,available,"REV-1");
    private static CertificationMarketEvent[] Events()=>[Event("E1",1,Now.AddSeconds(1),true),Event("E2",2,Now.AddSeconds(2),true)];
    private static InstrumentDefinition Instrument()=>new InstrumentDefinitionRegistry().GetAll().Single(x=>x.InstrumentId=="MES");
    private static List<PropTradingDayResult> WinningDays(PropFirmRulePack pack,int count,decimal net){var list=new List<PropTradingDayResult>();var balance=pack.StartingBalance;for(var i=0;i<count;i++){var start=balance;balance+=net;list.Add(new(DateOnly.FromDateTime(Now.AddDays(i)),start,balance,balance,start+net,start-100,net+10,10,1,2,false,false,PropAutomationMode.AutomatedExecutionPermitted,true,$"DAY-{i}",Now.AddDays(i+1)));}return list;}
    private static void Rechain(List<PropTradingDayResult> days){for(var i=1;i<days.Count;i++){var start=days[i-1].EndBalance;var net=days[i].GrossProfitLoss-days[i].Commissions;days[i]=days[i] with{StartBalance=start,EndBalance=start+net,EndEquity=start+net,IntradayHighEquity=start+Math.Max(0,net),IntradayLowEquity=start-100};}}
}
