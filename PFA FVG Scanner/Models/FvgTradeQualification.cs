namespace PFA_FVG_Scanner.Models
{
    public sealed class FvgTradeQualification
    {
        public string QualificationId { get; set; } =
            Guid.NewGuid().ToString();

        public string FvgId { get; set; } =
            string.Empty;

        public string Symbol { get; set; } =
            string.Empty;

        public string Timeframe { get; set; } =
            string.Empty;

        public string Direction { get; set; } =
            string.Empty;

        public DateTime FormationTimeUtc { get; set; }

        public decimal LowerBoundary { get; set; }

        public decimal UpperBoundary { get; set; }

        public decimal Midpoint { get; set; }

        public decimal GapSize { get; set; }

        public EntryQualification BoundaryTouch { get; set; } =
            new();

        public EntryQualification TwentyFivePercent { get; set; } =
            new();

        public EntryQualification FiftyPercent { get; set; } =
            new();

        public EntryQualification SeventyFivePercent { get; set; } =
            new();

        public bool AnyEntryTriggered { get; set; }

        public DateTime? FirstEntryTriggeredUtc { get; set; }

        public decimal? FirstEntryPrice { get; set; }

        public decimal? HighestPriceAfterFormation { get; set; }

        public decimal? LowestPriceAfterFormation { get; set; }

        public decimal MaximumFavorableExcursion { get; set; }

        public decimal MaximumAdverseExcursion { get; set; }

        public decimal MaximumFavorableR { get; set; }

        public string QualificationStatus { get; set; } =
            "WaitingForEntry";

        public bool IsTradable { get; set; }

        public bool IsExpired { get; set; }

        public string? RejectionReason { get; set; }

        public string Source { get; set; } =
            "Live";

        public DateTime? HistoricalRecoveryTimeUtc { get; set; }

        public DateTime LastEvaluatedUtc { get; set; } =
            DateTime.UtcNow;

        public string QualificationEngineVersion { get; set; } =
            "1.1.0";
    }

    public sealed class EntryQualification
    {
        public string EntryModel { get; set; } =
            string.Empty;

        public decimal EntryPrice { get; set; }

        public bool Triggered { get; set; }

        public DateTime? TriggeredAtUtc { get; set; }

        public decimal? ActualFillPrice { get; set; }

        public decimal? StopPrice { get; set; }

        public decimal RiskPoints { get; set; }

        public decimal MaximumFavorableExcursion { get; set; }

        public decimal MaximumAdverseExcursion { get; set; }

        public decimal MaximumFavorableR { get; set; }

        public decimal MaximumAdverseR { get; set; }

        public bool Hit1R { get; set; }

        public bool Hit1Point5R { get; set; }

        public bool Hit2R { get; set; }

        public bool Hit3R { get; set; }

        public DateTime? Hit1RTimeUtc { get; set; }

        public DateTime? Hit1Point5RTimeUtc { get; set; }

        public DateTime? Hit2RTimeUtc { get; set; }

        public DateTime? Hit3RTimeUtc { get; set; }

        public bool StopHit { get; set; }

        public DateTime? StopHitTimeUtc { get; set; }

        public string OneROutcome { get; set; } =
            "NotResolved";

        public string OnePointFiveROutcome { get; set; } =
            "NotResolved";

        public string TwoROutcome { get; set; } =
            "NotResolved";

        public string ThreeROutcome { get; set; } =
            "NotResolved";

        public decimal HighestConfirmedProfitableR { get; set; }

        public bool AnyProfitableExitModel { get; set; }

        public bool WasProfitable { get; set; }

        public string Status { get; set; } =
            "Waiting";
    }
}