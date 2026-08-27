using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class MesExecutionNormalizationService
    {
        public const decimal TickSize =
            0.25m;

        // ============================================================
        // ENTRY NORMALIZATION
        //
        // We use conservative directional rounding.
        //
        // Bullish FVG:
        // Buy limit on retracement.
        // Round DOWN so we do not assume a fill before price actually
        // reaches the theoretical depth.
        //
        // Bearish FVG:
        // Sell limit on retracement.
        // Round UP for the same reason.
        // ============================================================

        public decimal NormalizeEntry(
            FvgDirection direction,
            decimal theoreticalPrice)
        {
            return direction ==
                FvgDirection.Bullish
                    ? FloorToTick(theoreticalPrice)
                    : CeilingToTick(theoreticalPrice);
        }

        // ============================================================
        // STOP NORMALIZATION
        //
        // Keep the stop at least as far from entry as the theoretical
        // stop.
        //
        // Bullish:
        // stop is below entry -> round DOWN.
        //
        // Bearish:
        // stop is above entry -> round UP.
        // ============================================================

        public decimal NormalizeStop(
            FvgDirection direction,
            decimal theoreticalPrice)
        {
            return direction ==
                FvgDirection.Bullish
                    ? FloorToTick(theoreticalPrice)
                    : CeilingToTick(theoreticalPrice);
        }

        // ============================================================
        // TARGET NORMALIZATION
        //
        // Preserve AT LEAST the requested R multiple.
        //
        // Bullish:
        // target is above entry -> round UP.
        //
        // Bearish:
        // target is below entry -> round DOWN.
        //
        // This deliberately avoids giving the backtest an easier
        // target just because the theoretical price fell between
        // valid MES ticks.
        // ============================================================

        public decimal NormalizeTarget(
            FvgDirection direction,
            decimal theoreticalPrice)
        {
            return direction ==
                FvgDirection.Bullish
                    ? CeilingToTick(theoreticalPrice)
                    : FloorToTick(theoreticalPrice);
        }

        // ============================================================
        // VALIDATION
        // ============================================================

        public bool IsValidMesPrice(
            decimal price)
        {
            decimal ticks =
                price / TickSize;

            return ticks ==
                   decimal.Truncate(ticks);
        }

        // ============================================================
        // ROUND DOWN
        // ============================================================

        private static decimal FloorToTick(
            decimal price)
        {
            return
                Math.Floor(
                    price / TickSize)
                * TickSize;
        }

        // ============================================================
        // ROUND UP
        // ============================================================

        private static decimal CeilingToTick(
            decimal price)
        {
            return
                Math.Ceiling(
                    price / TickSize)
                * TickSize;
        }
    }
}