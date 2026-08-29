namespace PFA_FVG_Scanner.Domain.Modules;

public sealed record AdvancedStrategiesManifest(string ModuleId,string DisplayName,string ModuleVersion,
    string ContractVersion,string Integration,IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> SupportedInstruments,IReadOnlyList<string> SupportedTimeframes,
    IReadOnlyList<string> RequiredDataScopes,bool CanActivateStrategy,bool CanRouteToRealBroker,string ContentHash);

public sealed record AdvancedStrategiesAnalysisRequest(string RequestId,string IdempotencyKey,
    string ContractVersion,string InstrumentId,string ContractId,string Timeframe,DateTime AsOfUtc,
    string CanonicalDataRevision,IReadOnlyList<string> CanonicalBarIds,
    IReadOnlyList<string> ObservationReferences,IReadOnlyList<string> SequenceReferences,
    IReadOnlyDictionary<string,decimal> PointInTimeFeatures,string EntitlementAssertionReference,
    string TraceId,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record AdvancedStrategiesCandidate(string CandidateId,string PatternFamily,string Direction,
    decimal? EntryPrice,decimal? StopPrice,decimal? TargetPrice,int? MaximumHoldingMinutes,
    string Explanation,IReadOnlyList<string> EvidenceReferences,string ResearchState="Proposed",
    bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record AdvancedStrategiesAnalysisResponse(string AnalysisId,string RequestId,string ModuleVersion,
    string ContractVersion,string InstrumentId,string ContractId,string Timeframe,DateTime AsOfUtc,
    string CanonicalDataRevision,string Decision,IReadOnlyList<AdvancedStrategiesCandidate> Candidates,
    IReadOnlyList<string> Assumptions,IReadOnlyList<string> Exclusions,IReadOnlyList<string> RejectionReasons,
    string ContentHash,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record PartnerCompatibilityIssue(string Code,string Message);
public sealed record AdvancedStrategiesCompatibilityResult(bool Compatible,string ExpectedContractVersion,
    IReadOnlyList<PartnerCompatibilityIssue> Issues,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record AdvancedStrategiesIntegrationPacket(string ModuleId,string DisplayName,string ContractVersion,
    string IntegrationState,string CurrentBoundary,IReadOnlyList<string> AcceptedInstruments,
    IReadOnlyList<string> AcceptedTimeframes,IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> AllowedDataScopes,IReadOnlyList<string> RequiredDeliverables,
    string HandoffDocument,string CompatibilityEndpoint,bool CanActivateStrategy=false,
    bool CanRouteToRealBroker=false);
