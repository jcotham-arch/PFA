using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class MesScenarioEngine
    {
        private const decimal DollarsPerPointPerContract =
            5.00m;

        private const decimal TickSize =
            0.25m;

        private const decimal DollarsPerTickPerContract =
            1.25m;

        private static readonly decimal[] TargetRs =
        {
            1.00m,
            1.50m,
            2.00m,
            3.00m
        };

        private static readonly int[] ContractCounts =
        {
            1,
            2
        };

        private readonly MesExecutionNormalizationService
            _normalizationService;

        public MesScenarioEngine(
            MesExecutionNormalizationService normalizationService)
        {
            _normalizationService =
                normalizationService;
        }

        // ============================================================
        // ALL SCENARIOS
        // ============================================================

        public IReadOnlyList<MesTradeScenario> EvaluateAll(
            FairValueGap fvg,
            FvgOutcome outcome,
            IReadOnlyList<Candle> oneMinuteCandles)
        {
            if (fvg is null)
            {
                throw new ArgumentNullException(
                    nameof(fvg));
            }

            if (outcome is null)
            {
                throw new ArgumentNullException(
                    nameof(outcome));
            }

            oneMinuteCandles ??=
                Array.Empty<Candle>();

            var scenarios =
                new List<MesTradeScenario>();

            MesEntryModel[] entryModels =
            {
                MesEntryModel.BoundaryTouch,
                MesEntryModel.TwentyFivePercent,
                MesEntryModel.FiftyPercent,
                MesEntryModel.SeventyFivePercent
            };

            foreach (MesEntryModel entryModel
                     in entryModels)
            {
                foreach (decimal targetR
                         in TargetRs)
                {
                    foreach (int contracts
                             in ContractCounts)
                    {
                        scenarios.Add(
                            EvaluateSingleScenario(
                                fvg,
                                outcome,
                                oneMinuteCandles,
                                entryModel,
                                contracts,
                                targetR));
                    }
                }
            }

            return scenarios;
        }

        // ============================================================
        // SINGLE SCENARIO
        // ============================================================

        public MesTradeScenario EvaluateSingleScenario(
            FairValueGap fvg,
            FvgOutcome outcome,
            IReadOnlyList<Candle> oneMinuteCandles,
            MesEntryModel entryModel,
            int contracts,
            decimal targetR)
        {
            if (fvg is null)
            {
                throw new ArgumentNullException(
                    nameof(fvg));
            }

            if (outcome is null)
            {
                throw new ArgumentNullException(
                    nameof(outcome));
            }

            if (contracts <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contracts));
            }

            if (targetR <= 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetR));
            }

            oneMinuteCandles ??=
                Array.Empty<Candle>();

            // ========================================================
            // THEORETICAL ENTRY
            // ========================================================

            decimal theoreticalEntryPrice =
                GetTheoreticalEntryPrice(
                    fvg,
                    entryModel);

            // ========================================================
            // EXECUTABLE ENTRY
            // ========================================================

            decimal entryPrice =
                _normalizationService.NormalizeEntry(
                    fvg.Direction,
                    theoreticalEntryPrice);

            // ========================================================
            // ACTUAL EXECUTABLE ENTRY TIME
            //
            // CRITICAL:
            //
            // We no longer borrow the theoretical FVG fill timestamp
            // from FvgOutcome.
            //
            // We search the minute data for when the normalized MES
            // order price could actually have filled.
            // ========================================================

            DateTime? entryTimeUtc =
                FindExecutableEntryTime(
                    fvg,
                    outcome.ConfirmationTimeUtc,
                    entryPrice,
                    oneMinuteCandles);

            // ========================================================
            // STOP
            // ========================================================

            decimal theoreticalStopPrice =
                GetTheoreticalStopPrice(
                    fvg);

            decimal stopPrice =
                _normalizationService.NormalizeStop(
                    fvg.Direction,
                    theoreticalStopPrice);

            decimal riskPoints =
                Math.Abs(
                    entryPrice -
                    stopPrice);

            if (riskPoints <= 0m)
            {
                throw new InvalidOperationException(
                    "MES scenario produced zero or negative risk.");
            }

            decimal riskTicks =
                riskPoints /
                TickSize;

            decimal grossDollarRisk =
                riskPoints *
                DollarsPerPointPerContract *
                contracts;

            // ========================================================
            // TARGET
            //
            // Requested R is calculated from ACTUAL normalized risk.
            // Then the target itself is normalized to a valid MES tick.
            // ========================================================

            decimal requestedTargetPoints =
                riskPoints *
                targetR;

            decimal theoreticalTargetPrice =
                GetTargetPrice(
                    fvg.Direction,
                    entryPrice,
                    requestedTargetPoints);

            decimal targetPrice =
                _normalizationService.NormalizeTarget(
                    fvg.Direction,
                    theoreticalTargetPrice);

            decimal targetPoints =
                Math.Abs(
                    targetPrice -
                    entryPrice);

            decimal effectiveTargetR =
                targetPoints /
                riskPoints;

            decimal grossTargetProfit =
                targetPoints *
                DollarsPerPointPerContract *
                contracts;

            // ========================================================
            // VALIDATE EXECUTION PRICES
            // ========================================================

            bool entryPriceValid =
                _normalizationService
                    .IsValidMesPrice(
                        entryPrice);

            bool stopPriceValid =
                _normalizationService
                    .IsValidMesPrice(
                        stopPrice);

            bool targetPriceValid =
                _normalizationService
                    .IsValidMesPrice(
                        targetPrice);

            // ========================================================
            // CREATE SCENARIO
            // ========================================================

            var scenario =
                new MesTradeScenario
                {
                    FvgId =
                        fvg.Id,

                    OutcomeId =
                        outcome.OutcomeId,

                    Symbol =
                        fvg.Symbol,

                    Timeframe =
                        fvg.Timeframe,

                    Direction =
                        fvg.Direction,

                    FormationTimeUtc =
                        fvg.FormationTimeUtc,

                    ConfirmationTimeUtc =
                        outcome.ConfirmationTimeUtc,

                    EntryModel =
                        entryModel,

                    Contracts =
                        contracts,

                    DollarsPerPointPerContract =
                        DollarsPerPointPerContract,

                    TickSize =
                        TickSize,

                    DollarsPerTickPerContract =
                        DollarsPerTickPerContract,

                    TheoreticalEntryPrice =
                        theoreticalEntryPrice,

                    EntryPrice =
                        entryTimeUtc.HasValue
                            ? entryPrice
                            : null,

                    EntryNormalizationPoints =
                        entryPrice -
                        theoreticalEntryPrice,

                    EntryAvailable =
                        entryTimeUtc.HasValue,

                    EntryTriggered =
                        entryTimeUtc.HasValue,

                    EntryTimeUtc =
                        entryTimeUtc,

                    TheoreticalStopPrice =
                        theoreticalStopPrice,

                    StopPrice =
                        entryTimeUtc.HasValue
                            ? stopPrice
                            : null,

                    StopNormalizationPoints =
                        stopPrice -
                        theoreticalStopPrice,

                    RiskPoints =
                        entryTimeUtc.HasValue
                            ? riskPoints
                            : null,

                    RiskTicks =
                        entryTimeUtc.HasValue
                            ? riskTicks
                            : null,

                    GrossDollarRisk =
                        entryTimeUtc.HasValue
                            ? grossDollarRisk
                            : null,

                    TargetR =
                        targetR,

                    TheoreticalTargetPrice =
                        theoreticalTargetPrice,

                    TargetPrice =
                        entryTimeUtc.HasValue
                            ? targetPrice
                            : null,

                    TargetNormalizationPoints =
                        targetPrice -
                        theoreticalTargetPrice,

                    TargetPoints =
                        entryTimeUtc.HasValue
                            ? targetPoints
                            : null,

                    EffectiveTargetR =
                        entryTimeUtc.HasValue
                            ? effectiveTargetR
                            : null,

                    GrossTargetProfit =
                        entryTimeUtc.HasValue
                            ? grossTargetProfit
                            : null,

                    EntryPriceIsValidTick =
                        entryPriceValid,

                    StopPriceIsValidTick =
                        stopPriceValid,

                    TargetPriceIsValidTick =
                        targetPriceValid,

                    AllExecutionPricesValid =
                        entryPriceValid &&
                        stopPriceValid &&
                        targetPriceValid,

                    Status =
                        entryTimeUtc.HasValue
                            ? MesScenarioStatus.Open
                            : MesScenarioStatus.NoEntry,

                    EngineVersion =
                        "1.1.0"
                };

            // ========================================================
            // NO EXECUTABLE ENTRY
            // ========================================================

            if (!entryTimeUtc.HasValue)
            {
                scenario.RealizedPoints =
                    0m;

                scenario.GrossProfitLoss =
                    0m;

                scenario.NetProfitLoss =
                    0m;

                scenario.RealizedR =
                    0m;

                scenario.WasProfitable =
                    null;

                return scenario;
            }

            // ========================================================
            // POST-ENTRY CANDLES
            // ========================================================

            List<Candle> postEntryCandles =
                oneMinuteCandles
                    .Where(c =>
                        c.IsClosed &&
                        c.Symbol.Equals(
                            fvg.Symbol,
                            StringComparison.OrdinalIgnoreCase) &&
                        c.OpenTimeUtc >=
                            entryTimeUtc.Value)
                    .OrderBy(c =>
                        c.OpenTimeUtc)
                    .ToList();

            if (postEntryCandles.Count == 0)
            {
                scenario.Status =
                    MesScenarioStatus.EndOfData;

                return scenario;
            }

            decimal maximumFavorablePoints =
                0m;

            decimal maximumAdversePoints =
                0m;

            bool completed =
                false;

            // ========================================================
            // WALK FORWARD
            // ========================================================

            foreach (Candle candle
                     in postEntryCandles)
            {
                scenario.MinuteCandlesEvaluatedAfterEntry++;

                bool isEntryCandle =
                    candle.OpenTimeUtc ==
                    entryTimeUtc.Value;

                bool targetTouched =
                    IsTargetTouched(
                        fvg.Direction,
                        candle,
                        targetPrice);

                bool stopTouched =
                    IsStopTouched(
                        fvg.Direction,
                        candle,
                        stopPrice);

                // ====================================================
                // ENTRY-CANDLE AMBIGUITY
                //
                // If stop or target was also inside the candle that
                // produced our fill, OHLC does not prove whether that
                // price occurred before or after the entry.
                // ====================================================

                if (isEntryCandle &&
                    (targetTouched ||
                     stopTouched))
                {
                    scenario.IntrabarSequenceUnknown =
                        true;

                    scenario.TargetHit =
                        targetTouched;

                    scenario.StopHit =
                        stopTouched;

                    if (targetTouched)
                    {
                        scenario.TargetHitTimeUtc =
                            candle.OpenTimeUtc;
                    }

                    if (stopTouched)
                    {
                        scenario.StopHitTimeUtc =
                            candle.OpenTimeUtc;
                    }

                    scenario.EvaluatedThroughUtc =
                        candle.OpenTimeUtc;

                    completed =
                        true;

                    break;
                }

                // ====================================================
                // EXCURSION MEASUREMENT
                //
                // We deliberately exclude the entry candle because
                // some of that candle's high/low may have occurred
                // BEFORE the actual fill.
                // ====================================================

                if (!isEntryCandle)
                {
                    maximumFavorablePoints =
                        Math.Max(
                            maximumFavorablePoints,
                            GetMaximumFavorablePoints(
                                fvg.Direction,
                                entryPrice,
                                candle));

                    maximumAdversePoints =
                        Math.Max(
                            maximumAdversePoints,
                            GetMaximumAdversePoints(
                                fvg.Direction,
                                entryPrice,
                                candle));
                }

                // ====================================================
                // SAME LATER CANDLE: TARGET + STOP
                // ====================================================

                if (targetTouched &&
                    stopTouched)
                {
                    scenario.TargetHit =
                        true;

                    scenario.StopHit =
                        true;

                    scenario.TargetHitTimeUtc =
                        candle.OpenTimeUtc;

                    scenario.StopHitTimeUtc =
                        candle.OpenTimeUtc;

                    scenario.IntrabarSequenceUnknown =
                        true;

                    scenario.EvaluatedThroughUtc =
                        candle.OpenTimeUtc;

                    completed =
                        true;

                    break;
                }

                // ====================================================
                // TARGET FIRST
                // ====================================================

                if (targetTouched)
                {
                    scenario.TargetHit =
                        true;

                    scenario.TargetHitTimeUtc =
                        candle.OpenTimeUtc;

                    scenario.TargetBeforeStop =
                        true;

                    scenario.Status =
                        MesScenarioStatus.TargetHit;

                    scenario.RealizedPoints =
                        targetPoints;

                    scenario.RealizedR =
                        effectiveTargetR;

                    scenario.GrossProfitLoss =
                        grossTargetProfit;

                    scenario.WasProfitable =
                        true;

                    scenario.EvaluatedThroughUtc =
                        candle.OpenTimeUtc;

                    completed =
                        true;

                    break;
                }

                // ====================================================
                // STOP FIRST
                // ====================================================

                if (stopTouched)
                {
                    scenario.StopHit =
                        true;

                    scenario.StopHitTimeUtc =
                        candle.OpenTimeUtc;

                    scenario.StopBeforeTarget =
                        true;

                    scenario.Status =
                        MesScenarioStatus.StopHit;

                    scenario.RealizedPoints =
                        -riskPoints;

                    scenario.RealizedR =
                        -1m;

                    scenario.GrossProfitLoss =
                        -grossDollarRisk;

                    scenario.WasProfitable =
                        false;

                    scenario.EvaluatedThroughUtc =
                        candle.OpenTimeUtc;

                    completed =
                        true;

                    break;
                }
            }

            // ========================================================
            // MFE / MAE
            // ========================================================

            scenario.MaximumFavorableExcursionPoints =
                maximumFavorablePoints;

            scenario.MaximumAdverseExcursionPoints =
                maximumAdversePoints;

            scenario.MaximumFavorableR =
                maximumFavorablePoints /
                riskPoints;

            scenario.MaximumAdverseR =
                maximumAdversePoints /
                riskPoints;

            // ========================================================
            // END OF DATA
            // ========================================================

            if (!completed)
            {
                Candle finalCandle =
                    postEntryCandles[^1];

                decimal realizedPoints =
                    GetDirectionalPoints(
                        fvg.Direction,
                        entryPrice,
                        finalCandle.Close);

                scenario.Status =
                    MesScenarioStatus.EndOfData;

                scenario.RealizedPoints =
                    realizedPoints;

                scenario.RealizedR =
                    realizedPoints /
                    riskPoints;

                scenario.GrossProfitLoss =
                    realizedPoints *
                    DollarsPerPointPerContract *
                    contracts;

                scenario.WasProfitable =
                    realizedPoints > 0m
                        ? true
                        : realizedPoints < 0m
                            ? false
                            : null;

                scenario.EvaluatedThroughUtc =
                    finalCandle.OpenTimeUtc;
            }

            // ========================================================
            // AMBIGUOUS RESULTS HAVE NO REALIZED P&L
            // ========================================================

            if (scenario.IntrabarSequenceUnknown)
            {
                scenario.GrossProfitLoss =
                    null;

                scenario.NetProfitLoss =
                    null;

                scenario.RealizedPoints =
                    null;

                scenario.RealizedR =
                    null;

                scenario.WasProfitable =
                    null;

                return scenario;
            }

            // ========================================================
            // TRADING COSTS
            // ========================================================

            scenario.TotalEstimatedCommission =
                scenario.CommissionPerContractRoundTrip *
                contracts;

            scenario.TotalEstimatedSlippageCost =
                scenario.EstimatedSlippagePointsPerSide *
                2m *
                DollarsPerPointPerContract *
                contracts;

            scenario.TotalEstimatedTradingCost =
                scenario.TotalEstimatedCommission +
                scenario.TotalEstimatedSlippageCost;

            if (scenario.GrossProfitLoss.HasValue)
            {
                scenario.NetProfitLoss =
                    scenario.GrossProfitLoss.Value -
                    scenario.TotalEstimatedTradingCost;
            }

            return scenario;
        }

        // ============================================================
        // THEORETICAL ENTRY
        // ============================================================

        private static decimal GetTheoreticalEntryPrice(
            FairValueGap fvg,
            MesEntryModel entryModel)
        {
            decimal gap =
                fvg.UpperBoundary -
                fvg.LowerBoundary;

            return entryModel switch
            {
                MesEntryModel.BoundaryTouch =>
                    fvg.Direction ==
                    FvgDirection.Bullish
                        ? fvg.UpperBoundary
                        : fvg.LowerBoundary,

                MesEntryModel.TwentyFivePercent =>
                    fvg.Direction ==
                    FvgDirection.Bullish
                        ? fvg.UpperBoundary -
                          (gap * 0.25m)
                        : fvg.LowerBoundary +
                          (gap * 0.25m),

                MesEntryModel.FiftyPercent =>
                    fvg.Midpoint,

                MesEntryModel.SeventyFivePercent =>
                    fvg.Direction ==
                    FvgDirection.Bullish
                        ? fvg.UpperBoundary -
                          (gap * 0.75m)
                        : fvg.LowerBoundary +
                          (gap * 0.75m),

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(entryModel))
            };
        }

        // ============================================================
        // ACTUAL ENTRY TIME
        // ============================================================

        private static DateTime? FindExecutableEntryTime(
            FairValueGap fvg,
            DateTime confirmationTimeUtc,
            decimal executableEntryPrice,
            IReadOnlyList<Candle> candles)
        {
            foreach (Candle candle in
                     candles
                         .Where(c =>
                             c.IsClosed &&
                             c.Symbol.Equals(
                                 fvg.Symbol,
                                 StringComparison.OrdinalIgnoreCase) &&
                             c.OpenTimeUtc >=
                                 confirmationTimeUtc)
                         .OrderBy(c =>
                             c.OpenTimeUtc))
            {
                if (fvg.Direction ==
                    FvgDirection.Bullish)
                {
                    if (candle.Low <=
                        executableEntryPrice)
                    {
                        return candle.OpenTimeUtc;
                    }
                }
                else
                {
                    if (candle.High >=
                        executableEntryPrice)
                    {
                        return candle.OpenTimeUtc;
                    }
                }
            }

            return null;
        }

        // ============================================================
        // THEORETICAL STOP
        // ============================================================

        private static decimal GetTheoreticalStopPrice(
            FairValueGap fvg)
        {
            return fvg.Direction ==
                FvgDirection.Bullish
                    ? fvg.LowerBoundary -
                      TickSize
                    : fvg.UpperBoundary +
                      TickSize;
        }

        // ============================================================
        // TARGET
        // ============================================================

        private static decimal GetTargetPrice(
            FvgDirection direction,
            decimal entryPrice,
            decimal targetPoints)
        {
            return direction ==
                FvgDirection.Bullish
                    ? entryPrice +
                      targetPoints
                    : entryPrice -
                      targetPoints;
        }

        private static bool IsTargetTouched(
            FvgDirection direction,
            Candle candle,
            decimal targetPrice)
        {
            return direction ==
                FvgDirection.Bullish
                    ? candle.High >=
                      targetPrice
                    : candle.Low <=
                      targetPrice;
        }

        private static bool IsStopTouched(
            FvgDirection direction,
            Candle candle,
            decimal stopPrice)
        {
            return direction ==
                FvgDirection.Bullish
                    ? candle.Low <=
                      stopPrice
                    : candle.High >=
                      stopPrice;
        }

        // ============================================================
        // EXCURSION
        // ============================================================

        private static decimal GetMaximumFavorablePoints(
            FvgDirection direction,
            decimal entryPrice,
            Candle candle)
        {
            decimal points =
                direction ==
                FvgDirection.Bullish
                    ? candle.High -
                      entryPrice
                    : entryPrice -
                      candle.Low;

            return Math.Max(
                0m,
                points);
        }

        private static decimal GetMaximumAdversePoints(
            FvgDirection direction,
            decimal entryPrice,
            Candle candle)
        {
            decimal points =
                direction ==
                FvgDirection.Bullish
                    ? entryPrice -
                      candle.Low
                    : candle.High -
                      entryPrice;

            return Math.Max(
                0m,
                points);
        }

        private static decimal GetDirectionalPoints(
            FvgDirection direction,
            decimal entryPrice,
            decimal exitPrice)
        {
            return direction ==
                FvgDirection.Bullish
                    ? exitPrice -
                      entryPrice
                    : entryPrice -
                      exitPrice;
        }
    }
}