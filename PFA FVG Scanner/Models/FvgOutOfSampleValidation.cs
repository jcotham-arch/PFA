namespace PFA_FVG_Scanner.Models
{
    public enum ValidationDecision
    {
        InsufficientEvidence,
        FailedValidation,
        ContinueValidation,
        PassedValidation
    }

    public sealed class FrozenFvgCandidate
    {
        public Guid CandidateId { get; set; } =
            Guid.NewGuid();

        public string CandidateName { get; set; } =
            string.Empty;

        public MesEntryModel EntryModel { get; set; }

        public decimal TargetR { get; set; }

        public FvgDirection? Direction { get; set; }

        public FvgSessionBucket? SessionBucket { get; set; }

        public decimal? MinimumGapSizePoints { get; set; }

        public decimal? MaximumGapSizePoints { get; set; }

        public int? MinimumMinutesToEntry { get; set; }

        public int? MaximumMinutesToEntry { get; set; }

        public decimal? MinimumRiskTicks { get; set; }

        public decimal? MaximumRiskTicks { get; set; }

        // ============================================================
        // DISCOVERY PERFORMANCE
        //
        // Frozen reference values from the original discovery dataset.
        // They are NOT recalculated during validation.
        // ============================================================

        public int DiscoveryTrades { get; set; }

        public int DiscoveryDistinctFvgs { get; set; }

        public decimal DiscoveryWinRate { get; set; }

        public decimal DiscoveryExpectancyR { get; set; }

        public decimal DiscoveryProfitFactorR { get; set; }

        public decimal DiscoveryMaximumDrawdownR { get; set; }

        public DateTime FrozenAtUtc { get; set; } =
            DateTime.UtcNow;

        public string SourceEngineVersion { get; set; } =
            "1.0.0";
    }

    public sealed class FvgValidationDayResult
    {
        public DateTime ValidationDateUtc { get; set; }

        public int Trades { get; set; }

        public int DistinctFvgs { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal WinRate { get; set; }

        public decimal NetR { get; set; }

        public decimal ExpectancyR { get; set; }

        public decimal ProfitFactorR { get; set; }

        public decimal MaximumDrawdownR { get; set; }

        public decimal RawOneMesNetProfitLoss { get; set; }

        public decimal FixedRisk25NetProfitLoss { get; set; }

        public decimal FixedRisk50NetProfitLoss { get; set; }

        public bool WasPositiveDay { get; set; }
    }

    public sealed class FvgOutOfSampleValidationReport
    {
        public FrozenFvgCandidate Candidate { get; set; } =
            new();

        // ============================================================
        // DATASET
        // ============================================================

        public DateTime ValidationStartUtc { get; set; }

        public DateTime ValidationEndUtc { get; set; }

        public int DaysWithEligibleTrades { get; set; }

        public int TotalValidationRecordsEvaluated { get; set; }

        public int MatchingTrades { get; set; }

        public int DistinctFvgs { get; set; }

        // ============================================================
        // RESULTS
        // ============================================================

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal WinRate { get; set; }

        public decimal NetR { get; set; }

        public decimal ExpectancyR { get; set; }

        public decimal AverageWinnerR { get; set; }

        public decimal AverageLoserR { get; set; }

        public decimal ProfitFactorR { get; set; }

        public int MaximumConsecutiveLosses { get; set; }

        public decimal MaximumDrawdownR { get; set; }

        // ============================================================
        // CAPITAL MODELS
        // ============================================================

        public decimal RawOneMesNetProfitLoss { get; set; }

        public decimal FixedRisk25NetProfitLoss { get; set; }

        public decimal FixedRisk50NetProfitLoss { get; set; }

        // ============================================================
        // DAY-TO-DAY STABILITY
        // ============================================================

        public int PositiveDays { get; set; }

        public int NegativeDays { get; set; }

        public decimal PositiveDayPercentage { get; set; }

        public IReadOnlyList<FvgValidationDayResult> DailyResults
        {
            get;
            set;
        } = Array.Empty<FvgValidationDayResult>();

        // ============================================================
        // DISCOVERY VS VALIDATION
        // ============================================================

        public decimal ExpectancyRetentionPercentage { get; set; }

        public decimal WinRateChangePercentagePoints { get; set; }

        // ============================================================
        // PROMOTION GATES
        // ============================================================

        public int RequiredDistinctFvgs { get; set; }

        public int RequiredTradingDays { get; set; }

        public decimal RequiredMinimumExpectancyR { get; set; }

        public decimal RequiredMinimumProfitFactor { get; set; }

        public decimal RequiredMinimumPositiveDayPercentage { get; set; }

        public decimal MaximumAllowedDrawdownR { get; set; }

        public bool PassedSampleGate { get; set; }

        public bool PassedDayCountGate { get; set; }

        public bool PassedExpectancyGate { get; set; }

        public bool PassedProfitFactorGate { get; set; }

        public bool PassedPositiveDaysGate { get; set; }

        public bool PassedDrawdownGate { get; set; }

        public bool PassedAllPromotionGates { get; set; }

        // ============================================================
        // FINAL DECISION
        // ============================================================

        public ValidationDecision Decision { get; set; }

        public string DecisionReason { get; set; } =
            string.Empty;

        public bool CanActivateStrategy { get; set; } =
            false;

        public string NextRequiredStage { get; set; } =
            string.Empty;

        public string EngineVersion { get; set; } =
            "1.0.0";
    }
}