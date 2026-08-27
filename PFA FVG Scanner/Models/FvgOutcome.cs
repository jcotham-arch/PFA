namespace PFA_FVG_Scanner.Models
{
    public enum FvgLifecycleStatus
    {
        Formed,
        NoRetracement,
        BoundaryTouched,
        TwentyFivePercentFilled,
        FiftyPercentFilled,
        SeventyFivePercentFilled,
        FullyFilled
    }

    public sealed class FvgOutcome
    {
        // ============================================================
        // IDENTITY
        // ============================================================

        public Guid OutcomeId { get; set; } =
            Guid.NewGuid();

        public Guid FvgId { get; set; }

        public string Symbol { get; set; } =
            string.Empty;

        public string Timeframe { get; set; } =
            string.Empty;

        public FvgDirection Direction { get; set; }

        // ============================================================
        // FVG FORMATION
        // ============================================================

        /// <summary>
        /// Opening time of the third candle that creates the FVG.
        /// </summary>
        public DateTime FormationTimeUtc { get; set; }

        /// <summary>
        /// Time at which the FVG is actually knowable.
        ///
        /// For a 5-minute FVG formed on the 14:15 candle,
        /// confirmation occurs at 14:20.
        /// </summary>
        public DateTime ConfirmationTimeUtc { get; set; }

        public decimal LowerBoundary { get; set; }

        public decimal UpperBoundary { get; set; }

        public decimal Midpoint { get; set; }

        public decimal GapSize { get; set; }

        // ============================================================
        // MARKET LIFECYCLE CLASSIFICATION
        // ============================================================

        /// <summary>
        /// Highest mitigation stage reached during the observation
        /// window.
        /// </summary>
        public FvgLifecycleStatus LifecycleStatus { get; set; } =
            FvgLifecycleStatus.Formed;

        /// <summary>
        /// True only if price actually returned to the outer FVG
        /// boundary after confirmation.
        ///
        /// This is the minimum requirement for a boundary-entry
        /// scenario to have been executable.
        /// </summary>
        public bool WasBoundaryEntryOffered { get; set; }

        /// <summary>
        /// True when the FVG was completely mitigated.
        /// </summary>
        public bool WasFullyFilled { get; set; }

        // ============================================================
        // FIRST TOUCH
        // ============================================================

        public DateTime? FirstTouchTimeUtc { get; set; }

        public decimal? FirstTouchPrice { get; set; }

        /// <summary>
        /// Minutes from FVG confirmation until the first retracement
        /// to the boundary.
        /// </summary>
        public int? MinutesToFirstTouch { get; set; }

        // ============================================================
        // MITIGATION LEVELS
        // ============================================================

        public DateTime? TwentyFivePercentFillTimeUtc { get; set; }

        public DateTime? FiftyPercentFillTimeUtc { get; set; }

        public DateTime? SeventyFivePercentFillTimeUtc { get; set; }

        public DateTime? FullFillTimeUtc { get; set; }

        public int? MinutesToTwentyFivePercentFill { get; set; }

        public int? MinutesToFiftyPercentFill { get; set; }

        public int? MinutesToSeventyFivePercentFill { get; set; }

        public int? MinutesToFullFill { get; set; }

        // ============================================================
        // MARKET BEHAVIOR AFTER CONFIRMATION
        //
        // IMPORTANT:
        //
        // These are MARKET measurements.
        //
        // They do NOT mean a trade could have captured these values.
        //
        // Trade-specific MFE / MAE will be calculated separately by
        // the MES Scenario Engine after a particular entry is actually
        // triggered.
        // ============================================================

        public decimal? MaximumFavorableExcursion { get; set; }

        public decimal? MaximumAdverseExcursion { get; set; }

        public decimal? HighestPriceAfterSetup { get; set; }

        public decimal? LowestPriceAfterSetup { get; set; }

        // ============================================================
        // DIRECTIONAL MARKET RETURNS
        //
        // These answer:
        //
        // "What did price do after this FVG became confirmed?"
        //
        // Positive = movement in the expected FVG direction.
        // Negative = movement against the expected FVG direction.
        //
        // These are NOT realized trade returns.
        // ============================================================

        public decimal? Return5Minutes { get; set; }

        public decimal? Return15Minutes { get; set; }

        public decimal? Return30Minutes { get; set; }

        public decimal? Return60Minutes { get; set; }

        // ============================================================
        // OBSERVATION WINDOW
        // ============================================================

        /// <summary>
        /// Number of minutes the FVG remained under observation.
        ///
        /// If completely filled, this represents the lifecycle until
        /// mitigation.
        ///
        /// Otherwise it extends through the available dataset.
        /// </summary>
        public int? SetupLifetimeMinutes { get; set; }

        /// <summary>
        /// Final market-data timestamp available to this replay.
        /// </summary>
        public DateTime EvaluatedThroughUtc { get; set; }

        /// <summary>
        /// Total number of post-confirmation one-minute candles used
        /// by the lifecycle analysis.
        /// </summary>
        public int MinuteCandlesEvaluated { get; set; }

        // ============================================================
        // EXECUTION ELIGIBILITY
        //
        // These fields deliberately stop short of calculating P&L.
        //
        // They simply tell the scenario engine which hypothetical
        // entries were actually offered by the market.
        // ============================================================

        public bool BoundaryEntryAvailable =>
            FirstTouchTimeUtc.HasValue;

        public bool TwentyFivePercentEntryAvailable =>
            TwentyFivePercentFillTimeUtc.HasValue;

        public bool FiftyPercentEntryAvailable =>
            FiftyPercentFillTimeUtc.HasValue;

        public bool SeventyFivePercentEntryAvailable =>
            SeventyFivePercentFillTimeUtc.HasValue;

        // ============================================================
        // VERSIONING
        // ============================================================

        public string EngineVersion { get; set; } =
            "1.1.0";
    }
}