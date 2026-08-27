using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class FvgQualificationService
    {
        private const decimal DefaultStopBufferPoints = 0.25m;

        public FvgTradeQualification CreateQualification(
            FairValueGap fvg,
            string source = "Live",
            DateTime? historicalRecoveryTimeUtc = null)
        {
            if (fvg is null)
            {
                throw new ArgumentNullException(nameof(fvg));
            }

            bool bearish =
                fvg.Direction.ToString().Equals(
                    "Bearish",
                    StringComparison.OrdinalIgnoreCase);

            decimal boundaryEntry;
            decimal twentyFiveEntry;
            decimal fiftyEntry;
            decimal seventyFiveEntry;

            if (bearish)
            {
                boundaryEntry =
                    fvg.LowerBoundary;

                twentyFiveEntry =
                    fvg.LowerBoundary +
                    (fvg.GapSize * 0.25m);

                fiftyEntry =
                    fvg.Midpoint;

                seventyFiveEntry =
                    fvg.LowerBoundary +
                    (fvg.GapSize * 0.75m);
            }
            else
            {
                boundaryEntry =
                    fvg.UpperBoundary;

                twentyFiveEntry =
                    fvg.UpperBoundary -
                    (fvg.GapSize * 0.25m);

                fiftyEntry =
                    fvg.Midpoint;

                seventyFiveEntry =
                    fvg.UpperBoundary -
                    (fvg.GapSize * 0.75m);
            }

            return new FvgTradeQualification
            {
                FvgId =
                    fvg.Id.ToString(),

                Symbol =
                    fvg.Symbol,

                Timeframe =
                    fvg.Timeframe,

                Direction =
                    fvg.Direction.ToString(),

                FormationTimeUtc =
                    fvg.FormationTimeUtc,

                LowerBoundary =
                    fvg.LowerBoundary,

                UpperBoundary =
                    fvg.UpperBoundary,

                Midpoint =
                    fvg.Midpoint,

                GapSize =
                    fvg.GapSize,

                BoundaryTouch =
                    CreateEntryModel(
                        fvg,
                        "BoundaryTouch",
                        boundaryEntry),

                TwentyFivePercent =
                    CreateEntryModel(
                        fvg,
                        "TwentyFivePercent",
                        twentyFiveEntry),

                FiftyPercent =
                    CreateEntryModel(
                        fvg,
                        "FiftyPercent",
                        fiftyEntry),

                SeventyFivePercent =
                    CreateEntryModel(
                        fvg,
                        "SeventyFivePercent",
                        seventyFiveEntry),

                Source =
                    source,

                HistoricalRecoveryTimeUtc =
                    historicalRecoveryTimeUtc,

                QualificationStatus =
                    "WaitingForEntry",

                LastEvaluatedUtc =
                    DateTime.UtcNow
            };
        }

        public void EvaluateMinuteCandle(
            FvgTradeQualification qualification,
            Candle candle)
        {
            if (qualification is null)
            {
                throw new ArgumentNullException(
                    nameof(qualification));
            }

            if (candle is null)
            {
                throw new ArgumentNullException(
                    nameof(candle));
            }

            if (!string.Equals(
                    qualification.Symbol,
                    candle.Symbol,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!string.Equals(
                    candle.Timeframe,
                    "1m",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DateTime confirmationTimeUtc =
                qualification.FormationTimeUtc
                    .AddMinutes(5);

            if (candle.OpenTimeUtc <
                confirmationTimeUtc)
            {
                return;
            }

            qualification.LastEvaluatedUtc =
                DateTime.UtcNow;

            UpdateGeneralExtremes(
                qualification,
                candle);

            EvaluateEntry(
                qualification,
                qualification.BoundaryTouch,
                candle);

            EvaluateEntry(
                qualification,
                qualification.TwentyFivePercent,
                candle);

            EvaluateEntry(
                qualification,
                qualification.FiftyPercent,
                candle);

            EvaluateEntry(
                qualification,
                qualification.SeventyFivePercent,
                candle);

            qualification.AnyEntryTriggered =
                qualification.BoundaryTouch.Triggered ||
                qualification.TwentyFivePercent.Triggered ||
                qualification.FiftyPercent.Triggered ||
                qualification.SeventyFivePercent.Triggered;

            if (qualification.AnyEntryTriggered)
            {
                EntryQualification? first =
                    GetAllEntries(qualification)
                        .Where(
                            x =>
                                x.Triggered &&
                                x.TriggeredAtUtc.HasValue)
                        .OrderBy(
                            x =>
                                x.TriggeredAtUtc)
                        .FirstOrDefault();

                if (first is not null)
                {
                    qualification.FirstEntryTriggeredUtc =
                        first.TriggeredAtUtc;

                    qualification.FirstEntryPrice =
                        first.ActualFillPrice ??
                        first.EntryPrice;
                }
            }

            qualification.IsTradable =
                qualification.AnyEntryTriggered;

            qualification.QualificationStatus =
                DetermineOverallStatus(
                    qualification);

            UpdateAggregateExcursion(
                qualification);
        }

        private static EntryQualification CreateEntryModel(
            FairValueGap fvg,
            string model,
            decimal entryPrice)
        {
            bool bearish =
                fvg.Direction.ToString().Equals(
                    "Bearish",
                    StringComparison.OrdinalIgnoreCase);

            decimal stopPrice =
                bearish
                    ? fvg.UpperBoundary +
                      DefaultStopBufferPoints
                    : fvg.LowerBoundary -
                      DefaultStopBufferPoints;

            return new EntryQualification
            {
                EntryModel =
                    model,

                EntryPrice =
                    entryPrice,

                StopPrice =
                    stopPrice,

                RiskPoints =
                    Math.Abs(
                        stopPrice -
                        entryPrice),

                Status =
                    "Waiting"
            };
        }

        private static void EvaluateEntry(
            FvgTradeQualification qualification,
            EntryQualification entry,
            Candle candle)
        {
            bool bearish =
                qualification.Direction.Equals(
                    "Bearish",
                    StringComparison.OrdinalIgnoreCase);

            // --------------------------------------------------------
            // WAITING FOR ENTRY
            // --------------------------------------------------------

            if (!entry.Triggered)
            {
                bool entryTouched =
                    candle.Low <= entry.EntryPrice &&
                    candle.High >= entry.EntryPrice;

                if (!entryTouched)
                {
                    return;
                }

                entry.Triggered =
                    true;

                entry.TriggeredAtUtc =
                    candle.OpenTimeUtc;

                entry.ActualFillPrice =
                    entry.EntryPrice;

                entry.Status =
                    "Triggered";

                // We deliberately do NOT calculate profit or loss
                // from the entry-trigger minute because OHLC alone
                // cannot prove which intrabar extreme occurred first.

                if (entry.StopPrice.HasValue)
                {
                    bool stopAlsoTouched =
                        bearish
                            ? candle.High >= entry.StopPrice.Value
                            : candle.Low <= entry.StopPrice.Value;

                    if (stopAlsoTouched)
                    {
                        entry.StopHit =
                            true;

                        entry.StopHitTimeUtc =
                            candle.OpenTimeUtc;

                        entry.OneROutcome =
                            "AmbiguousIntrabar";

                        entry.OnePointFiveROutcome =
                            "AmbiguousIntrabar";

                        entry.TwoROutcome =
                            "AmbiguousIntrabar";

                        entry.ThreeROutcome =
                            "AmbiguousIntrabar";

                        entry.Status =
                            "AmbiguousIntrabar";
                    }
                }

                return;
            }

            // --------------------------------------------------------
            // NOTHING MORE CAN BE PROVEN
            // --------------------------------------------------------

            if (entry.Status ==
                "AmbiguousIntrabar")
            {
                return;
            }

            if (AllOutcomesResolved(entry))
            {
                return;
            }

            decimal fillPrice =
                entry.ActualFillPrice ??
                entry.EntryPrice;

            decimal favorable;
            decimal adverse;

            if (bearish)
            {
                favorable =
                    Math.Max(
                        0m,
                        fillPrice -
                        candle.Low);

                adverse =
                    Math.Max(
                        0m,
                        candle.High -
                        fillPrice);
            }
            else
            {
                favorable =
                    Math.Max(
                        0m,
                        candle.High -
                        fillPrice);

                adverse =
                    Math.Max(
                        0m,
                        fillPrice -
                        candle.Low);
            }

            entry.MaximumFavorableExcursion =
                Math.Max(
                    entry.MaximumFavorableExcursion,
                    favorable);

            entry.MaximumAdverseExcursion =
                Math.Max(
                    entry.MaximumAdverseExcursion,
                    adverse);

            if (entry.RiskPoints > 0)
            {
                entry.MaximumFavorableR =
                    entry.MaximumFavorableExcursion /
                    entry.RiskPoints;

                entry.MaximumAdverseR =
                    entry.MaximumAdverseExcursion /
                    entry.RiskPoints;
            }

            bool stopTouched =
                entry.StopPrice.HasValue &&
                (
                    bearish
                        ? candle.High >= entry.StopPrice.Value
                        : candle.Low <= entry.StopPrice.Value
                );

            bool target1R =
                ReachedTarget(
                    entry,
                    candle,
                    bearish,
                    1m);

            bool target1Point5R =
                ReachedTarget(
                    entry,
                    candle,
                    bearish,
                    1.5m);

            bool target2R =
                ReachedTarget(
                    entry,
                    candle,
                    bearish,
                    2m);

            bool target3R =
                ReachedTarget(
                    entry,
                    candle,
                    bearish,
                    3m);

            // --------------------------------------------------------
            // 1R
            // --------------------------------------------------------

            entry.OneROutcome =
                ResolveOutcome(
                    entry.OneROutcome,
                    target1R,
                    stopTouched);

            if (entry.OneROutcome ==
                "TargetBeforeStop")
            {
                entry.Hit1R =
                    true;

                entry.Hit1RTimeUtc ??=
                    candle.OpenTimeUtc;
            }

            // --------------------------------------------------------
            // 1.5R
            // --------------------------------------------------------

            entry.OnePointFiveROutcome =
                ResolveOutcome(
                    entry.OnePointFiveROutcome,
                    target1Point5R,
                    stopTouched);

            if (entry.OnePointFiveROutcome ==
                "TargetBeforeStop")
            {
                entry.Hit1Point5R =
                    true;

                entry.Hit1Point5RTimeUtc ??=
                    candle.OpenTimeUtc;
            }

            // --------------------------------------------------------
            // 2R
            // --------------------------------------------------------

            entry.TwoROutcome =
                ResolveOutcome(
                    entry.TwoROutcome,
                    target2R,
                    stopTouched);

            if (entry.TwoROutcome ==
                "TargetBeforeStop")
            {
                entry.Hit2R =
                    true;

                entry.Hit2RTimeUtc ??=
                    candle.OpenTimeUtc;
            }

            // --------------------------------------------------------
            // 3R
            // --------------------------------------------------------

            entry.ThreeROutcome =
                ResolveOutcome(
                    entry.ThreeROutcome,
                    target3R,
                    stopTouched);

            if (entry.ThreeROutcome ==
                "TargetBeforeStop")
            {
                entry.Hit3R =
                    true;

                entry.Hit3RTimeUtc ??=
                    candle.OpenTimeUtc;
            }

            // --------------------------------------------------------
            // STOP
            // --------------------------------------------------------

            if (stopTouched)
            {
                entry.StopHit =
                    true;

                entry.StopHitTimeUtc ??=
                    candle.OpenTimeUtc;
            }

            UpdateEntrySummary(
                entry);
        }

        private static string ResolveOutcome(
            string currentOutcome,
            bool targetTouched,
            bool stopTouched)
        {
            if (currentOutcome !=
                "NotResolved")
            {
                return currentOutcome;
            }

            if (targetTouched &&
                stopTouched)
            {
                return "AmbiguousIntrabar";
            }

            if (targetTouched)
            {
                return "TargetBeforeStop";
            }

            if (stopTouched)
            {
                return "StopBeforeTarget";
            }

            return "NotResolved";
        }

        private static bool ReachedTarget(
            EntryQualification entry,
            Candle candle,
            bool bearish,
            decimal rMultiple)
        {
            if (entry.RiskPoints <= 0)
            {
                return false;
            }

            decimal distance =
                entry.RiskPoints *
                rMultiple;

            decimal targetPrice =
                bearish
                    ? entry.EntryPrice -
                      distance
                    : entry.EntryPrice +
                      distance;

            return bearish
                ? candle.Low <= targetPrice
                : candle.High >= targetPrice;
        }

        private static void UpdateEntrySummary(
            EntryQualification entry)
        {
            decimal highestR =
                0m;

            if (entry.OneROutcome ==
                "TargetBeforeStop")
            {
                highestR =
                    1m;
            }

            if (entry.OnePointFiveROutcome ==
                "TargetBeforeStop")
            {
                highestR =
                    1.5m;
            }

            if (entry.TwoROutcome ==
                "TargetBeforeStop")
            {
                highestR =
                    2m;
            }

            if (entry.ThreeROutcome ==
                "TargetBeforeStop")
            {
                highestR =
                    3m;
            }

            entry.HighestConfirmedProfitableR =
                highestR;

            entry.AnyProfitableExitModel =
                highestR > 0;

            entry.WasProfitable =
                entry.AnyProfitableExitModel;

            if (HasAmbiguousOutcome(entry))
            {
                entry.Status =
                    "ContainsAmbiguousOutcome";
            }
            else if (highestR >= 3m)
            {
                entry.Status =
                    "Confirmed3R";
            }
            else if (highestR >= 2m)
            {
                entry.Status =
                    "Confirmed2R";
            }
            else if (highestR >= 1.5m)
            {
                entry.Status =
                    "Confirmed1.5R";
            }
            else if (highestR >= 1m)
            {
                entry.Status =
                    "Confirmed1R";
            }
            else if (entry.StopHit)
            {
                entry.Status =
                    "StoppedWithoutTarget";
            }
            else
            {
                entry.Status =
                    "Triggered";
            }
        }

        private static bool HasAmbiguousOutcome(
            EntryQualification entry)
        {
            return
                entry.OneROutcome ==
                "AmbiguousIntrabar" ||
                entry.OnePointFiveROutcome ==
                "AmbiguousIntrabar" ||
                entry.TwoROutcome ==
                "AmbiguousIntrabar" ||
                entry.ThreeROutcome ==
                "AmbiguousIntrabar";
        }

        private static bool AllOutcomesResolved(
            EntryQualification entry)
        {
            return
                entry.OneROutcome !=
                "NotResolved" &&
                entry.OnePointFiveROutcome !=
                "NotResolved" &&
                entry.TwoROutcome !=
                "NotResolved" &&
                entry.ThreeROutcome !=
                "NotResolved";
        }

        private static string DetermineOverallStatus(
            FvgTradeQualification qualification)
        {
            EntryQualification[] entries =
                GetAllEntries(
                    qualification);

            if (entries.Any(
                    HasAmbiguousOutcome))
            {
                return "ContainsAmbiguousOutcome";
            }

            decimal bestConfirmedR =
                entries.Max(
                    x =>
                        x.HighestConfirmedProfitableR);

            if (bestConfirmedR >= 3m)
            {
                return "Confirmed3ROpportunity";
            }

            if (bestConfirmedR >= 2m)
            {
                return "Confirmed2ROpportunity";
            }

            if (bestConfirmedR >= 1.5m)
            {
                return "Confirmed1.5ROpportunity";
            }

            if (bestConfirmedR >= 1m)
            {
                return "Confirmed1ROpportunity";
            }

            if (entries.Any(
                    x =>
                        x.StopHit))
            {
                return "NoConfirmedProfitBeforeStop";
            }

            if (entries.Any(
                    x =>
                        x.Triggered))
            {
                return "EntryTriggered";
            }

            return "WaitingForEntry";
        }

        private static void UpdateGeneralExtremes(
            FvgTradeQualification qualification,
            Candle candle)
        {
            qualification.HighestPriceAfterFormation =
                !qualification.HighestPriceAfterFormation.HasValue
                    ? candle.High
                    : Math.Max(
                        qualification.HighestPriceAfterFormation.Value,
                        candle.High);

            qualification.LowestPriceAfterFormation =
                !qualification.LowestPriceAfterFormation.HasValue
                    ? candle.Low
                    : Math.Min(
                        qualification.LowestPriceAfterFormation.Value,
                        candle.Low);
        }

        private static void UpdateAggregateExcursion(
            FvgTradeQualification qualification)
        {
            List<EntryQualification> triggered =
                GetAllEntries(
                        qualification)
                    .Where(
                        x =>
                            x.Triggered)
                    .ToList();

            if (triggered.Count == 0)
            {
                return;
            }

            qualification.MaximumFavorableExcursion =
                triggered.Max(
                    x =>
                        x.MaximumFavorableExcursion);

            qualification.MaximumAdverseExcursion =
                triggered.Max(
                    x =>
                        x.MaximumAdverseExcursion);

            qualification.MaximumFavorableR =
                triggered.Max(
                    x =>
                        x.MaximumFavorableR);
        }

        private static EntryQualification[] GetAllEntries(
            FvgTradeQualification qualification)
        {
            return
            [
                qualification.BoundaryTouch,
                qualification.TwentyFivePercent,
                qualification.FiftyPercent,
                qualification.SeventyFivePercent
            ];
        }
    }
}