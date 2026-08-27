using PFA_FVG_Scanner.Domain.Observations;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Sessions;

namespace PFA_FVG_Scanner.Domain.Sequences;

public sealed class MarketSequenceEngine : IMarketSequenceEngine
{
    private readonly ITradingSessionService _sessions;
    public MarketSequenceEngine(ITradingSessionService sessions) => _sessions = sessions;

    public IReadOnlyList<MarketSequenceInstance> Replay(MarketSequenceDefinition definition,
        IReadOnlyList<UniversalMarketObservation> observations, DateTime asOfUtc)
    {
        Validate(definition);
        asOfUtc = Utc(asOfUtc);
        var eligible = observations.Where(x => Utc(x.KnownAtUtc) <= asOfUtc)
            .OrderBy(x => x.KnownAtUtc).ThenBy(x => x.ObservationId, StringComparer.Ordinal).ToArray();
        var results = new List<MarketSequenceInstance>();
        var active = new List<MutableSequence>();

        foreach (var observation in eligible)
        {
            var assignment = _sessions.Assign(observation.InstrumentId, observation.KnownAtUtc);
            Expire(active, results, definition, observation.KnownAtUtc,
                observation.InstrumentId, assignment.Session.TradingSessionId);

            foreach (var sequence in active.ToArray())
            {
                if (!SameStream(sequence, observation, assignment.Session.TradingSessionId)) continue;
                if (definition.TerminationPatternTypes?.Contains(observation.PatternType) == true)
                {
                    sequence.Terminate(observation.KnownAtUtc, "termination-observation");
                    active.Remove(sequence); results.Add(sequence.Freeze()); continue;
                }
                if (definition.RequireSameDirection && sequence.Direction != observation.Direction) continue;
                var nextIndex = sequence.StageIndex + 1;
                if (nextIndex >= definition.Stages.Count ||
                    !Matches(definition.Stages[nextIndex], observation)) continue;
                sequence.Advance(definition.Stages[nextIndex].Role, observation, definition.Stages.Count);
                if (sequence.State == MarketSequenceState.Successful)
                {
                    active.Remove(sequence); results.Add(sequence.Freeze());
                }
            }

            if (Matches(definition.Stages[0], observation))
                active.Add(MutableSequence.Start(definition, observation, assignment.Session));
        }

        Expire(active, results, definition, asOfUtc, null, null);
        results.AddRange(active.Select(x => x.Freeze()));
        return results.OrderBy(x => x.StartedAtUtc).ThenBy(x => x.SequenceInstanceId,
            StringComparer.Ordinal).ToArray();
    }

    private static void Expire(List<MutableSequence> active, List<MarketSequenceInstance> results,
        MarketSequenceDefinition definition, DateTime clockUtc, string? currentInstrumentId,
        string? currentSessionId)
    {
        foreach (var sequence in active.ToArray())
        {
            string? reason = null;
            MarketSequenceState state = MarketSequenceState.Partial;
            if (clockUtc >= sequence.Session.SessionCloseUtc &&
                (currentSessionId is null ||
                 (sequence.InstrumentId.Equals(currentInstrumentId, StringComparison.OrdinalIgnoreCase) &&
                  sequence.Session.TradingSessionId != currentSessionId)))
            { reason = "session-ended"; state = MarketSequenceState.Terminated; }
            else if (clockUtc - sequence.UpdatedAtUtc > definition.MaximumTransitionDuration)
            { reason = "transition-timeout"; state = MarketSequenceState.Failed; }
            if (reason is null) continue;
            sequence.End(state, clockUtc, reason); active.Remove(sequence); results.Add(sequence.Freeze());
        }
    }

