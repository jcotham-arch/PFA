using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class OutOfSampleValidationController : ControllerBase
    {
        private readonly HistoricalCandleRebuildService
            _historicalRebuildService;

        private readonly FvgOutOfSampleValidationService
            _validationService;

        public OutOfSampleValidationController(
            HistoricalCandleRebuildService historicalRebuildService,
            FvgOutOfSampleValidationService validationService)
        {
            _historicalRebuildService =
                historicalRebuildService;

            _validationService =
                validationService;
        }

        // ============================================================
        // OUT-OF-SAMPLE VALIDATION
        //
        // Example:
        //
        // POST /api/OutOfSampleValidation/MESU6
        //
        // ?discoveryStartUtc=2026-08-26T14:00:00Z
        // &discoveryEndUtc=2026-08-26T23:44:00Z
        //
        // &validationStartUtc=2026-08-25T00:00:00Z
        // &validationEndUtc=2026-08-25T23:59:00Z
        //
        // The candidate is discovered ONLY from the discovery window.
        //
        // The validation window is evaluated separately.
        // ============================================================

        [HttpPost("{symbol}")]
        public async Task<ActionResult> Validate(
            string symbol,
            [FromQuery] DateTime discoveryStartUtc,
            [FromQuery] DateTime discoveryEndUtc,
            [FromQuery] DateTime validationStartUtc,
            [FromQuery] DateTime validationEndUtc,
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

            if (discoveryEndUtc <=
                discoveryStartUtc)
            {
                return BadRequest(
                    new
                    {
                        message =
                            "discoveryEndUtc must be after discoveryStartUtc."
                    });
            }

            if (validationEndUtc <=
                validationStartUtc)
            {
                return BadRequest(
                    new
                    {
                        message =
                            "validationEndUtc must be after validationStartUtc."
                    });
            }

            // ========================================================
            // HARD DATA-SEPARATION RULE
            //
            // Discovery and validation windows may not overlap.
            // ========================================================

            bool overlaps =
                discoveryStartUtc <=
                    validationEndUtc &&
                validationStartUtc <=
                    discoveryEndUtc;

            if (overlaps)
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Discovery and validation windows must not overlap."
                    });
            }

            try
            {
                // ====================================================
                // STAGE 1:
                // RUN DISCOVERY WINDOW
                // ====================================================

                HistoricalRebuildResult discoveryResult =
                    await _historicalRebuildService
                        .RebuildFiveMinuteCandlesAsync(
                            symbol,
                            discoveryStartUtc,
                            discoveryEndUtc,
                            cancellationToken);

                // ====================================================
                // CHOOSE FROZEN CANDIDATE
                //
                // Only sample-qualified positive candidates are
                // eligible.
                //
                // Prefer PromisingCandidate first.
                // Then research score.
                // ====================================================

                FvgCandidateRule? discoveryCandidate =
                    discoveryResult
                        .CandidateDiscovery
                        .RankedCandidates
                        .Where(x =>
                            x.MeetsMinimumSample)
                        .Where(x =>
                            x.PositiveExpectancy)
                        .OrderByDescending(x =>
                            x.Status ==
                            CandidateRuleStatus.PromisingCandidate)
                        .ThenByDescending(x =>
                            x.ResearchScore)
                        .ThenByDescending(x =>
                            x.ExpectancyR)
                        .ThenByDescending(x =>
                            x.DistinctFvgs)
                        .FirstOrDefault();

                if (discoveryCandidate is null)
                {
                    return Ok(
                        new
                        {
                            symbol =
                                symbol
                                    .Trim()
                                    .ToUpperInvariant(),

                            discoveryWindow =
                                new
                                {
                                    discoveryStartUtc,
                                    discoveryEndUtc
                                },

                            validationWindow =
                                new
                                {
                                    validationStartUtc,
                                    validationEndUtc
                                },

                            candidateFound =
                                false,

                            message =
                                "No sample-qualified positive-expectancy candidate " +
                                "was found in the discovery window.",

                            discovery =
                                new
                                {
                                    discoveryResult
                                        .CandidateDiscovery
                                        .DistinctFvgsEvaluated,

                                    discoveryResult
                                        .CandidateDiscovery
                                        .CandidateRulesTested,

                                    discoveryResult
                                        .CandidateDiscovery
                                        .RulesMeetingMinimumSample,

                                    discoveryResult
                                        .CandidateDiscovery
                                        .PositiveExpectancyRules,

                                    discoveryResult
                                        .CandidateDiscovery
                                        .PromisingRules
                                }
                        });
                }

                // ====================================================
                // FREEZE RULE
                // ====================================================

                FrozenFvgCandidate frozenCandidate =
                    _validationService
                        .FreezeCandidate(
                            discoveryCandidate);

                // ====================================================
                // STAGE 2:
                // RUN COMPLETELY SEPARATE VALIDATION WINDOW
                // ====================================================

                HistoricalRebuildResult validationResult =
                    await _historicalRebuildService
                        .RebuildFiveMinuteCandlesAsync(
                            symbol,
                            validationStartUtc,
                            validationEndUtc,
                            cancellationToken);

                // ====================================================
                // APPLY FROZEN RULE
                //
                // No candidate discovery data is allowed to modify
                // these parameters.
                // ====================================================

                FvgOutOfSampleValidationReport report =
                    _validationService
                        .Validate(
                            frozenCandidate,
                            validationResult.FeatureRecords,
                            validationStartUtc,
                            validationEndUtc);

                // ====================================================
                // COMPACT RESPONSE
                // ====================================================

                return Ok(
                    new
                    {
                        symbol =
                            symbol
                                .Trim()
                                .ToUpperInvariant(),

                        candidate =
                            new
                            {
                                frozenCandidate
                                    .CandidateName,

                                frozenCandidate
                                    .EntryModel,

                                frozenCandidate
                                    .TargetR,

                                frozenCandidate
                                    .Direction,

                                frozenCandidate
                                    .SessionBucket,

                                filters =
                                    new
                                    {
                                        frozenCandidate
                                            .MinimumGapSizePoints,

                                        frozenCandidate
                                            .MaximumGapSizePoints,

                                        frozenCandidate
                                            .MinimumMinutesToEntry,

                                        frozenCandidate
                                            .MaximumMinutesToEntry,

                                        frozenCandidate
                                            .MinimumRiskTicks,

                                        frozenCandidate
                                            .MaximumRiskTicks
                                    }
                            },

                        discovery =
                            new
                            {
                                startUtc =
                                    discoveryStartUtc,

                                endUtc =
                                    discoveryEndUtc,

                                frozenCandidate
                                    .DiscoveryTrades,

                                frozenCandidate
                                    .DiscoveryDistinctFvgs,

                                frozenCandidate
                                    .DiscoveryWinRate,

                                frozenCandidate
                                    .DiscoveryExpectancyR,

                                frozenCandidate
                                    .DiscoveryProfitFactorR,

                                frozenCandidate
                                    .DiscoveryMaximumDrawdownR,

                                discoveryStatus =
                                    discoveryCandidate.Status,

                                discoveryCandidate
                                    .ResearchScore
                            },

                        validationDataset =
                            new
                            {
                                startUtc =
                                    validationStartUtc,

                                endUtc =
                                    validationEndUtc,

                                validationResult
                                    .OneMinuteCandlesLoaded,

                                validationResult
                                    .FiveMinuteCandlesBuilt,

                                validationResult
                                    .FvgsDetected,

                                validationResult
                                    .OutcomesEvaluated,

                                report
                                    .TotalValidationRecordsEvaluated
                            },

                        validation =
                            new
                            {
                                report
                                    .DaysWithEligibleTrades,

                                report
                                    .MatchingTrades,

                                report
                                    .DistinctFvgs,

                                report.Wins,

                                report.Losses,

                                report.WinRate,

                                report.NetR,

                                report.ExpectancyR,

                                report.AverageWinnerR,

                                report.AverageLoserR,

                                report.ProfitFactorR,

                                report.MaximumConsecutiveLosses,

                                report.MaximumDrawdownR,

                                report.RawOneMesNetProfitLoss,

                                report.FixedRisk25NetProfitLoss,

                                report.FixedRisk50NetProfitLoss
                            },

                        stability =
                            new
                            {
                                report.PositiveDays,

                                report.NegativeDays,

                                report
                                    .PositiveDayPercentage,

                                report
                                    .ExpectancyRetentionPercentage,

                                report
                                    .WinRateChangePercentagePoints,

                                report.DailyResults
                            },

                        promotionGates =
                            new
                            {
                                report.RequiredDistinctFvgs,

                                report.RequiredTradingDays,

                                report.RequiredMinimumExpectancyR,

                                report.RequiredMinimumProfitFactor,

                                report.RequiredMinimumPositiveDayPercentage,

                                report.MaximumAllowedDrawdownR,

                                report.PassedSampleGate,

                                report.PassedDayCountGate,

                                report.PassedExpectancyGate,

                                report.PassedProfitFactorGate,

                                report.PassedPositiveDaysGate,

                                report.PassedDrawdownGate,

                                report.PassedAllPromotionGates
                            },

                        decision =
                            new
                            {
                                report.Decision,

                                report.DecisionReason,

                                report.CanActivateStrategy,

                                report.NextRequiredStage
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
                            "Out-of-sample FVG validation failed.",

                        error =
                            ex.Message
                    });
            }
        }
    }
}