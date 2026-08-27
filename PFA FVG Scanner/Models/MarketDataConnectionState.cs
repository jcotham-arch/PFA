namespace PFA_FVG_Scanner.Models
{
    public enum MarketDataConnectionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Error
    }

    public sealed class MarketDataConnectionState
    {
        public string Provider { get; set; } = string.Empty;

        public MarketDataConnectionStatus Status { get; set; }
            = MarketDataConnectionStatus.Disconnected;

        public string Message { get; set; } = string.Empty;

        public DateTime? ConnectedAtUtc { get; set; }

        public DateTime? LastCandleReceivedUtc { get; set; }

        public string? Symbol { get; set; }

        public string? Timeframe { get; set; }
    }
}