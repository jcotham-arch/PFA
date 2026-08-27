using PFA_FVG_Scanner.MarketData;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class MarketDataWatchdogService : BackgroundService
    {
        private readonly IMarketDataProvider _provider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<MarketDataWatchdogService> _logger;

        private readonly TimeSpan _checkInterval =
            TimeSpan.FromSeconds(30);

        private readonly TimeSpan _staleThreshold =
            TimeSpan.FromMinutes(3);

        private DateTime? _lastReconnectAttemptUtc;

        public bool IsFeedHealthy { get; private set; }

        public bool IsFeedStale { get; private set; }

        public DateTime? LastHealthCheckUtc { get; private set; }

        public DateTime? LastReconnectAttemptUtc =>
            _lastReconnectAttemptUtc;

        public string HealthMessage { get; private set; } =
            "Watchdog has not started yet.";

        public MarketDataWatchdogService(
            IMarketDataProvider provider,
            IConfiguration configuration,
            ILogger<MarketDataWatchdogService> logger)
        {
            _provider = provider;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "PFA market-data watchdog started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckFeedAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    IsFeedHealthy = false;

                    HealthMessage =
                        $"Watchdog error: {ex.Message}";

                    _logger.LogError(
                        ex,
                        "Market-data watchdog check failed.");
                }

                try
                {
                    await Task.Delay(
                        _checkInterval,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task CheckFeedAsync(
            CancellationToken cancellationToken)
        {
            LastHealthCheckUtc = DateTime.UtcNow;

            MarketDataConnectionState state =
                _provider.ConnectionState;

            // --------------------------------------------------------
            // PROVIDER IS NOT CONNECTED
            // --------------------------------------------------------

            if (state.Status !=
                MarketDataConnectionStatus.Connected)
            {
                IsFeedHealthy = false;
                IsFeedStale = true;

                HealthMessage =
                    $"Provider status is {state.Status}. " +
                    "Attempting recovery.";

                await RecoverConnectionAsync(
                    cancellationToken);

                return;
            }

            // --------------------------------------------------------
            // CONNECTED BUT NO DATA HAS ARRIVED YET
            // --------------------------------------------------------

            if (!state.LastCandleReceivedUtc.HasValue)
            {
                if (!state.ConnectedAtUtc.HasValue)
                {
                    IsFeedHealthy = false;
                    IsFeedStale = true;

                    HealthMessage =
                        "Provider reports Connected, but no " +
                        "connection timestamp is available.";

                    await RecoverConnectionAsync(
                        cancellationToken);

                    return;
                }

                TimeSpan connectedFor =
                    DateTime.UtcNow -
                    state.ConnectedAtUtc.Value;

                if (connectedFor <= _staleThreshold)
                {
                    IsFeedHealthy = true;
                    IsFeedStale = false;

                    HealthMessage =
                        "Connected and waiting for the first " +
                        "market-data event.";

                    return;
                }

                IsFeedHealthy = false;
                IsFeedStale = true;

                HealthMessage =
                    $"Connected for {connectedFor.TotalMinutes:F1} " +
                    "minutes without receiving market data.";

                await RecoverConnectionAsync(
                    cancellationToken);

                return;
            }

            // --------------------------------------------------------
            // DATA HAS ARRIVED - CHECK HOW OLD IT IS
            // --------------------------------------------------------

            TimeSpan age =
                DateTime.UtcNow -
                state.LastCandleReceivedUtc.Value;

            if (age <= _staleThreshold)
            {
                IsFeedHealthy = true;
                IsFeedStale = false;

                HealthMessage =
                    $"Feed healthy. Last market-data event " +
                    $"{age.TotalSeconds:F0} seconds ago.";

                return;
            }

            // --------------------------------------------------------
            // FEED IS STALE
            // --------------------------------------------------------

            IsFeedHealthy = false;
            IsFeedStale = true;

            HealthMessage =
                $"Feed stale. No market data received for " +
                $"{age.TotalMinutes:F1} minutes. " +
                "Attempting reconnect and resubscribe.";

            _logger.LogWarning(
                "Market-data feed stale. " +
                "Last data received at {LastDataUtc}.",
                state.LastCandleReceivedUtc);

            await RecoverConnectionAsync(
                cancellationToken);
        }

        private async Task RecoverConnectionAsync(
            CancellationToken cancellationToken)
        {
            // Prevent reconnect storms.
            if (_lastReconnectAttemptUtc.HasValue)
            {
                TimeSpan sinceLastAttempt =
                    DateTime.UtcNow -
                    _lastReconnectAttemptUtc.Value;

                if (sinceLastAttempt <
                    TimeSpan.FromMinutes(1))
                {
                    return;
                }
            }

            _lastReconnectAttemptUtc =
                DateTime.UtcNow;

            string symbol =
                _configuration[
                    "Massive:ContractTicker"]
                ?? "MESU6";

            string timeframe =
                _configuration[
                    "Massive:SourceTimeframe"]
                ?? "1m";

            try
            {
                _logger.LogWarning(
                    "Attempting market-data recovery for " +
                    "{Symbol} {Timeframe}.",
                    symbol,
                    timeframe);

                try
                {
                    await _provider.DisconnectAsync(
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Disconnect during recovery produced an error.");
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(2),
                    cancellationToken);

                await _provider.ConnectAsync(
                    cancellationToken);

                // Give the provider time to authenticate.
                await Task.Delay(
                    TimeSpan.FromSeconds(3),
                    cancellationToken);

                if (_provider.ConnectionState.Status !=
                    MarketDataConnectionStatus.Connected)
                {
                    IsFeedHealthy = false;

                    HealthMessage =
                        "Reconnect attempt did not reach " +
                        "Connected status.";

                    return;
                }

                await _provider.SubscribeAsync(
                    symbol,
                    timeframe,
                    cancellationToken);

                IsFeedHealthy = false;
                IsFeedStale = false;

                HealthMessage =
                    $"Recovery completed. Subscribed to " +
                    $"{symbol} {timeframe}; waiting for data.";

                _logger.LogInformation(
                    "Market-data recovery completed for " +
                    "{Symbol} {Timeframe}.",
                    symbol,
                    timeframe);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                IsFeedHealthy = false;
                IsFeedStale = true;

                HealthMessage =
                    $"Recovery failed: {ex.Message}";

                _logger.LogError(
                    ex,
                    "Market-data recovery failed.");
            }
        }
    }
}