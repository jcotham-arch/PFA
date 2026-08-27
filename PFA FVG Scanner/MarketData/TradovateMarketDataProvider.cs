using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.MarketData
{
    public sealed class TradovateMarketDataProvider
        : IMarketDataProvider
    {
        private readonly TradovateOptions _options;

        public TradovateMarketDataProvider(
            TradovateOptions options)
        {
            _options = options;

            ConnectionState = new MarketDataConnectionState
            {
                Provider = ProviderName,
                Status =
                    MarketDataConnectionStatus.Disconnected,
                Message =
                    "Tradovate provider is configured but not connected."
            };
        }

        public string ProviderName =>
            "Tradovate Market Data";

        public MarketDataConnectionState ConnectionState
        {
            get;
        }

        public event Func<Candle, Task>?
            ClosedCandleReceived;

        public Task ConnectAsync(
            CancellationToken cancellationToken = default)
        {
            ConnectionState.Status =
                MarketDataConnectionStatus.Connecting;

            ConnectionState.Message =
                "Tradovate connection is not activated yet. " +
                "Credentials and live WebSocket authentication " +
                "must be configured first.";

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(
            CancellationToken cancellationToken = default)
        {
            ConnectionState.Status =
                MarketDataConnectionStatus.Disconnected;

            ConnectionState.Message =
                "Tradovate disconnected.";

            ConnectionState.Symbol = null;
            ConnectionState.Timeframe = null;

            return Task.CompletedTask;
        }

        public Task SubscribeAsync(
            string symbol,
            string timeframe,
            CancellationToken cancellationToken = default)
        {
            if (ConnectionState.Status !=
                MarketDataConnectionStatus.Connected)
            {
                throw new InvalidOperationException(
                    "Tradovate is not connected.");
            }

            ConnectionState.Symbol =
                symbol.Trim().ToUpperInvariant();

            ConnectionState.Timeframe =
                timeframe.Trim().ToLowerInvariant();

            ConnectionState.Message =
                $"Subscribed to " +
                $"{ConnectionState.Symbol} " +
                $"{ConnectionState.Timeframe}.";

            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(
            string symbol,
            string timeframe,
            CancellationToken cancellationToken = default)
        {
            ConnectionState.Symbol = null;
            ConnectionState.Timeframe = null;

            ConnectionState.Message =
                "Tradovate subscription removed.";

            return Task.CompletedTask;
        }

        private async Task PublishClosedCandleAsync(
            Candle candle)
        {
            ConnectionState.LastCandleReceivedUtc =
                DateTime.UtcNow;

            if (ClosedCandleReceived is null)
            {
                return;
            }

            foreach (Func<Candle, Task> handler
                     in ClosedCandleReceived
                         .GetInvocationList())
            {
                await handler(candle);
            }
        }
    }
}