using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class HistoricalFvgReplayService
    {
        private const string EngineVersion =
            "1.1.0";

        public FvgOutcome Evaluate(
            FairValueGap fvg,
            IReadOnlyList<Candle> futureCandles)
        {
            if (fvg is null)
            {
                throw new ArgumentNullException(
                    nameof(fvg));
            }

            futureCandles ??=
                Array.Empty<Candle>();

            DateTime confirmationTimeUtc =
                GetConfirmationTimeUtc(
                    fvg);

            // ========================================================
            // FILTER POST-CONFIRMATION 1-MINUTE CANDLES
            //
            // IMPORTANT:
            //
            // For a 5-minute FVG whose third candle opens at 14:15,
            // the setup is not actually confirmed until 14:20.
            //
            // We never use 14:15-14:19 as "future" information.
            // ========================================================

            List<Candle> candles =
                futureCandles
                    .Where(c =>
                        c.IsClosed &&
                        c.Symbol.Equals(
                            fvg.Symbol,
                            StringComparison.OrdinalIgnoreCase) &&
                        c.OpenTimeUtc >=
                            confirmationTimeUtc)
                    .OrderBy(c =>
                        c.OpenTimeUtc)
                    .ToList();

            var outcome =
                new FvgOutcome
                {
                    FvgId =
                        fvg.Id,

                    Symbol =
                        fvg.Symbol,

                    Timeframe =
                        fvg.Timeframe,

                    Direction =
                        fvg.Direction,

                    FormationTimeUtc =
                        EnsureUtc(
                            fvg.FormationTimeUtc),

                    ConfirmationTimeUtc =
                        confirmationTimeUtc,

                    LowerBoundary =
                        fvg.LowerBoundary,

                    UpperBoundary =
                        fvg.UpperBoundary,

                    Midpoint =
                        fvg.Midpoint,

                    GapSize =
                        fvg.GapSize,

                    LifecycleStatus =
                        FvgLifecycleStatus.Formed,

                    WasBoundaryEntryOffered =
                        false,

                    WasFullyFilled =
                        false,

                    EvaluatedThroughUtc =
                        candles.Count > 0
                            ? candles[^1].OpenTimeUtc
                            : confirmationTimeUtc,

                    MinuteCandlesEvaluated =
                        candles.Count,

                    EngineVersion =
                        EngineVersion
                };

            if (candles.Count == 0)
            {
                outcome.LifecycleStatus =
                    FvgLifecycleStatus.NoRetracement;

                outcome.SetupLifetimeMinutes =
                    0;

                return outcome;
            }

            // ========================================================
            // FVG LEVELS
            // ========================================================

            decimal boundaryTouchLevel =
                fvg.Direction ==
                FvgDirection.Bullish
                    ? fvg.UpperBoundary
                    : fvg.LowerBoundary;

            decimal twentyFiveLevel =
                CalculateFillLevel(
                    fvg,
                    0.25m);

            decimal fiftyLevel =
                fvg.Midpoint;

            decimal seventyFiveLevel =
                CalculateFillLevel(
                    fvg,
                    0.75m);

            decimal fullFillLevel =
                fvg.Direction ==
                FvgDirection.Bullish
                    ? fvg.LowerBoundary
                    : fvg.UpperBoundary;

            // ========================================================
            // MARKET BEHAVIOR
            //
            // These are deliberately NOT trade results.
            //
            // They describe what price did after confirmation,
            // regardless of whether an entry ever became available.
            // ========================================================

            decimal highest =
                candles.Max(
                    c => c.High);

            decimal lowest =
                candles.Min(
                    c => c.Low);

            outcome.HighestPriceAfterSetup =
                highest;

            outcome.LowestPriceAfterSetup =
                lowest;

            if (fvg.Direction ==
                FvgDirection.Bullish)
            {
                outcome.MaximumFavorableExcursion =
                    Math.Max(
                        0m,
                        highest -
                        boundaryTouchLevel);

                outcome.MaximumAdverseExcursion =
                    Math.Max(
                        0m,
                        boundaryTouchLevel -
                        lowest);
            }
            else
            {
                outcome.MaximumFavorableExcursion =
                    Math.Max(
                        0m,
                        boundaryTouchLevel -
                        lowest);

                outcome.MaximumAdverseExcursion =
                    Math.Max(
                        0m,
                        highest -
                        boundaryTouchLevel);
            }

            // ========================================================
            // WALK FORWARD THROUGH EACH 1-MINUTE CANDLE
            // ========================================================

            foreach (Candle candle in candles)
            {
                // ----------------------------------------------------
                // BOUNDARY TOUCH
                // ----------------------------------------------------

                if (!outcome.FirstTouchTimeUtc.HasValue &&
                    TouchesLevel(
                        candle,
                        boundaryTouchLevel))
                {
                    outcome.FirstTouchTimeUtc =
                        candle.OpenTimeUtc;

                    outcome.FirstTouchPrice =
                        boundaryTouchLevel;

                    outcome.MinutesToFirstTouch =
                        CalculateElapsedMinutes(
                            confirmationTimeUtc,
                            candle.OpenTimeUtc);

                    outcome.WasBoundaryEntryOffered =
                        true;

                    outcome.LifecycleStatus =
                        FvgLifecycleStatus.BoundaryTouched;
                }

                // ----------------------------------------------------
                // 25% MITIGATION
                // ----------------------------------------------------

                if (!outcome
                        .TwentyFivePercentFillTimeUtc
                        .HasValue &&
                    ReachesFillLevel(
                        fvg,
                        candle,
                        twentyFiveLevel))
                {
                    outcome.TwentyFivePercentFillTimeUtc =
                        candle.OpenTimeUtc;

                    outcome.MinutesToTwentyFivePercentFill =
                        CalculateElapsedMinutes(
                            confirmationTimeUtc,
                            candle.OpenTimeUtc);

                    outcome.LifecycleStatus =
                        FvgLifecycleStatus
                            .TwentyFivePercentFilled;
                }

                // ----------------------------------------------------
                // 50% MITIGATION
                // ----------------------------------------------------

                if (!outcome
                        .FiftyPercentFillTimeUtc
                        .HasValue &&
                    ReachesFillLevel(
                        fvg,
                        candle,
                        fiftyLevel))
                {
                    outcome.FiftyPercentFillTimeUtc =
                        candle.OpenTimeUtc;

                    outcome.MinutesToFiftyPercentFill =
                        CalculateElapsedMinutes(
                            confirmationTimeUtc,
                            candle.OpenTimeUtc);

                    outcome.LifecycleStatus =
                        FvgLifecycleStatus
                            .FiftyPercentFilled;
                }

                // ----------------------------------------------------
                // 75% MITIGATION
                // ----------------------------------------------------

                if (!outcome
                        .SeventyFivePercentFillTimeUtc
                        .HasValue &&
                    ReachesFillLevel(
                        fvg,
                        candle,
                        seventyFiveLevel))
                {
                    outcome.SeventyFivePercentFillTimeUtc =
                        candle.OpenTimeUtc;

                    outcome.MinutesToSeventyFivePercentFill =
                        CalculateElapsedMinutes(
                            confirmationTimeUtc,
                            candle.OpenTimeUtc);

                    outcome.LifecycleStatus =
                        FvgLifecycleStatus
                            .SeventyFivePercentFilled;
                }

                // ----------------------------------------------------
                // FULL MITIGATION
                // ----------------------------------------------------

                if (!outcome
                        .FullFillTimeUtc
                        .HasValue &&
                    ReachesFillLevel(
                        fvg,
                        candle,
                        fullFillLevel))
                {
                    outcome.FullFillTimeUtc =
                        candle.OpenTimeUtc;

                    outcome.MinutesToFullFill =
                        CalculateElapsedMinutes(
                            confirmationTimeUtc,
                            candle.OpenTimeUtc);

                    outcome.WasFullyFilled =
                        true;

                    outcome.LifecycleStatus =
                        FvgLifecycleStatus.FullyFilled;
                }
            }

            // ========================================================
            // NO RETRACEMENT CLASSIFICATION
            // ========================================================

            if (!outcome.FirstTouchTimeUtc.HasValue)
            {
                outcome.LifecycleStatus =
                    FvgLifecycleStatus.NoRetracement;
            }

            // ========================================================
            // DIRECTIONAL MARKET RETURNS
            //
            // These are still observational—not realized P&L.
            // ========================================================

            outcome.Return5Minutes =
                CalculateDirectionalReturn(
                    fvg,
                    candles,
                    confirmationTimeUtc,
                    5);

            outcome.Return15Minutes =
                CalculateDirectionalReturn(
                    fvg,
                    candles,
                    confirmationTimeUtc,
                    15);

            outcome.Return30Minutes =
                CalculateDirectionalReturn(
                    fvg,
                    candles,
                    confirmationTimeUtc,
                    30);

            outcome.Return60Minutes =
                CalculateDirectionalReturn(
                    fvg,
                    candles,
                    confirmationTimeUtc,
                    60);

            // ========================================================
            // LIFETIME
            //
            // If fully filled:
            // confirmation -> full fill
            //
            // Otherwise:
            // confirmation -> end of available replay data
            // ========================================================

            DateTime lifetimeEndUtc =
                outcome.FullFillTimeUtc ??
                outcome.EvaluatedThroughUtc;

            outcome.SetupLifetimeMinutes =
                CalculateElapsedMinutes(
                    confirmationTimeUtc,
                    lifetimeEndUtc);

            return outcome;
        }

        // ============================================================
        // CONFIRMATION TIME
        // ============================================================

        private static DateTime GetConfirmationTimeUtc(
            FairValueGap fvg)
        {
            int timeframeMinutes =
                ParseTimeframeMinutes(
                    fvg.Timeframe);

            return EnsureUtc(
                    fvg.FormationTimeUtc)
                .AddMinutes(
                    timeframeMinutes);
        }

        // ============================================================
        // TIMEFRAME PARSER
        // ============================================================

        private static int ParseTimeframeMinutes(
            string timeframe)
        {
            if (string.IsNullOrWhiteSpace(
                    timeframe))
            {
                return 5;
            }

            string normalized =
                timeframe
                    .Trim()
                    .ToLowerInvariant();

            if (normalized.EndsWith("m") &&
                int.TryParse(
                    normalized[..^1],
                    out int minutes) &&
                minutes > 0)
            {
                return minutes;
            }

            return 5;
        }

        // ============================================================
        // FVG DEPTH LEVEL
        // ============================================================

        private static decimal CalculateFillLevel(
            FairValueGap fvg,
            decimal percentage)
        {
            decimal range =
                fvg.UpperBoundary -
                fvg.LowerBoundary;

            if (fvg.Direction ==
                FvgDirection.Bullish)
            {
                return
                    fvg.UpperBoundary -
                    (range * percentage);
            }

            return
                fvg.LowerBoundary +
                (range * percentage);
        }

        // ============================================================
        // TOUCH TEST
        // ============================================================

        private static bool TouchesLevel(
            Candle candle,
            decimal level)
        {
            return
                candle.Low <= level &&
                candle.High >= level;
        }

        // ============================================================
        // MITIGATION TEST
        // ============================================================

        private static bool ReachesFillLevel(
            FairValueGap fvg,
            Candle candle,
            decimal level)
        {
            if (fvg.Direction ==
                FvgDirection.Bullish)
            {
                return
                    candle.Low <= level;
            }

            return
                candle.High >= level;
        }

        // ============================================================
        // OBSERVATIONAL RETURN
        // ============================================================

        private static decimal?
            CalculateDirectionalReturn(
                FairValueGap fvg,
                IReadOnlyList<Candle> candles,
                DateTime confirmationTimeUtc,
                int minutesAfterConfirmation)
        {
            DateTime targetTime =
                confirmationTimeUtc
                    .AddMinutes(
                        minutesAfterConfirmation);

            Candle? target =
                candles.FirstOrDefault(
                    candle =>
                        candle.OpenTimeUtc ==
                        targetTime);

            // Exact minute only.
            //
            // If that minute does not exist because the market was
            // closed or data was unavailable, the result is null.
            // We do not silently substitute a later candle.

            if (target is null)
            {
                return null;
            }

            decimal reference =
                fvg.Direction ==
                FvgDirection.Bullish
                    ? fvg.UpperBoundary
                    : fvg.LowerBoundary;

            if (fvg.Direction ==
                FvgDirection.Bullish)
            {
                return
                    target.Close -
                    reference;
            }

            return
                reference -
                target.Close;
        }

        // ============================================================
        // ELAPSED MINUTES
        // ============================================================

        private static int CalculateElapsedMinutes(
            DateTime startUtc,
            DateTime endUtc)
        {
            double minutes =
                (
                    EnsureUtc(endUtc) -
                    EnsureUtc(startUtc)
                ).TotalMinutes;

            return Math.Max(
                0,
                (int)Math.Round(
                    minutes));
        }

        // ============================================================
        // UTC NORMALIZATION
        // ============================================================

        private static DateTime EnsureUtc(
            DateTime value)
        {
            if (value.Kind ==
                DateTimeKind.Utc)
            {
                return value;
            }

            if (value.Kind ==
                DateTimeKind.Unspecified)
            {
                return DateTime.SpecifyKind(
                    value,
                    DateTimeKind.Utc);
            }

            return value.ToUniversalTime();
        }
    }
}