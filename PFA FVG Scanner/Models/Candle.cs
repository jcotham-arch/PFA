namespace PFA_FVG_Scanner.Models
{
    public sealed class Candle
    {
        public string Symbol { get; set; } = "MES";

        public string Timeframe { get; set; } = "5m";

        public DateTime OpenTimeUtc { get; set; }

        public decimal Open { get; set; }

        public decimal High { get; set; }

        public decimal Low { get; set; }

        public decimal Close { get; set; }

        public decimal Volume { get; set; }

        public bool IsClosed { get; set; } = true;
    }
}