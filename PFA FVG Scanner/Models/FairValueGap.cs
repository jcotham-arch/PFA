namespace PFA_FVG_Scanner.Models
{
    public enum FvgDirection
    {
        Bullish,
        Bearish
    }

    public enum FvgStatus
    {
        New,
        Active,
        PartiallyFilled,
        FiftyPercentFilled,
        FullyFilled,
        Invalidated
    }

    public sealed class FairValueGap
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Symbol { get; set; } = "MES";

        public string Timeframe { get; set; } = "5m";

        public FvgDirection Direction { get; set; }

        public DateTime FormationTimeUtc { get; set; }

        public decimal LowerBoundary { get; set; }

        public decimal UpperBoundary { get; set; }

        public decimal GapSize { get; set; }

        public decimal Midpoint =>
            (LowerBoundary + UpperBoundary) / 2m;

        public decimal? CurrentPrice { get; set; }

        public decimal FillPercentage { get; set; }

        public FvgStatus Status { get; set; } = FvgStatus.New;

        public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;
    }
}