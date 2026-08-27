using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class CandleProcessingService
    {
        private readonly FvgDetectionService _detector;
        private readonly FvgTrackingService _tracker;

        private readonly Dictionary<string, List<Candle>> _windows = new();

        private readonly object _syncRoot = new();

        public CandleProcessingService(
            FvgDetectionService detector,
            FvgTrackingService tracker)
        {
            _detector = detector;
            _tracker = tracker;
        }

        public CandleProcessingResult ProcessClosedCandle(Candle candle)
        {
            if (candle is null)
            {
                return new CandleProcessingResult
                {
                    Accepted = false,
                    Message = "Candle was null."
                };
            }

            if (!candle.IsClosed)
            {
                return new CandleProcessingResult
                {
                    Accepted = false,
                    Message = "Only closed candles are processed.",
                    Candle = candle
                };
            }

            if (string.IsNullOrWhiteSpace(candle.Symbol))
            {
                return new CandleProcessingResult
                {
                    Accepted = false,
                    Message = "Symbol is required.",
                    Candle = candle
                };
            }

            if (string.IsNullOrWhiteSpace(candle.Timeframe))
            {
                return new CandleProcessingResult
                {
                    Accepted = false,
                    Message = "Timeframe is required.",
                    Candle = candle
                };
            }

            string key = BuildKey(
                candle.Symbol,
                candle.Timeframe);

            lock (_syncRoot)
            {
                if (!_windows.TryGetValue(key, out var window))
                {
                    window = new List<Candle>();
                    _windows[key] = window;
                }

                bool duplicate = window.Any(x =>
                    x.OpenTimeUtc == candle.OpenTimeUtc);

                if (duplicate)
                {
                    return new CandleProcessingResult
                    {
                        Accepted = false,
                        Message = "Duplicate candle ignored.",
                        CandlesInWindow = window.Count,
                        Candle = candle
                    };
                }

                window.Add(candle);

                window.Sort((a, b) =>
                    a.OpenTimeUtc.CompareTo(b.OpenTimeUtc));

                while (window.Count > 3)
                {
                    window.RemoveAt(0);
                }

                if (window.Count < 3)
                {
                    return new CandleProcessingResult
                    {
                        Accepted = true,
                        Message =
                            $"Candle accepted. Waiting for {3 - window.Count} more candle(s).",
                        CandlesInWindow = window.Count,
                        Candle = candle
                    };
                }

                Candle candle1 = window[0];
                Candle candle2 = window[1];
                Candle candle3 = window[2];

                FairValueGap? detected =
                    _detector.Detect(
                        candle1,
                        candle2,
                        candle3);

                if (detected is null)
                {
                    return new CandleProcessingResult
                    {
                        Accepted = true,
                        Message =
                            "Candle accepted. Three-candle window contains no qualifying FVG.",
                        CandlesInWindow = window.Count,
                        Candle = candle
                    };
                }

                FairValueGap tracked =
                    _tracker.Add(detected);

                return new CandleProcessingResult
                {
                    Accepted = true,
                    Message =
                        $"{tracked.Direction} FVG detected and added to tracker.",
                    CandlesInWindow = window.Count,
                    Candle = candle,
                    DetectedFvg = tracked
                };
            }
        }

        public IReadOnlyList<Candle> GetCurrentWindow(
            string symbol,
            string timeframe)
        {
            string key = BuildKey(symbol, timeframe);

            lock (_syncRoot)
            {
                if (!_windows.TryGetValue(key, out var window))
                {
                    return Array.Empty<Candle>();
                }

                return window
                    .OrderBy(x => x.OpenTimeUtc)
                    .ToList();
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _windows.Clear();
            }
        }

        private static string BuildKey(
            string symbol,
            string timeframe)
        {
            return
                $"{symbol.Trim().ToUpperInvariant()}|" +
                $"{timeframe.Trim().ToLowerInvariant()}";
        }
    }
}