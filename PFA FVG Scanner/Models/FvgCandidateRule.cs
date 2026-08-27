namespace PFA_FVG_Scanner.Models
{
    public enum CandidateRuleStatus
    {
        InsufficientEvidence,
        ResearchCandidate,
        PromisingCandidate,
        NegativeExpectancy,
        RequiresValidation
    }

    public sealed class FvgCandidateRule
    {
        // ============================================================
        // IDENTITY
        // ============================================================

        public Guid RuleId { get; set; } =
            Guid.NewGuid();

        public string RuleName { get; set; } =
            string.Empty;

        // ============================================================
        // REQUIRED STRATEGY COMPONENTS
        //
        // EntryModel + TargetR are ALWAYS specified.
        //
        // This prevents one FVG from being counted several times
        // inside the same candidate strategy.
        // ============================================================

        public MesEntryModel EntryModel { get; set; }

        public decimal TargetR { get; set; }

        // ============================================================
        // OPTIONAL PRE-ENTRY FILTERS
        //
        // Null = this rule does not filter on that characteristic.
        // ============================================================

        public FvgDirection? Direction { get; set; }

        public FvgSessionBucket? SessionBucket { get; set; }

        public decimal? MinimumGapSizePoints { get; set; }

        public decimal? MaximumGapSizePoints { get; set; }

        public int? MinimumMinutesToEntry { get; set; }

        public int? MaximumMinutesToEntry { get; set; }

        public decimal? MinimumRiskTicks { get; set; }

        public decimal? MaximumRiskTicks { get; set; }

        // ============================================================
        // SAMPLE INFORMATION
        // ============================================================

        public int Trades { get; set; }

        public int DistinctFvgs { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal WinRate { get; set; }

        // ============================================================
        // R-BASED PERFORMANCE
        // ============================================================

        public decimal NetR { get; set; }

        public decimal AverageR { get; set; }

        public decimal ExpectancyR { get; set; }

        public decimal AverageWinnerR { get; set; }

        public decimal AverageLoserR { get; set; }

        // ============================================================
        // RAW 1-MES PERFORMANCE
        // ============================================================

        public decimal RawNetProfitLoss { get; set; }

        public decimal RawAverageProfitLoss { get; set; }

        // ============================================================
        // FIXED-RISK NORMALIZATION
        //
        // This answers:
        //
        // "What if every trade risked exactly the same dollar amount?"
        //
        // FixedRiskPnL = RealizedR × RiskBudget
        // ============================================================

        public decimal FixedRisk25NetProfitLoss { get; set; }

        public decimal FixedRisk25AverageProfitLoss { get; set; }

        public decimal FixedRisk50NetProfitLoss { get; set; }

        public decimal FixedRisk50AverageProfitLoss { get; set; }

        // ============================================================
        // QUALITY / RISK
        // ============================================================

        public decimal ProfitFactorR { get; set; }

        public decimal MaximumConsecutiveLosses { get; set; }

        public decimal MaximumDrawdownR { get; set; }

        // ============================================================
        // SAMPLE SAFEGUARDS
        // ============================================================

        public int MinimumSampleRequired { get; set; }

        public bool MeetsMinimumSample { get; set; }

        public bool PositiveExpectancy { get; set; }

        public bool RequiresOutOfSampleValidation { get; set; } =
            true;

        public CandidateRuleStatus Status { get; set; } =
            CandidateRuleStatus.InsufficientEvidence;

        // ============================================================
        // SCORE
        //
        // This is a RESEARCH ranking score only.
        //
        // It is NOT a probability and is NOT permission to trade.
        // ============================================================

        public decimal ResearchScore { get; set; }

        public string ResearchNotes { get; set; } =
            string.Empty;

        public string EngineVersion { get; set; } =
            "1.0.0";
    }

    public sealed class FvgCandidateDiscoveryReport
    {
        public int LearningRecordsEvaluated { get; set; }

        public int DistinctFvgsEvaluated { get; set; }

        public int CandidateRulesTested { get; set; }

        public int RulesMeetingMinimumSample { get; set; }

        public int PositiveExpectancyRules { get; set; }

        public int PromisingRules { get; set; }

        public int MinimumSampleRequired { get; set; }

        public string DatasetWarning { get; set; } =
            string.Empty;

        public IReadOnlyList<FvgCandidateRule> RankedCandidates
        {
            get;
            set;
        } = Array.Empty<FvgCandidateRule>();
    }
}