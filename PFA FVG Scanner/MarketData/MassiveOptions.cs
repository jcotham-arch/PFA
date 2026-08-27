namespace PFA_FVG_Scanner.MarketData
{
    public sealed class MassiveOptions
    {
        public string Feed { get; set; } = "Delayed";

        public string WebSocketUrl { get; set; }
            = "wss://delayed.massive.com/futures";

        public string ApiBaseUrl { get; set; }
            = "https://api.massive.com";

        public string ApiKey { get; set; } = string.Empty;

        public string ContractTicker { get; set; } = string.Empty;

        public string SourceTimeframe { get; set; } = "1m";

        public string TargetTimeframe { get; set; } = "5m";
    }
}