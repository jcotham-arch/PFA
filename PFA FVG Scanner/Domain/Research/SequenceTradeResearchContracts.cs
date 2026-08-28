namespace PFA_FVG_Scanner.Domain.Research;

public sealed record SequenceTradeResearchRequest(DateTime AsOfUtc,string? SourcePatternTradeRunId=null);

public sealed record SequenceTradeContextSample(string ContextSampleId,string SourceSampleId,
    string SequenceInstanceId,string SequenceDefinitionId,string Role,string HypothesisId,string ObservationId,
    string Split,HypothesisExitOutcome Outcome,decimal? NetR,DateTime SequenceKnownAtUtc,DateTime DecisionTimeUtc,
    string ContentHash);

public sealed record SequenceTradeHypothesisSummary(string SequenceDefinitionId,string Role,string HypothesisId,
    string ModuleId,string EntryPolicy,string StopPolicy,string ExitPolicy,HypothesisDirectionPolicy DirectionPolicy,
    decimal TargetR,int MaximumHoldingMinutes,string Split,int Samples,int Targets,int Stops,int BreakEvenExits,
    int TimeExits,int Ambiguous,int NoEntryOrInvalid,decimal MeanNetR,decimal WinRate,decimal ProfitFactor,
    decimal MaximumDrawdownR,bool IsTradableEvidence=false);

public sealed record SequenceTradeResearchRun(string RunId,string EngineVersion,string SourcePatternTradeRunId,
    DateTime AsOfUtc,int SequenceCompletionCount,int ContextSampleCount,
    IReadOnlyList<SequenceTradeHypothesisSummary> Summaries,string ContentHash,DateTime CreatedAtUtc,
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);
