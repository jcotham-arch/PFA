using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class FiveMinuteCandleAggregator
    {
        private readonly object _syncRoot = new();

        private readonly Dictionary<string, List<Candle>>
            _minuteBuckets = new();

        public Candle? AddMinuteCandle(Candle minuteCandle)
        {
            if (minuteCandle is null)
            {
                return null;
            }

            if (!minuteCandle.IsClosed)
            {
                return null;
            }

            if (!string.Equals(
                    minuteCandle.Timeframe,
                    "1m",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            DateTime bucketStart =
                AlignToFiveMinuteBucket(
                    minuteCandle.OpenTimeUtc);

            string key =
                $"{minuteCandle.Symbol.ToUpperInvariant()}|" +
                $"{bucketStart:O}";

            lock (_syncRoot)
            {
                if (!_minuteBuckets.TryGetValue(
                        key,
                        out var bucket))
                {
                    bucket = new List<Candle>();
                    _minuteBuckets[key] = bucket;
                }

                bool duplicate = bucket.Any(x =>
                    x.OpenTimeUtc ==
                    minuteCandle.OpenTimeUtc);

                if (duplicate)
                {
                    return null;
                }

                bucket.Add(minuteCandle);

                bucket = bucket
                    .OrderBy(x => x.OpenTimeUtc)
                    .ToList();

                _minuteBuckets[key] = bucket;

                if (bucket.Count < 5)
                {
                    return null;
                }

                Candle fiveMinuteCandle =
                    new()
                    {
                        Symbol =
                            minuteCandle.Symbol,

                        Timeframe = "5m",

                        OpenTimeUtc =
                            bucketStart,

                        Open =
                            bucket.First().Open,

                        High =
                            bucket.Max(x => x.High),

                        Low =
                            bucket.Min(x => x.Low),

                        Close =
                            bucket.Last().Close,

                        Volume =
                            bucket.Sum(x => x.Volume),

                        IsClosed = true
                    };

                _minuteBuckets.Remove(key);

                return fiveMinuteCandle;
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _minuteBuckets.Clear();
            }
        }

        private static DateTime AlignToFiveMinuteBucket(
            DateTime timestampUtc)
        {
            timestampUtc =
                timestampUtc.Kind == DateTimeKind.Utc
                    ? timestampUtc
                    : timestampUtc.ToUniversalTime();

            int alignedMinute =
                timestampUtc.Minute -
                (timestampUtc.Minute % 5);

            return new DateTime(
                timestampUtc.Year,
                timestampUtc.Month,
                timestampUtc.Day,
                timestampUtc.Hour,
                alignedMinute,
                0,
                DateTimeKind.Utc);
        }
    }
}