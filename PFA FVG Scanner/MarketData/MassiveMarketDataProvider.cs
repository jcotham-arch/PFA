using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.MarketData
{
    public sealed class MassiveMarketDataProvider : IMarketDataProvider
    {
        private readonly MassiveOptions _options;
        private readonly FiveMinuteCandleAggregator _aggregator;
        private readonly RawMarketEventRepository _rawRepository;

        private ClientWebSocket? _socket;
        private CancellationTokenSource? _receiveCts;

        private string? _lastRawMessage;
        private DateTime? _lastRawMessageUtc;

        public MassiveMarketDataProvider(
            MassiveOptions options,
            FiveMinuteCandleAggregator aggregator,
            RawMarketEventRepository rawRepository)
        {
            _options = options;
            _aggregator = aggregator;
            _rawRepository = rawRepository;

            ConnectionState = new MarketDataConnectionState
            {
                Provider = ProviderName,
                Status = MarketDataConnectionStatus.Disconnected,
                Message = "Massive provider is disconnected."
            };
        }

        public string ProviderName =>
            "Massive Futures Market Data";

        public MarketDataConnectionState ConnectionState { get; }

        public string? LastRawMessage => _lastRawMessage;

        public DateTime? LastRawMessageUtc => _lastRawMessageUtc;

        public event Func<Candle, Task>? ClosedCandleReceived;

        public async Task ConnectAsync(
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                ConnectionState.Status =
                    MarketDataConnectionStatus.Error;

                ConnectionState.Message =
                    "Massive API key is missing.";

                return;
            }

            if (_socket is not null &&
                _socket.State == WebSocketState.Open)
            {
                return;
            }

            try
            {
                ConnectionState.Status =
                    MarketDataConnectionStatus.Connecting;

                ConnectionState.Message =
                    "Connecting to Massive Futures WebSocket...";

                _socket = new ClientWebSocket();

                await _socket.ConnectAsync(
                    new Uri(_options.WebSocketUrl),
                    cancellationToken);

                await SendJsonAsync(
                    new
                    {
                        action = "auth",
                        @params = _options.ApiKey
                    },
                    cancellationToken);

                ConnectionState.Status =
                    MarketDataConnectionStatus.Connected;

                ConnectionState.Message =
                    "Connected to Massive. Waiting for authentication.";

                ConnectionState.ConnectedAtUtc =
                    DateTime.UtcNow;

                _receiveCts =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                _ = Task.Run(
                    () => ReceiveLoopAsync(_receiveCts.Token),
                    _receiveCts.Token);
            }
            catch (Exception ex)
            {
                ConnectionState.Status =
                    MarketDataConnectionStatus.Error;

                ConnectionState.Message =
                    $"Massive connection failed: {ex.Message}";
            }
        }

        public async Task DisconnectAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                _receiveCts?.Cancel();

                if (_socket is not null &&
                    _socket.State == WebSocketState.Open)
                {
                    await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "PFA disconnect",
                        cancellationToken);
                }
            }
            catch
            {
                // Ignore shutdown errors.
            }
            finally
            {
                _socket?.Dispose();
                _socket = null;

                ConnectionState.Status =
                    MarketDataConnectionStatus.Disconnected;

                ConnectionState.Message =
                    "Massive disconnected.";

                ConnectionState.Symbol = null;
                ConnectionState.Timeframe = null;
            }
        }

        public async Task SubscribeAsync(
            string symbol,
            string timeframe,
            CancellationToken cancellationToken = default)
        {
            if (_socket is null ||
                _socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException(
                    "Massive WebSocket is not connected.");
            }

            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException(
                    "A futures contract ticker is required.",
                    nameof(symbol));
            }

            string normalizedSymbol =
                symbol.Trim().ToUpperInvariant();

            string subscription =
                $"AM.{normalizedSymbol}";

            await SendJsonAsync(
                new
                {
                    action = "subscribe",
                    @params = subscription
                },
                cancellationToken);

            ConnectionState.Symbol =
                normalizedSymbol;

            ConnectionState.Timeframe =
                timeframe.Trim().ToLowerInvariant();

            ConnectionState.Message =
                $"Subscription requested for {subscription}.";
        }

        public async Task UnsubscribeAsync(
            string symbol,
            string timeframe,
            CancellationToken cancellationToken = default)
        {
            if (_socket is not null &&
                _socket.State == WebSocketState.Open &&
                !string.IsNullOrWhiteSpace(symbol))
            {
                string subscription =
                    $"AM.{symbol.Trim().ToUpperInvariant()}";

                await SendJsonAsync(
                    new
                    {
                        action = "unsubscribe",
                        @params = subscription
                    },
                    cancellationToken);
            }

            ConnectionState.Symbol = null;
            ConnectionState.Timeframe = null;

            ConnectionState.Message =
                "Massive subscription removed.";
        }

        private async Task ReceiveLoopAsync(
            CancellationToken cancellationToken)
        {
            if (_socket is null)
            {
                return;
            }

            byte[] buffer =
                new byte[64 * 1024];

            while (!cancellationToken.IsCancellationRequested &&
                   _socket.State == WebSocketState.Open)
            {
                try
                {
                    using var messageBuffer =
                        new MemoryStream();

                    WebSocketReceiveResult result;

                    do
                    {
                        result =
                            await _socket.ReceiveAsync(
                                new ArraySegment<byte>(buffer),
                                cancellationToken);

                        if (result.MessageType ==
                            WebSocketMessageType.Close)
                        {
                            await DisconnectAsync(
                                cancellationToken);

                            return;
                        }

                        messageBuffer.Write(
                            buffer,
                            0,
                            result.Count);
                    }
                    while (!result.EndOfMessage);

                    string json =
                        Encoding.UTF8.GetString(
                            messageBuffer.ToArray());

                    DateTime receivedUtc =
                        DateTime.UtcNow;

                    _lastRawMessage = json;
                    _lastRawMessageUtc = receivedUtc;

                    await SaveRawEventsAsync(
                        json,
                        receivedUtc,
                        cancellationToken);

                    await HandleIncomingMessageAsync(json);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ConnectionState.Status =
                        MarketDataConnectionStatus.Error;

                    ConnectionState.Message =
                        $"Massive receive error: {ex.Message}";

                    return;
                }
            }
        }

        private async Task SaveRawEventsAsync(
            string json,
            DateTime receivedUtc,
            CancellationToken cancellationToken)
        {
            using JsonDocument document =
                JsonDocument.Parse(json);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Array)
            {
                await _rawRepository.SaveAsync(
                    ProviderName,
                    null,
                    null,
                    null,
                    receivedUtc,
                    json,
                    cancellationToken);

                return;
            }

            foreach (JsonElement item
                     in document.RootElement.EnumerateArray())
            {
                string? eventType = null;
                string? symbol = null;
                DateTime? marketTimestampUtc = null;

                if (item.TryGetProperty(
                        "ev",
                        out JsonElement eventElement))
                {
                    eventType =
                        eventElement.GetString();
                }

                if (item.TryGetProperty(
                        "sym",
                        out JsonElement symbolElement))
                {
                    symbol =
                        symbolElement.GetString();
                }

                if (item.TryGetProperty(
                        "e",
                        out JsonElement endElement) &&
                    TryReadInt64(
                        endElement,
                        out long endTimestampMs))
                {
                    marketTimestampUtc =
                        DateTimeOffset
                            .FromUnixTimeMilliseconds(
                                endTimestampMs)
                            .UtcDateTime;
                }
                else if (item.TryGetProperty(
                             "s",
                             out JsonElement startElement) &&
                         TryReadInt64(
                             startElement,
                             out long startTimestampMs))
                {
                    marketTimestampUtc =
                        DateTimeOffset
                            .FromUnixTimeMilliseconds(
                                startTimestampMs)
                            .UtcDateTime;
                }

                await _rawRepository.SaveAsync(
                    ProviderName,
                    symbol,
                    eventType,
                    marketTimestampUtc,
                    receivedUtc,
                    item.GetRawText(),
                    cancellationToken);
            }
        }

        private async Task HandleIncomingMessageAsync(
            string json)
        {
            using JsonDocument document =
                JsonDocument.Parse(json);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement item
                     in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty(
                        "ev",
                        out JsonElement eventElement))
                {
                    continue;
                }

                string? eventType =
                    eventElement.GetString();

                if (string.Equals(
                        eventType,
                        "status",
                        StringComparison.OrdinalIgnoreCase))
                {
                    HandleStatusMessage(item);
                    continue;
                }

                if (!string.Equals(
                        eventType,
                        "AM",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                MassiveMinuteAggregate? aggregate =
                    item.Deserialize<MassiveMinuteAggregate>();

                if (aggregate is null)
                {
                    continue;
                }

                await HandleMinuteAggregateAsync(aggregate);
            }
        }

        private void HandleStatusMessage(
            JsonElement item)
        {
            string status =
                item.TryGetProperty(
                    "status",
                    out JsonElement statusElement)
                    ? statusElement.GetString()
                        ?? string.Empty
                    : string.Empty;

            string message =
                item.TryGetProperty(
                    "message",
                    out JsonElement messageElement)
                    ? messageElement.GetString()
                        ?? string.Empty
                    : string.Empty;

            if (status.Equals(
                    "auth_success",
                    StringComparison.OrdinalIgnoreCase))
            {
                ConnectionState.Status =
                    MarketDataConnectionStatus.Connected;

                ConnectionState.Message =
                    "Massive authentication successful.";

                return;
            }

            ConnectionState.Message =
                $"Massive status: {status} - {message}";
        }

        private async Task HandleMinuteAggregateAsync(
            MassiveMinuteAggregate aggregate)
        {
            DateTime openTimeUtc =
                DateTimeOffset
                    .FromUnixTimeMilliseconds(
                        aggregate.StartTimestampMs)
                    .UtcDateTime;

            Candle oneMinute =
                new()
                {
                    Symbol = aggregate.Symbol,
                    Timeframe = "1m",
                    OpenTimeUtc = openTimeUtc,
                    Open = aggregate.Open,
                    High = aggregate.High,
                    Low = aggregate.Low,
                    Close = aggregate.Close,
                    Volume = aggregate.Volume,
                    IsClosed = true
                };

            ConnectionState.LastCandleReceivedUtc =
                DateTime.UtcNow;

            Candle? fiveMinute =
                _aggregator.AddMinuteCandle(
                    oneMinute);

            if (fiveMinute is null)
            {
                return;
            }

            if (ClosedCandleReceived is null)
            {
                return;
            }

            foreach (Func<Candle, Task> handler
                     in ClosedCandleReceived.GetInvocationList())
            {
                await handler(fiveMinute);
            }
        }

        private async Task SendJsonAsync(
            object payload,
            CancellationToken cancellationToken)
        {
            if (_socket is null ||
                _socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException(
                    "Massive WebSocket is not connected.");
            }

            string json =
                JsonSerializer.Serialize(payload);

            byte[] bytes =
                Encoding.UTF8.GetBytes(json);

            await _socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }

        private static bool TryReadInt64(
            JsonElement element,
            out long value)
        {
            if (element.ValueKind ==
                JsonValueKind.Number)
            {
                return element.TryGetInt64(out value);
            }

            if (element.ValueKind ==
                JsonValueKind.String)
            {
                return long.TryParse(
                    element.GetString(),
                    out value);
            }

            value = 0;
            return false;
        }
    }
}