using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.MarketData
{
    public sealed class SimulatedMarketDataProvider
        : IMarketDataProvider
    {
        private string? _subscribedSymbol;
        private string? _subscribedTimeframe;

        public string ProviderName =>
            "PFA Simulated Market Data";

        public MarketDataConnectionState ConnectionState { get; }
            = new()
            {
                Provider = "PFA Simulated Market Data",
                Status = MarketDataConnectionStatus.Disconnected,
                Message = "Simulator is disconnected."
            };

        public event Func<Candle, Task>? ClosedCandleReceived;

        public Task ConnectAsync(
            CancellationToken cancellationToken = default)
        {
            ConnectionState.Status =
                MarketDataConnectionStatus.Connected;

            ConnectionState.Message =
                "Simulator connected.";

            ConnectionState.ConnectedAtUtc =
                DateTime.UtcNow;

            return Task.CompletedTask;
        }

        public Task DisconnectAsync(
            CancellationToken cancellationToken = default)
        {
            ConnectionState.Status =
                MarketDataConnectionStatus.Disconnected;

            ConnectionState.Message =
                "Simulator disconnected.";

            ConnectionState.Symbol = null;
            ConnectionState.Timeframe = null;

            _subscribedSymbol = null;
            _subscribedTimeframe = null;

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
                    "Market data provider is not connected.");
            }

            _subscribedSymbol =
                symbol.Trim().ToUpperInvariant();

            _subscribedTimeframe =
                timeframe.Trim().ToLowerInvariant();

            ConnectionState.Symbol =
                _subscribedSymbol;

            ConnectionState.Timeframe =
                _subscribedTimeframe;

            ConnectionState.Message =
                $"Subscribed to {_subscribedSymbol} {_subscribedTimeframe}.";

            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(
            string symbol,
            string timeframe,
            CancellationToken cancellationToken = default)
        {
            _subscribedSymbol = null;
            _subscribedTimeframe = null;

            ConnectionState.Symbol = null;
            ConnectionState.Timeframe = null;

            ConnectionState.Message =
                "Subscription removed.";

            return Task.CompletedTask;
        }

        public async Task PublishClosedCandleAsync(
            Candle candle)
        {
            if (ConnectionState.Status !=
                MarketDataConnectionStatus.Connected)
            {
                throw new InvalidOperationException(
                    "Simulator is not connected.");
            }

            if (string.IsNullOrWhiteSpace(
                    _subscribedSymbol) ||
                string.IsNullOrWhiteSpace(
                    _subscribedTimeframe))
            {
                throw new InvalidOperationException(
                    "No market-data subscription is active.");
            }

            if (!candle.Symbol.Equals(
                    _subscribedSymbol,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Expected symbol {_subscribedSymbol}, " +
                    $"but received {candle.Symbol}.");
            }

            if (!candle.Timeframe.Equals(
                    _subscribedTimeframe,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Expected timeframe {_subscribedTimeframe}, " +
                    $"but received {candle.Timeframe}.");
            }

            if (!candle.IsClosed)
            {
                throw new InvalidOperationException(
                    "Only closed candles may be published.");
            }

            ConnectionState.LastCandleReceivedUtc =
                DateTime.UtcNow;

            if (ClosedCandleReceived is null)
            {
                return;
            }

            foreach (Func<Candle, Task> handler
                     in ClosedCandleReceived.GetInvocationList())
            {
                await handler(candle);
            }
        }
    }
}