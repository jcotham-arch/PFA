namespace PFA_FVG_Scanner.Domain.Intermarket;

public sealed record OptionsGammaObservation(
    string ObservationId,DateTime AsOfUtc,DateTime KnownAtUtc,string Ticker,string Provider,
    decimal? TotalNetGamma,decimal? GammaFlipLevel,decimal? CallWallStrike,decimal? PutWallStrike,
    decimal? ZeroGammaLevel,int? ProviderGammaRegime,string MethodologyVersion,string ContentHash);

public sealed record VolatilityObservation(
    string ObservationId,DateTime AsOfUtc,DateTime KnownAtUtc,string Provider,
    decimal? VixSpot,decimal? VvixSpot,decimal? Vix3Month,string SourceVersion,string ContentHash);

public sealed record IntermarketBreadthObservation(
    string ObservationId,DateTime AsOfUtc,DateTime KnownAtUtc,string Provider,
    decimal? NyseTick,decimal? EsPrice,decimal? NqPrice,decimal? SpxCash,
    decimal? EsNqRollingCorrelation,decimal? FairValueBasis,string CalculationVersion,string ContentHash);

public sealed record IntermarketContextSnapshot(
    DateTime AsOfUtc,decimal MesPrice,
    OptionsGammaObservation? Gamma,VolatilityObservation? Volatility,IntermarketBreadthObservation? Breadth,
    int? DistanceToCallWallTicks,int? DistanceToPutWallTicks,bool? IsNegativeGammaRegime,
    bool? IsVolatilityExpanding,bool? IsBreadthDivergent,
    IReadOnlyList<string> MissingContext,string CalculationVersion);

public sealed record TransitionEvidence(string Feature,string State,decimal Contribution,string Explanation);

public sealed record StructuralTransitionRadarSnapshot(
    string PredictionId,string InstrumentId,DateTime AsOfUtc,string CurrentState,string PredictedTransition,
    string Direction,int HorizonMinutes,decimal TransitionProbability,decimal DirectionalConfidence,
    string CalibrationStatus,string ResearchAuthority,IReadOnlyList<TransitionEvidence> Evidence,
    IReadOnlyList<string> MissingContext,IntermarketContextSnapshot Context,string EngineVersion,string ContentHash);

public sealed record IntermarketObservationBatch(
    OptionsGammaObservation? Gamma,VolatilityObservation? Volatility,IntermarketBreadthObservation? Breadth);

public sealed record StructuralTransitionOutcome(
    string OutcomeId,string PredictionId,DateTime EvaluatedAtUtc,bool TransitionOccurred,string ActualDirection,
    decimal MaximumUpTicks,decimal MaximumDownTicks,bool PredictionSuccessful,decimal BrierScore,
    string EvaluatorVersion,string ContentHash);

public sealed record StructuralTransitionCalibration(
    int Predictions,int Evaluated,int Successful,decimal SuccessRate,decimal MeanBrierScore,
    string Status,IReadOnlyList<StructuralTransitionCalibrationBand> Bands,
    IReadOnlyList<StructuralTransitionOutcome> LatestOutcomes);

public sealed record StructuralTransitionCalibrationBand(
    string Band,int Predictions,decimal MeanPredictedProbability,decimal ActualTransitionRate,decimal MeanBrierScore);

public sealed record StructuralTransitionBackfillResult(
    DateTime StartUtc,DateTime EndUtc,int CandidateClocks,int PredictionsStored,
    StructuralTransitionCalibration Calibration,string EngineVersion);
