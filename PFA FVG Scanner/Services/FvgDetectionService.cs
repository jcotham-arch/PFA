using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class FvgDetectionService
    {
        private const decimal MinimumGapSize = 0.50m;

        public FairValueGap? Detect(
            Candle candle1,
            Candle candle2,
            Candle candle3)
        {
            if (candle1 is null ||
                candle2 is null ||
                candle3 is null)
            {
                return null;
            }

            if (!candle1.IsClosed ||
                !candle2.IsClosed ||
                !candle3.IsClosed)
            {
                return null;
            }

            if (!string.Equals(
                    candle1.Symbol,
                    candle2.Symbol,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    candle1.Symbol,
                    candle3.Symbol,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.Equals(
                    candle1.Timeframe,
                    candle2.Timeframe,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    candle1.Timeframe,
                    candle3.Timeframe,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // ------------------------------------------------------------
            // BULLISH FAIR VALUE GAP
            //
            // Candle 3 Low is strictly above Candle 1 High.
            //
            // Example:
            // Candle 1 High = 7683.50
            // Candle 3 Low  = 7685.00
            //
            // Gap = 1.50 points
            // ------------------------------------------------------------

            if (candle3.Low > candle1.High)
            {
                decimal gapSize = candle3.Low - candle1.High;

                if (gapSize >= MinimumGapSize)
                {
                    return new FairValueGap
                    {
                        Symbol = candle3.Symbol,
                        Timeframe = candle3.Timeframe,
                        Direction = FvgDirection.Bullish,
                        FormationTimeUtc = candle3.OpenTimeUtc,
                        LowerBoundary = candle1.High,
                        UpperBoundary = candle3.Low,
                        GapSize = gapSize,
                        CurrentPrice = candle3.Close,
                        FillPercentage = 0m,
                        Status = FvgStatus.New
                    };
                }
            }

            // ------------------------------------------------------------
            // BEARISH FAIR VALUE GAP
            //
            // Candle 3 High is strictly below Candle 1 Low.
            // ------------------------------------------------------------

            if (candle3.High < candle1.Low)
            {
                decimal gapSize = candle1.Low - candle3.High;

                if (gapSize >= MinimumGapSize)
                {
                    return new FairValueGap
                    {
                        Symbol = candle3.Symbol,
                        Timeframe = candle3.Timeframe,
                        Direction = FvgDirection.Bearish,
                        FormationTimeUtc = candle3.OpenTimeUtc,
                        LowerBoundary = candle3.High,
                        UpperBoundary = candle1.Low,
                        GapSize = gapSize,
                        CurrentPrice = candle3.Close,
                        FillPercentage = 0m,
                        Status = FvgStatus.New
                    };
                }
            }

            return null;
        }
    }
}