    private static bool SameStream(MutableSequence sequence, UniversalMarketObservation observation,
        string sessionId) => sequence.InstrumentId.Equals(observation.InstrumentId, StringComparison.OrdinalIgnoreCase)
        && sequence.Timeframe.Equals(observation.Timeframe, StringComparison.OrdinalIgnoreCase)
        && sequence.Session.TradingSessionId == sessionId;
    private static bool Matches(SequenceStageDefinition stage, UniversalMarketObservation observation) =>
        stage.AcceptedPatternTypes.Contains("*") || stage.AcceptedPatternTypes.Contains(observation.PatternType);
    private static void Validate(MarketSequenceDefinition definition)
    {
        if (definition.Stages.Count < 2) throw new ArgumentException("A sequence requires at least two stages.");
        if (definition.MaximumTransitionDuration <= TimeSpan.Zero)
            throw new ArgumentException("Maximum transition duration must be positive.");
        if (definition.Stages.Any(x => string.IsNullOrWhiteSpace(x.Role) || x.AcceptedPatternTypes.Count == 0))
            throw new ArgumentException("Every sequence stage requires a role and accepted pattern types.");
    }
    private static DateTime Utc(DateTime value) => value.Kind switch
    { DateTimeKind.Utc => value, DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc), _ => value.ToUniversalTime() };

    private sealed class MutableSequence
    {
        private readonly MarketSequenceDefinition _definition;
        private readonly List<MarketSequenceMember> _members = [];
        private readonly List<MarketSequenceTransition> _transitions = [];
        public string Id { get; private init; } = string.Empty;
        public string InstrumentId { get; private init; } = string.Empty;
        public string? ContractId { get; private init; }
        public string Timeframe { get; private init; } = string.Empty;
        public PatternDirection Direction { get; private init; }
        public TradingSession Session { get; private init; } = null!;
        public int StageIndex { get; private set; }
        public DateTime StartedAtUtc { get; private init; }
        public DateTime UpdatedAtUtc { get; private set; }
        public MarketSequenceState State { get; private set; } = MarketSequenceState.Partial;
        public string? Reason { get; private set; }
        private MutableSequence(MarketSequenceDefinition definition) => _definition = definition;
        public static MutableSequence Start(MarketSequenceDefinition definition,
            UniversalMarketObservation observation, TradingSession session)
        {
            var value = new MutableSequence(definition)
            {
                Id = MarketSequenceIdentity.Create(definition.SequenceDefinitionId, definition.Version,
                    session.TradingSessionId, observation.ObservationId),
                InstrumentId = observation.InstrumentId, ContractId = observation.ContractId,
                Timeframe = observation.Timeframe, Direction = observation.Direction, Session = session,
                StartedAtUtc = observation.KnownAtUtc, UpdatedAtUtc = observation.KnownAtUtc
            };
            value._members.Add(new(observation.ObservationId, observation.Revision,
                definition.Stages[0].Role, 1, observation.KnownAtUtc));
            return value;
        }
        public void Advance(string role, UniversalMarketObservation observation, int stageCount)
        {
            var previousRole = _definition.Stages[StageIndex].Role;
            var duration = observation.KnownAtUtc - UpdatedAtUtc;
            StageIndex++;
            UpdatedAtUtc = observation.KnownAtUtc;
            _members.Add(new(observation.ObservationId, observation.Revision, role, StageIndex + 1, UpdatedAtUtc));
            var confidence = decimal.Divide(StageIndex + 1, stageCount);
            _transitions.Add(new(previousRole, role, UpdatedAtUtc, duration, confidence));
            if (StageIndex == stageCount - 1) State = MarketSequenceState.Successful;
        }
        public void Terminate(DateTime atUtc, string reason) => End(MarketSequenceState.Terminated, atUtc, reason);
        public void End(MarketSequenceState state, DateTime atUtc, string reason)
        { State = state; UpdatedAtUtc = atUtc; Reason = reason; }
        public MarketSequenceInstance Freeze() => new(Id, _definition.SequenceDefinitionId, _definition.Version,
            InstrumentId, ContractId, Timeframe, Session.TradingSessionId, Session.TradingDate, State, StageIndex,
            StartedAtUtc, UpdatedAtUtc, decimal.Divide(StageIndex + 1, _definition.Stages.Count), Reason,
            _members.ToArray(), _transitions.ToArray());
    }
}
