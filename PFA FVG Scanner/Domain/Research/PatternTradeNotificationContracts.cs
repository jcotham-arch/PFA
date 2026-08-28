namespace PFA_FVG_Scanner.Domain.Research;

public enum PatternTradeNotificationState
{Detected,ResearchEntryEligible,TargetReached,StopReached,BreakEvenExit,TimeExpired,Ambiguous,Unavailable}

public sealed record PatternTradeNotification(string NotificationId,string SourceSampleId,string HypothesisId,
    string ObservationId,string InstrumentId,string ModuleId,PatternTradeNotificationState State,
    DateTime KnownAtUtc,decimal? EntryPrice,decimal? StopPrice,decimal? TargetPrice,string Headline,string Message,
    bool IsActionable=false,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public static class PatternTradeNotificationInterpreter
{
    public const string Version="pattern-trade-notification-semantics-1.0.0";

    public static PatternTradeNotification Interpret(PatternTradeHypothesisSample sample,DateTime asOfUtc,
        DateTime evaluationKnownAtUtc)
    {
        ArgumentNullException.ThrowIfNull(sample);var asOf=Utc(asOfUtc);var evaluation=Utc(evaluationKnownAtUtc);
        if(asOf<sample.DecisionTimeUtc)throw new ArgumentException("A notification cannot exist before the pattern decision clock.");
        PatternTradeNotificationState state;DateTime known;string headline;string message;
        if(!sample.EntryTimeUtc.HasValue||asOf<sample.EntryTimeUtc.Value)
        {
            if(sample.Outcome==HypothesisExitOutcome.NoEntry&&asOf>=evaluation)
            {state=PatternTradeNotificationState.Unavailable;known=evaluation;headline="Entry unavailable";message="No completed entry bar became available inside the evaluated data horizon.";}
            else
            {state=PatternTradeNotificationState.Detected;known=sample.DecisionTimeUtc;headline="Pattern detected";message="The pattern fact is known; the defined research entry clock has not arrived.";}
        }
        else if(sample.Outcome==HypothesisExitOutcome.InvalidRisk)
        {state=PatternTradeNotificationState.Unavailable;known=sample.EntryTimeUtc.Value;headline="Hypothesis unavailable";message="The structural stop did not create valid minimum risk.";}
        else if(!sample.ExitTimeUtc.HasValue||asOf<sample.ExitTimeUtc.Value)
        {state=PatternTradeNotificationState.ResearchEntryEligible;known=sample.EntryTimeUtc.Value;headline="Research entry eligible";message="The hypothetical entry, stop, and target are now known. No validated edge, recommendation, or execution authority is implied.";}
        else
        {
            known=sample.ExitTimeUtc.Value;(state,headline,message)=sample.Outcome switch
            {
                HypothesisExitOutcome.Target=>(PatternTradeNotificationState.TargetReached,"Target reached","The research target was reached in the historical replay."),
                HypothesisExitOutcome.Stop=>(PatternTradeNotificationState.StopReached,"Hypothesis invalidated","The structural stop was reached in the historical replay."),
                HypothesisExitOutcome.BreakEven=>(PatternTradeNotificationState.BreakEvenExit,"Break-even exit","The activated break-even stop was reached in the historical replay."),
                HypothesisExitOutcome.TimeExit=>(PatternTradeNotificationState.TimeExpired,"Holding window expired","The maximum holding window elapsed before stop or target."),
                HypothesisExitOutcome.Ambiguous=>(PatternTradeNotificationState.Ambiguous,"Intrabar outcome ambiguous","Stop and target ordering cannot be resolved from one-minute bars."),
                _=>(PatternTradeNotificationState.Unavailable,"Hypothesis unavailable",sample.Reason)
            };
        }
        return new($"PTN-{sample.ContentHash[..20]}-{state}",sample.SampleId,sample.HypothesisId,sample.ObservationId,
            sample.InstrumentId,sample.ModuleId,state,known,sample.EntryPrice,sample.StopPrice,sample.TargetPrice,
            headline,message);
    }

    private static DateTime Utc(DateTime value)=>value.Kind switch
    {DateTimeKind.Utc=>value,DateTimeKind.Unspecified=>DateTime.SpecifyKind(value,DateTimeKind.Utc),_=>value.ToUniversalTime()};
}
