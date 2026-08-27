using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class CrossDayEvidenceController : ControllerBase
    {
        private readonly HistoricalCandleRebuildService
            _historicalRebuildService;

        private readonly FvgCrossDayEvidenceService
            _crossDayEvidenceService;

        public CrossDayEvidenceController(
            HistoricalCandleRebuildService historicalRebuildService,
            FvgCrossDayEvidenceService crossDayEvidenceService)
        {
            _historicalRebuildService =
                historicalRebuildService;

            _crossDayEvidenceService =
                crossDayEvidenceService;
        }

        // ============================================================
        // CROSS-DAY EVIDENCE ANALYSIS
        //
        // This endpoint is intentionally compact.
        //
        // It:
        //
        // 1. Runs each supplied trading day independently
        // 2. Uses each day's candidate discovery results
        // 3. Aggregates identical rule definitions across days
        // 4. Measures persistence
        // 5. Returns only the strongest evidence
        //
        // No strategy can become active from this endpoint.
        // ============================================================

        [HttpPost("{symbol}")]
        public async Task<ActionResult> Analyze(
            string symbol,
            [FromQuery] DateTime startUtc,
            [FromQuery] DateTime endUtc,
            [FromQuery] int top = 10,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Symbol is required."
                    });
            }

            if (endUtc <= startUtc)
            {
                return BadRequest(
                    new
                    {
                        message =
                            "endUtc must be after startUtc."
                    });
            }

            top =
                Math.Clamp(
                    top,
                    1,
                    25);

            try
            {
                string normalizedSymbol =
                    symbol
                        .Trim()
                        .ToUpperInvariant();

                DateTime normalizedStart =
                    EnsureUtc(
                        startUtc);

                DateTime normalizedEnd =
                    EnsureUtc(
                        endUtc);

                List<DateTime> tradingDates =
                    BuildTradingDates(
                        normalizedStart,
                        normalizedEnd);

                var dayInputs =
                    new List<FvgCrossDayEvidenceInput>();

                var datasetDays =
                    new List<object>();

                // ====================================================
                // RUN EACH DAY INDEPENDENTLY
                //
                // IMPORTANT:
                //
                // We intentionally do NOT run the entire date range as
                // one discovery dataset.
                //
                // Each day must produce its own independent candidate
                // discovery report so persistence can be measured.
                // ====================================================

                foreach (DateTime tradingDate
                         in tradingDates)
                {
                    DateTime dayStart =
                        new DateTime(
                            tradingDate.Year,
                            tradingDate.Month,
                            tradingDate.Day,
                            0,
                            0,
                            0,
                            DateTimeKind.Utc);

                    DateTime dayEnd =
                        dayStart
                            .AddDays(1)
                            .AddMinutes(-1);

                    HistoricalRebuildResult result =
                        await _historicalRebuildService
                            .RebuildFiveMinuteCandlesAsync(
                                normalizedSymbol,
                                dayStart,
                                dayEnd,
                                cancellationToken);

                    // ------------------------------------------------
                    // Skip days with no usable market data.
                    //
                    // This naturally excludes Saturdays and other
                    // empty calendar days.
                    // ------------------------------------------------

                    if (result.OneMinuteCandlesLoaded <= 0 ||
                        result.FvgsDetected <= 0)
                    {
                        datasetDays.Add(
                            new
                            {
                                tradingDateUtc =
                                    dayStart,

                                included =
                                    false,

                                reason =
                                    "No usable historical FVG dataset.",

                                result
                                    .OneMinuteCandlesLoaded,

                                result
                                    .FiveMinuteCandlesBuilt,

                                result
                                    .FvgsDetected
                            });

                        continue;
                    }

                    dayInputs.Add(
                        new FvgCrossDayEvidenceInput
                        {
                            TradingDateUtc =
                                dayStart,

                            Symbol =
                                normalizedSymbol,

                            CandidateDiscovery =
                                result.CandidateDiscovery
                        });

                    datasetDays.Add(
                        new
                        {
                            tradingDateUtc =
                                dayStart,

                            included =
                                true,

                            result
                                .OneMinuteCandlesLoaded,

                            result
                                .FiveMinuteCandlesBuilt,

                            result
                                .FvgsDetected,

                            result
                                .OutcomesEvaluated,

                            learningRecords =
                                result
                                    .FeatureAnalysis
                                    .TotalLearningRecords,

                            distinctFvgs =
                                result
                                    .CandidateDiscovery
                                    .DistinctFvgsEvaluated,

                            candidateRulesTested =
                                result
                                    .CandidateDiscovery
                                    .CandidateRulesTested,

                            promisingRules =
                                result
                                    .CandidateDiscovery
                                    .PromisingRules
                        });
                }

                if (dayInputs.Count == 0)
                {
                    return Ok(
                        new
                        {
                            symbol =
                                normalizedSymbol,

                            startUtc =
                                normalizedStart,

                            endUtc =
                                normalizedEnd,

                            tradingDaysEvaluated =
                                0,

                            message =
                                "No usable trading days were found " +
                                "inside the requested range.",

                            datasetDays
                        });
                }

                // ====================================================
                // CROSS-DAY ANALYSIS
                // ====================================================

                FvgCrossDayEvidenceReport report =
                    _crossDayEvidenceService
                        .Analyze(
                            dayInputs);

                // ====================================================
                // COMPACT RESPONSE
                // ====================================================

                return Ok(
                    new
                    {
                        dataset =
                            new
                            {
                                report.Symbol,

                                requestedStartUtc =
                                    normalizedStart,

                                requestedEndUtc =
                                    normalizedEnd,

                                report.StartDateUtc,

                                report.EndDateUtc,

                                report.TradingDaysEvaluated,

                                calendarDaysRequested =
                                    tradingDates.Count,

                                datasetDays
                            },

                        evidenceSummary =
                            new
                            {
                                report.UniqueRulesObserved,

                                report.PersistentCandidateCount,

                                report.WatchlistCount,

                                report.UnstableCount,

                                report.PersistentNegativeCount,

                                report.InsufficientEvidenceCount
                            },

                        gates =
                            new
                            {
                                requiredObservedDays =
                                    3,

                                requiredDistinctFvgs =
                                    20,

                                requiredPositiveDayPercentage =
                                    60,

                                requiredMinimumExpectancyR =
                                    0.10m,

                                requiredMinimumProfitFactor =
                                    1.25m
                            },

                        persistentCandidates =
                            report
                                .PersistentCandidates
                                .Take(top)
                                .Select(
                                    BuildCompactRule),

                        watchlist =
                            report
                                .Watchlist
                                .Take(top)
                                .Select(
                                    BuildCompactRule),

                        persistentNegativeRules =
                            report
                                .PersistentNegativeRules
                                .Take(top)
                                .Select(
                                    BuildCompactRule),

                        researchState =
                            new
                            {
                                report.ResearchState,

                                report
                                    .CanActivateAnyStrategy,

                                report
                                    .NextRequiredStage
                            }
                    });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message =
                            "Cross-day FVG evidence analysis failed.",

                        error =
                            ex.Message
                    });
            }
        }

        // ============================================================
        // COMPACT RULE
        // ============================================================

        private static object BuildCompactRule(
            FvgCrossDayRuleEvidence rule)
        {
            return new
            {
                rule.RuleName,

                strategy =
                    new
                    {
                        rule.EntryModel,

                        rule.TargetR,

                        rule.Direction,

                        rule.SessionBucket
                    },

                filters =
                    new
                    {
                        rule.MinimumGapSizePoints,

                        rule.MaximumGapSizePoints,

                        rule.MinimumMinutesToEntry,

                        rule.MaximumMinutesToEntry,

                        rule.MinimumRiskTicks,

                        rule.MaximumRiskTicks
                    },

                persistence =
                    new
                    {
                        rule.TotalDaysInDataset,

                        rule.DaysObserved,

                        rule.PositiveDays,

                        rule.NegativeDays,

                        rule.FlatDays,

                        rule.PositiveDayPercentage,

                        rule.NegativeDayPercentage
                    },

                evidence =
                    new
                    {
                        rule.TotalTrades,

                        rule.TotalDistinctFvgs,

                        rule.Wins,

                        rule.Losses,

                        rule.WinRate
                    },

                performance =
                    new
                    {
                        rule.NetR,

                        rule.ExpectancyR,

                        rule.AverageDailyExpectancyR,

                        rule.ProfitFactorR,

                        rule.AverageWinnerR,

                        rule.AverageLoserR
                    },

                stability =
                    new
                    {
                        rule.BestDayR,

                        rule.WorstDayR,

                        rule.MaximumObservedDailyDrawdownR,

                        rule.CrossDayMaximumDrawdownR,

                        rule.ExpectancyStandardDeviation,

                        rule.ExpectancyStabilityScore
                    },

                capital =
                    new
                    {
                        rule.RawNetProfitLoss,

                        rule.FixedRisk25NetProfitLoss,

                        rule.FixedRisk50NetProfitLoss
                    },

                promotionGates =
                    new
                    {
                        rule.PassedDayCoverageGate,

                        rule.PassedSampleGate,

                        rule.PassedPositiveDaysGate,

                        rule.PassedExpectancyGate,

                        rule.PassedProfitFactorGate,

                        rule.PassedPersistenceGates
                    },

                research =
                    new
                    {
                        rule.Status,

                        rule.PersistenceScore,

                        rule.EvidenceSummary,

                        rule.CanAdvanceToFrozenValidation,

                        rule.CanActivateStrategy
                    },

                dailyResults =
                    rule.DailyResults
            };
        }

        // ============================================================
        // BUILD CALENDAR DATES
        // ============================================================

        private static List<DateTime> BuildTradingDates(
            DateTime startUtc,
            DateTime endUtc)
        {
            DateTime startDate =
                startUtc.Date;

            DateTime endDate =
                endUtc.Date;

            var dates =
                new List<DateTime>();

            DateTime current =
                startDate;

            while (current <= endDate)
            {
                // ----------------------------------------------------
                // Saturday has no MES session.
                //
                // Sunday is excluded in this first implementation
                // because its futures session begins later in the day
                // and does not represent a clean UTC trading day.
                //
                // We can later replace UTC calendar days with actual
                // CME session-date logic.
                // ----------------------------------------------------

                if (current.DayOfWeek !=
                        DayOfWeek.Saturday &&
                    current.DayOfWeek !=
                        DayOfWeek.Sunday)
                {
                    dates.Add(
                        DateTime.SpecifyKind(
                            current,
                            DateTimeKind.Utc));
                }

                current =
                    current.AddDays(1);
            }

            return dates;
        }

        // ============================================================
        // UTC
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