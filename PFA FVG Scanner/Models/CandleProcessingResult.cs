namespace PFA_FVG_Scanner.Models
{
    public sealed class CandleProcessingResult
    {
        public bool Accepted { get; set; }

        public string Message { get; set; } = string.Empty;

        public int CandlesInWindow { get; set; }

        public Candle? Candle { get; set; }

        public FairValueGap? DetectedFvg { get; set; }
    }
}