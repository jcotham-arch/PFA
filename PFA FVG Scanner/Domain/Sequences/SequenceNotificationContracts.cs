using System.Security.Cryptography;
using System.Text;

namespace PFA_FVG_Scanner.Domain.Sequences;

public enum SequenceNotificationState { Watching, ResearchEligible, Invalidated, Expired }

public sealed record SequenceNotification(
    string NotificationId,string SequenceInstanceId,string SequenceDefinitionId,string DisplayName,
    string InstrumentId,string Timeframe,SequenceNotificationState State,string CurrentRole,string? NextRole,
    DateTime KnownAtUtc,DateTime? ExpiresAtUtc,decimal PointInTimeConfidence,string Headline,string Message,
    bool IsActionable=false,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public static class SequenceNotificationInterpreter
{
    public const string Version="sequence-notification-semantics-1.0.0";

    public static SequenceNotification Interpret(MarketSequenceDefinition definition,
        MarketSequenceInstance instance,DateTime asOfUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);ArgumentNullException.ThrowIfNull(instance);
        var asOf=Utc(asOfUtc);
        if(asOf<instance.UpdatedAtUtc)throw new ArgumentException("A notification cannot be interpreted before the sequence state was known.");
        var currentIndex=Math.Clamp(instance.CurrentStageIndex,0,definition.Stages.Count-1);
        var current=definition.Stages[currentIndex].Role;
        var next=currentIndex+1<definition.Stages.Count?definition.Stages[currentIndex+1].Role:null;
        var state=instance.State switch
        {MarketSequenceState.Partial=>SequenceNotificationState.Watching,
         MarketSequenceState.Successful=>SequenceNotificationState.ResearchEligible,
         MarketSequenceState.Failed=>SequenceNotificationState.Expired,
         _=>SequenceNotificationState.Invalidated};
        var (headline,message)=state switch
        {
            SequenceNotificationState.Watching=>("Sequence forming",$"{current} is known. Watching for {next} before the transition window expires."),
            SequenceNotificationState.ResearchEligible=>("Sequence completed",$"{current} completed the defined sequence. This is research eligibility only; no validated trade edge or execution authority is implied."),
            SequenceNotificationState.Expired=>("Sequence expired",$"The transition window expired after {current}; no entry notification is active."),
            _=>("Sequence invalidated",$"The sequence ended after {current}: {instance.TerminationReason??"invalidation"}.")
        };
        DateTime? expires=state==SequenceNotificationState.Watching?instance.UpdatedAtUtc+definition.MaximumTransitionDuration:null;
        var identity=string.Join('|',Version,instance.SequenceInstanceId,state,instance.UpdatedAtUtc.ToUniversalTime().ToString("O"));
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new($"SQN-{hash[..32]}",instance.SequenceInstanceId,definition.SequenceDefinitionId,
            definition.DisplayName,instance.InstrumentId,instance.Timeframe,state,current,next,instance.UpdatedAtUtc,
            expires,instance.PointInTimeConfidence,headline,message);
    }

    private static DateTime Utc(DateTime value)=>value.Kind switch
    {DateTimeKind.Utc=>value,DateTimeKind.Unspecified=>DateTime.SpecifyKind(value,DateTimeKind.Utc),_=>value.ToUniversalTime()};
}
