namespace PFA_FVG_Scanner.Models
{
    public enum FvgFeatureOutcome
    {
        Win,
        Loss
    }

    public enum FvgSessionBucket
    {
        Overnight,
        Premarket,
        RegularMorning,
        RegularMidday,
        RegularAfternoon,
        PostMarket,
        Unknown
    }

    public sealed class FvgFeatureRecord
    {
        // ============================================================
        // IDENTITY
        // ============================================================

        public Guid FeatureRecordId { get; set; } =
            Guid.NewGuid();

        public Guid FvgId { get; set; }

        public Guid OutcomeId { get; set; }

        public Guid ScenarioId { get; set; }

        public string Symbol { get; set; } =
            string.Empty;

        public string Timeframe { get; set; } =
            string.Empty;

        // ============================================================
        // FVG CHARACTERISTICS
        //
        // These were knowable when the FVG formed.
        // ============================================================

        public FvgDirection Direction { get; set; }

        public DateTime FormationTimeUtc { get; set; }

        public DateTime ConfirmationTimeUtc { get; set; }

        public decimal LowerBoundary { get; set; }

        public decimal UpperBoundary { get; set; }

        public decimal Midpoint { get; set; }

        public decimal GapSizePoints { get; set; }

        public decimal GapSizeTicks { get; set; }

        // ============================================================
        // TIME / SESSION
        // ============================================================

        public int FormationHourUtc { get; set; }

        public int FormationMinuteUtc { get; set; }

        public FvgSessionBucket SessionBucket { get; set; }

        // ============================================================
        // ENTRY MODEL
        //
        // These describe the scenario being tested.
        // ============================================================

        public MesEntryModel EntryModel { get; set; }

        public decimal TargetR { get; set; }

        public decimal? EffectiveTargetR { get; set; }

        // ============================================================
        // EXECUTION CHARACTERISTICS
        //
        // These values are known when the trade becomes executable.
        // ============================================================

        public DateTime EntryTimeUtc { get; set; }

        public int MinutesFromConfirmationToEntry { get; set; }

        public decimal EntryPrice { get; set; }

        public decimal StopPrice { get; set; }

        public decimal RiskPoints { get; set; }

        public decimal RiskTicks { get; set; }

        public decimal GrossDollarRiskOneContract { get; set; }

        // ============================================================
        // EXECUTION NORMALIZATION
        // ============================================================

        public decimal TheoreticalEntryPrice { get; set; }

        public decimal EntryNormalizationPoints { get; set; }

        public decimal TheoreticalStopPrice { get; set; }

        public decimal StopNormalizationPoints { get; set; }

        public decimal TheoreticalTargetPrice { get; set; }

        public decimal TargetPrice { get; set; }

        public decimal TargetNormalizationPoints { get; set; }

        // ============================================================
        // OUTCOME
        //
        // IMPORTANT:
        //
        // These fields are labels/results.
        //
        // They must NEVER be used as pre-entry predictors.
        // ============================================================

        public FvgFeatureOutcome Outcome { get; set; }

        public decimal RealizedR { get; set; }

        public decimal GrossProfitLoss { get; set; }

        public decimal NetProfitLoss { get; set; }

        public DateTime? TargetHitTimeUtc { get; set; }

        public DateTime? StopHitTimeUtc { get; set; }

        // ============================================================
        // POST-ENTRY DIAGNOSTICS
        //
        // These help us understand winners and losers AFTER the fact.
        //
        // They are explicitly not predictor features.
        // ============================================================

        public decimal? MaximumFavorableExcursionPoints { get; set; }

        public decimal? MaximumAdverseExcursionPoints { get; set; }

        public decimal? MaximumFavorableR { get; set; }

        public decimal? MaximumAdverseR { get; set; }

        public int MinuteCandlesEvaluatedAfterEntry { get; set; }

        // ============================================================
        // DATA QUALITY
        // ============================================================

        public bool ExecutionPricesValid { get; set; }

        public bool IntrabarSequenceWasKnown { get; set; }

        public bool IncludedInLearningPopulation { get; set; }

        public string ExclusionReason { get; set; } =
            string.Empty;

        // ============================================================
        // VERSIONING
        // ============================================================

        public string FeatureEngineVersion { get; set; } =
            "1.0.0";

        public string ScenarioEngineVersion { get; set; } =
            string.Empty;
    }
}