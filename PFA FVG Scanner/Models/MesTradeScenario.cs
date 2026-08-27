namespace PFA_FVG_Scanner.Models
{
    public enum MesEntryModel
    {
        BoundaryTouch,
        TwentyFivePercent,
        FiftyPercent,
        SeventyFivePercent
    }

    public enum MesScenarioStatus
    {
        NoEntry,
        Open,
        TargetHit,
        StopHit,
        EndOfData
    }

    public sealed class MesTradeScenario
    {
        // ============================================================
        // IDENTITY
        // ============================================================

        public Guid ScenarioId { get; set; } =
            Guid.NewGuid();

        public Guid FvgId { get; set; }

        public Guid OutcomeId { get; set; }

        public string Symbol { get; set; } =
            string.Empty;

        public string Timeframe { get; set; } =
            string.Empty;

        public FvgDirection Direction { get; set; }

        public DateTime FormationTimeUtc { get; set; }

        public DateTime ConfirmationTimeUtc { get; set; }

        // ============================================================
        // SCENARIO
        // ============================================================

        public MesEntryModel EntryModel { get; set; }

        public int Contracts { get; set; }

        public decimal DollarsPerPointPerContract { get; set; } =
            5.00m;

        public decimal TickSize { get; set; } =
            0.25m;

        public decimal DollarsPerTickPerContract { get; set; } =
            1.25m;

        // ============================================================
        // ENTRY
        //
        // TheoreticalEntryPrice:
        // mathematical FVG level.
        //
        // EntryPrice:
        // actual tick-normalized MES order price.
        // ============================================================

        public decimal TheoreticalEntryPrice { get; set; }

        public decimal? EntryPrice { get; set; }

        public decimal EntryNormalizationPoints { get; set; }

        public bool EntryAvailable { get; set; }

        public bool EntryTriggered { get; set; }

        public DateTime? EntryTimeUtc { get; set; }

        // ============================================================
        // STOP
        // ============================================================

        public decimal TheoreticalStopPrice { get; set; }

        public decimal? StopPrice { get; set; }

        public decimal StopNormalizationPoints { get; set; }

        public decimal? RiskPoints { get; set; }

        public decimal? RiskTicks { get; set; }

        public decimal? GrossDollarRisk { get; set; }

        // ============================================================
        // TARGET
        // ============================================================

        public decimal TargetR { get; set; }

        public decimal TheoreticalTargetPrice { get; set; }

        public decimal? TargetPrice { get; set; }

        public decimal TargetNormalizationPoints { get; set; }

        public decimal? TargetPoints { get; set; }

        /// <summary>
        /// Actual R after target tick normalization.
        /// It may be slightly greater than TargetR.
        /// </summary>
        public decimal? EffectiveTargetR { get; set; }

        public decimal? GrossTargetProfit { get; set; }

        // ============================================================
        // PRICE VALIDATION
        // ============================================================

        public bool EntryPriceIsValidTick { get; set; }

        public bool StopPriceIsValidTick { get; set; }

        public bool TargetPriceIsValidTick { get; set; }

        public bool AllExecutionPricesValid { get; set; }

        // ============================================================
        // POST-ENTRY MOVEMENT
        // ============================================================

        public decimal? MaximumFavorableExcursionPoints { get; set; }

        public decimal? MaximumAdverseExcursionPoints { get; set; }

        public decimal? MaximumFavorableR { get; set; }

        public decimal? MaximumAdverseR { get; set; }

        // ============================================================
        // EXECUTION OUTCOME
        // ============================================================

        public bool TargetHit { get; set; }

        public DateTime? TargetHitTimeUtc { get; set; }

        public bool StopHit { get; set; }

        public DateTime? StopHitTimeUtc { get; set; }

        public bool TargetBeforeStop { get; set; }

        public bool StopBeforeTarget { get; set; }

        public bool IntrabarSequenceUnknown { get; set; }

        public MesScenarioStatus Status { get; set; } =
            MesScenarioStatus.NoEntry;

        // ============================================================
        // REALIZED RESULT
        // ============================================================

        public decimal? RealizedPoints { get; set; }

        public decimal? GrossProfitLoss { get; set; }

        public decimal? RealizedR { get; set; }

        public bool? WasProfitable { get; set; }

        // ============================================================
        // COST MODEL
        // ============================================================

        public decimal CommissionPerContractRoundTrip { get; set; }

        public decimal EstimatedSlippagePointsPerSide { get; set; }

        public decimal TotalEstimatedCommission { get; set; }

        public decimal TotalEstimatedSlippageCost { get; set; }

        public decimal TotalEstimatedTradingCost { get; set; }

        public decimal? NetProfitLoss { get; set; }

        // ============================================================
        // CAPITAL / MARGIN
        // ============================================================

        public decimal? EstimatedMarginPerContract { get; set; }

        public decimal? EstimatedMarginRequired { get; set; }

        // ============================================================
        // REPLAY
        // ============================================================

        public int MinuteCandlesEvaluatedAfterEntry { get; set; }

        public DateTime? EvaluatedThroughUtc { get; set; }

        // ============================================================
        // VERSION
        // ============================================================

        public string EngineVersion { get; set; } =
            "1.1.0";
    }
}