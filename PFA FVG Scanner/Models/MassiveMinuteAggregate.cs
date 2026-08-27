using System.Text.Json.Serialization;

namespace PFA_FVG_Scanner.Models
{
    public sealed class MassiveMinuteAggregate
    {
        [JsonPropertyName("ev")]
        public string EventType { get; set; } = string.Empty;

        [JsonPropertyName("sym")]
        public string Symbol { get; set; } = string.Empty;

        [JsonPropertyName("v")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal Volume { get; set; }

        [JsonPropertyName("o")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal Open { get; set; }

        [JsonPropertyName("c")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal Close { get; set; }

        [JsonPropertyName("h")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal High { get; set; }

        [JsonPropertyName("l")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal Low { get; set; }

        [JsonPropertyName("n")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long TransactionCount { get; set; }

        [JsonPropertyName("s")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long StartTimestampMs { get; set; }

        [JsonPropertyName("e")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long EndTimestampMs { get; set; }
    }
}