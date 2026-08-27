using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.MarketData
{
    public interface IMarketDataProvider
    {
        string ProviderName { get; }

        MarketDataConnectionState ConnectionState { get; }

        event Func<Candle, Task>? ClosedCandleReceived;

        Task ConnectAsync(
            CancellationToken cancellationToken = default);

        Task DisconnectAsync(
            CancellationToken cancellationToken = default);

        Task SubscribeAsync(
            string symbol,
            string timeframe,
            CancellationToken cancellationToken = default);

        Task UnsubscribeAsync(
            string symbol,
            string timeframe,
            CancellationToken cancellationToken = default);
    }
}