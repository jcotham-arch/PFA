using System.Security.Cryptography;
using System.Text;
using PFA_FVG_Scanner.Domain.Observations;

namespace PFA_FVG_Scanner.Domain.Sequences;

public enum MarketSequenceState { Partial, Successful, Failed, Terminated }

public sealed record SequenceStageDefinition(
    string Role,
    IReadOnlySet<string> AcceptedPatternTypes);

public sealed record MarketSequenceDefinition(
    string SequenceDefinitionId,
    string Version,
    string DisplayName,
    IReadOnlyList<SequenceStageDefinition> Stages,
    TimeSpan MaximumTransitionDuration,
    bool RequireSameDirection = false,
    IReadOnlySet<string>? TerminationPatternTypes = null);

public sealed record MarketSequenceMember(
    string ObservationId,
    int ObservationRevision,
    string Role,
    int Ordinal,
    DateTime JoinedAtUtc);

public sealed record MarketSequenceTransition(
    string FromRole,
    string ToRole,
    DateTime OccurredAtUtc,
    TimeSpan Duration,
    decimal PointInTimeConfidence);

public sealed record MarketSequenceInstance(
    string SequenceInstanceId,
    string SequenceDefinitionId,
    string SequenceDefinitionVersion,
    string InstrumentId,
    string? ContractId,
    string Timeframe,
    string TradingSessionId,
    DateOnly TradingDate,
    MarketSequenceState State,
    int CurrentStageIndex,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc,
    decimal PointInTimeConfidence,
    string? TerminationReason,
    IReadOnlyList<MarketSequenceMember> Members,
    IReadOnlyList<MarketSequenceTransition> Transitions);

public interface IMarketSequenceEngine
{
    IReadOnlyList<MarketSequenceInstance> Replay(MarketSequenceDefinition definition,
        IReadOnlyList<UniversalMarketObservation> observations, DateTime asOfUtc);
}

public static class MarketSequenceIdentity
{
    public static string Create(string definitionId, string version, string sessionId,
        string firstObservationId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('|', definitionId, version, sessionId, firstObservationId))));
}
