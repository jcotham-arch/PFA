using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class ResearchSummaryController : ControllerBase
    {
        private readonly HistoricalCandleRebuildService
            _historicalRebuildService;

        public ResearchSummaryController(
            HistoricalCandleRebuildService historicalRebuildService)
        {
            _historicalRebuildService =
                historicalRebuildService;
        }

        // ============================================================
        // COMPACT RESEARCH SUMMARY
        //
        // Example:
        //
        // POST
        // /api/ResearchSummary/MESU6
        // ?startUtc=2026-08-26T14:00:00Z
        // &endUtc=2026-08-26T23:44:00Z
        // &top=10
        //
        // This still performs the full historical research pipeline,
        // but DOES NOT return:
        //
        // - hundreds of candles
        // - all FVG objects
        // - all outcomes
        // - 640 MES scenarios
        // - every feature record
        //
        // It returns only the useful research conclusions.
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
                HistoricalRebuildResult result =
                    await _historicalRebuildService
                        .RebuildFiveMinuteCandlesAsync(
                            symbol,
                            startUtc,
                            endUtc,
                            cancellationToken);

                FvgFeatureAnalysisReport features =
                    result.FeatureAnalysis;

                FvgCandidateDiscoveryReport discovery =
                    result.CandidateDiscovery;

                // ====================================================
                // CANDIDATES THAT HAVE AT LEAST THE MINIMUM SAMPLE
                // ====================================================

                List<FvgCandidateRule> sampleQualified =
                    discovery.RankedCandidates
                        .Where(x =>
                            x.MeetsMinimumSample)
                        .OrderByDescending(x =>
                            x.ResearchScore)
                        .ThenByDescending(x =>
                            x.ExpectancyR)
                        .ThenByDescending(x =>
                            x.DistinctFvgs)
                        .ToList();

                // ====================================================
                // POSITIVE CANDIDATES
                // ====================================================

                List<FvgCandidateRule> positiveCandidates =
                    sampleQualified
                        .Where(x =>
                            x.PositiveExpectancy)
                        .OrderByDescending(x =>
                            x.ResearchScore)
                        .ThenByDescending(x =>
                            x.ExpectancyR)
                        .Take(top)
                        .ToList();

                // ====================================================
                // PROMISING CANDIDATES
                // ====================================================

                List<FvgCandidateRule> promisingCandidates =
                    sampleQualified
                        .Where(x =>
                            x.Status ==
                            CandidateRuleStatus.PromisingCandidate)
                        .OrderByDescending(x =>
                            x.ResearchScore)
                        .ThenByDescending(x =>
                            x.ExpectancyR)
                        .Take(top)
                        .ToList();

                // ====================================================
                // NEGATIVE RULES
                //
                // These are valuable too. They tell us which behaviors
                // should potentially be avoided later.
                // ====================================================

                List<FvgCandidateRule> negativeCandidates =
                    sampleQualified
                        .Where(x =>
                            x.Status ==
                            CandidateRuleStatus.NegativeExpectancy)
                        .OrderBy(x =>
                            x.ExpectancyR)
                        .ThenByDescending(x =>
                            x.DistinctFvgs)
                        .Take(top)
                        .ToList();

                // ====================================================
                // BUILD COMPACT RESPONSE
                // ====================================================

                return Ok(
                    new
                    {
                        // ------------------------------------------------
                        // DATASET
                        // ------------------------------------------------

                        dataset =
                            new
                            {
                                result.Symbol,
                                result.StartUtc,
                                result.EndUtc,

                                result.OneMinuteCandlesLoaded,

                                result.FiveMinuteCandlesBuilt,

                                result.FvgsDetected,

                                result.OutcomesEvaluated
                            },

                        // ------------------------------------------------
                        // EXECUTION QUALITY
                        // ------------------------------------------------

                        execution =
                            new
                            {
                                result
                                    .ScenarioSummary
                                    .TotalScenarios,

                                result
                                    .ScenarioSummary
                                    .ExecutableScenarios,

                                result
                                    .ScenarioSummary
                                    .ResolvedScenarios,

                                result
                                    .ScenarioSummary
                                    .NoEntryScenarios,

                                result
                                    .ScenarioSummary
                                    .AmbiguousScenarios,

                                result
                                    .ScenarioSummary
                                    .EndOfDataScenarios
                            },

                        // ------------------------------------------------
                        // BASELINE LEARNING POPULATION
                        // ------------------------------------------------

                        learningPopulation =
                            new
                            {
                                features
                                    .TotalLearningRecords,

                                features.Wins,

                                features.Losses,

                                features.WinRate,

                                features
                                    .AverageRealizedR,

                                features
                                    .NetProfitLoss,

                                distinctFvgs =
                                    result.FeatureRecords
                                        .Select(x =>
                                            x.FvgId)
                                        .Distinct()
                                        .Count()
                            },

                        // ------------------------------------------------
                        // CANDIDATE DISCOVERY SUMMARY
                        // ------------------------------------------------

                        candidateDiscovery =
                            new
                            {
                                discovery
                                    .LearningRecordsEvaluated,

                                discovery
                                    .DistinctFvgsEvaluated,

                                discovery
                                    .CandidateRulesTested,

                                discovery
                                    .RulesMeetingMinimumSample,

                                discovery
                                    .PositiveExpectancyRules,

                                discovery
                                    .PromisingRules,

                                discovery
                                    .MinimumSampleRequired,

                                discovery
                                    .DatasetWarning
                            },

                        // ------------------------------------------------
                        // FEATURE LEADERS
                        //
                        // These are single-variable observations.
                        // They are NOT automatically strategy rules.
                        // ------------------------------------------------

                        featureLeaders =
                            new
                            {
                                bestEntryModel =
                                    GetBestGroup(
                                        features.ByEntryModel),

                                bestTargetR =
                                    GetBestGroup(
                                        features.ByTargetR),

                                bestDirection =
                                    GetBestGroup(
                                        features.ByDirection),

                                bestSession =
                                    GetBestGroup(
                                        features.BySession),

                                bestGapSize =
                                    GetBestGroup(
                                        features.ByGapSize),

                                bestEntryDelay =
                                    GetBestGroup(
                                        features.ByEntryDelay),

                                bestRiskRange =
                                    GetBestGroup(
                                        features.ByRiskTicks)
                            },

                        // ------------------------------------------------
                        // PROMISING RULES
                        // ------------------------------------------------

                        promisingCandidates =
                            promisingCandidates
                                .Select(
                                    BuildCandidateSummary),

                        // ------------------------------------------------
                        // ALL POSITIVE RULES THAT CLEAR SAMPLE FLOOR
                        // ------------------------------------------------

                        positiveCandidates =
                            positiveCandidates
                                .Select(
                                    BuildCandidateSummary),

                        // ------------------------------------------------
                        // BEST AVAILABLE SAMPLE-QUALIFIED RULES
                        //
                        // Useful even when there are currently zero
                        // positive rules.
                        // ------------------------------------------------

                        topSampleQualifiedRules =
                            sampleQualified
                                .Take(top)
                                .Select(
                                    BuildCandidateSummary),

                        // ------------------------------------------------
                        // RULES CURRENTLY SHOWING CLEARLY NEGATIVE EDGE
                        // ------------------------------------------------

                        strongestAvoidSignals =
                            negativeCandidates
                                .Select(
                                    BuildCandidateSummary),

                        // ------------------------------------------------
                        // CURRENT RESEARCH STATE
                        // ------------------------------------------------

                        researchState =
                            BuildResearchState(
                                discovery,
                                promisingCandidates,
                                positiveCandidates)
                    });
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message =
                            "Compact research analysis failed.",

                        error =
                            ex.Message
                    });
            }
        }

        // ============================================================
        // BEST SINGLE FEATURE GROUP
        // ============================================================

        private static object? GetBestGroup(
            IReadOnlyList<FvgFeatureGroupResult> groups)
        {
            FvgFeatureGroupResult? best =
                groups
                    .Where(x =>
                        x.Trades > 0)
                    .OrderByDescending(x =>
                        x.ExpectancyR)
                    .ThenByDescending(x =>
                        x.Trades)
                    .FirstOrDefault();

            if (best is null)
            {
                return null;
            }

            return new
            {
                best.Name,
                best.Trades,
                best.Wins,
                best.Losses,
                best.WinRate,
                best.NetR,
                best.ExpectancyR,
                best.NetProfitLoss,
                best.AverageGapSizePoints,
                best.AverageMinutesToEntry,
                best.AverageRiskTicks
            };
        }

        // ============================================================
        // COMPACT CANDIDATE
        // ============================================================

        private static object BuildCandidateSummary(
            FvgCandidateRule rule)
        {
            return new
            {
                rule.RuleName,

                rule.EntryModel,

                rule.TargetR,

                rule.Direction,

                rule.SessionBucket,

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

                evidence =
                    new
                    {
                        rule.Trades,
                        rule.DistinctFvgs,
                        rule.Wins,
                        rule.Losses,
                        rule.WinRate,

                        rule.MinimumSampleRequired,
                        rule.MeetsMinimumSample
                    },

                performance =
                    new
                    {
                        rule.NetR,
                        rule.ExpectancyR,

                        rule.AverageWinnerR,
                        rule.AverageLoserR,

                        rule.ProfitFactorR,

                        rule.MaximumConsecutiveLosses,
                        rule.MaximumDrawdownR
                    },

                oneMes =
                    new
                    {
                        rule.RawNetProfitLoss,
                        rule.RawAverageProfitLoss
                    },

                fixedRisk25 =
                    new
                    {
                        net =
                            rule.FixedRisk25NetProfitLoss,

                        average =
                            rule.FixedRisk25AverageProfitLoss
                    },

                fixedRisk50 =
                    new
                    {
                        net =
                            rule.FixedRisk50NetProfitLoss,

                        average =
                            rule.FixedRisk50AverageProfitLoss
                    },

                research =
                    new
                    {
                        rule.Status,
                        rule.ResearchScore,
                        rule.PositiveExpectancy,
                        rule.RequiresOutOfSampleValidation,
                        rule.ResearchNotes
                    }
            };
        }

        // ============================================================
        // HUMAN-READABLE RESEARCH STATE
        // ============================================================

        private static object BuildResearchState(
            FvgCandidateDiscoveryReport discovery,
            IReadOnlyList<FvgCandidateRule> promising,
            IReadOnlyList<FvgCandidateRule> positive)
        {
            string stage;

            string message;

            if (promising.Count > 0)
            {
                stage =
                    "CandidateDiscovery";

                message =
                    "At least one in-sample rule meets the current " +
                    "promising-candidate threshold. No rule should " +
                    "be activated until it passes unseen historical " +
                    "validation data.";
            }
            else if (positive.Count > 0)
            {
                stage =
                    "Research";

                message =
                    "Positive-expectancy research candidates exist, " +
                    "but none currently meet the full promising-rule " +
                    "threshold.";
            }
            else
            {
                stage =
                    "EvidenceCollection";

                message =
                    "No sample-qualified positive-expectancy rule " +
                    "currently survives the candidate screen. More " +
                    "historical evidence is required before changing " +
                    "the strategy.";
            }

            return new
            {
                stage,

                message,

                canActivateStrategy =
                    false,

                nextRequiredStage =
                    "OutOfSampleValidation",

                discovery
                    .MinimumSampleRequired
            };
        }
    }
}