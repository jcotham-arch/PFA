namespace PFA_FVG_Scanner.MarketData
{
    public sealed class TradovateOptions
    {
        public string Environment { get; set; } = "Demo";

        public string ApiBaseUrl { get; set; }
            = "https://demo.tradovateapi.com/v1";

        public string MarketDataWebSocketUrl { get; set; }
            = "wss://md.tradovateapi.com/v1/websocket";

        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string AppId { get; set; } = "PFA-FVG-Scanner";

        public string AppVersion { get; set; } = "0.1";

        public string Cid { get; set; } = string.Empty;

        public string Sec { get; set; } = string.Empty;
    }
}