using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class FvgCandidateRuleDiscoveryService
    {
        private const int MinimumSampleRequired =
            5;

        private const decimal FixedRisk25 =
            25m;

        private const decimal FixedRisk50 =
            50m;

        // ============================================================
        // PUBLIC ENTRY
        // ============================================================

        public FvgCandidateDiscoveryReport Discover(
            IReadOnlyList<FvgFeatureRecord> featureRecords)
        {
            featureRecords ??=
                Array.Empty<FvgFeatureRecord>();

            List<FvgFeatureRecord> population =
                featureRecords
                    .Where(x =>
                        x.IncludedInLearningPopulation)
                    .ToList();

            var candidates =
                new List<FvgCandidateRule>();

            // ========================================================
            // BASE STRATEGY MATRIX
            //
            // Every candidate starts with:
            //
            // Entry model
            // +
            // Target R
            //
            // Optional filters are layered on top.
            // ========================================================

            MesEntryModel[] entryModels =
            {
                MesEntryModel.BoundaryTouch,
                MesEntryModel.TwentyFivePercent,
                MesEntryModel.FiftyPercent,
                MesEntryModel.SeventyFivePercent
            };

            decimal[] targetRs =
            {
                1.00m,
                1.50m,
                2.00m,
                3.00m
            };

            foreach (MesEntryModel entryModel
                     in entryModels)
            {
                foreach (decimal targetR
                         in targetRs)
                {
                    // ------------------------------------------------
                    // BASELINE RULE
                    // ------------------------------------------------

                    AddCandidate(
                        candidates,
                        population,
                        new FvgCandidateRule
                        {
                            RuleName =
                                $"{entryModel} + {targetR:0.##}R",

                            EntryModel =
                                entryModel,

                            TargetR =
                                targetR
                        });

                    // ------------------------------------------------
                    // DIRECTION
                    // ------------------------------------------------

                    foreach (FvgDirection direction
                             in Enum.GetValues<FvgDirection>())
                    {
                        AddCandidate(
                            candidates,
                            population,
                            new FvgCandidateRule
                            {
                                RuleName =
                                    $"{direction} | {entryModel} | {targetR:0.##}R",

                                EntryModel =
                                    entryModel,

                                TargetR =
                                    targetR,

                                Direction =
                                    direction
                            });
                    }

                    // ------------------------------------------------
                    // GAP SIZE
                    // ------------------------------------------------

                    AddGapRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        0m,
                        0.99m,
                        "<1 pt");

                    AddGapRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        1m,
                        1.99m,
                        "1-1.99 pts");

                    AddGapRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        2m,
                        2.99m,
                        "2-2.99 pts");

                    AddGapRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        3m,
                        4.99m,
                        "3-4.99 pts");

                    AddGapRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        5m,
                        null,
                        "5+ pts");

                    // ------------------------------------------------
                    // ENTRY DELAY
                    // ------------------------------------------------

                    AddDelayRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        0,
                        5,
                        "0-5 min");

                    AddDelayRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        6,
                        15,
                        "6-15 min");

                    AddDelayRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        16,
                        30,
                        "16-30 min");

                    AddDelayRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        31,
                        60,
                        "31-60 min");

                    AddDelayRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        61,
                        null,
                        "61+ min");

                    // ------------------------------------------------
                    // RISK TICKS
                    // ------------------------------------------------

                    AddRiskRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        1m,
                        4m,
                        "1-4 ticks");

                    AddRiskRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        5m,
                        8m,
                        "5-8 ticks");

                    AddRiskRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        9m,
                        12m,
                        "9-12 ticks");

                    AddRiskRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        13m,
                        20m,
                        "13-20 ticks");

                    AddRiskRule(
                        candidates,
                        population,
                        entryModel,
                        targetR,
                        21m,
                        null,
                        "21+ ticks");

                    // ------------------------------------------------
                    // SESSION
                    // ------------------------------------------------

                    foreach (FvgSessionBucket session
                             in Enum.GetValues<FvgSessionBucket>())
                    {
                        if (session ==
                            FvgSessionBucket.Unknown)
                        {
                            continue;
                        }

                        AddCandidate(
                            candidates,
                            population,
                            new FvgCandidateRule
                            {
                                RuleName =
                                    $"{session} | {entryModel} | {targetR:0.##}R",

                                EntryModel =
                                    entryModel,

                                TargetR =
                                    targetR,

                                SessionBucket =
                                    session
                            });
                    }
                }
            }

            // ========================================================
            // LIMITED COMBINED-FEATURE CANDIDATES
            //
            // We intentionally do NOT brute-force every possible
            // combination yet.
            //
            // These are based on the broad characteristics that the
            // first analysis showed were worth investigating.
            // ========================================================

            foreach (MesEntryModel entryModel
                     in entryModels)
            {
                foreach (decimal targetR
                         in targetRs)
                {
                    AddCandidate(
                        candidates,
                        population,
                        new FvgCandidateRule
                        {
                            RuleName =
                                $"1-1.99 pt | {entryModel} | {targetR:0.##}R",

                            EntryModel =
                                entryModel,

                            TargetR =
                                targetR,

                            MinimumGapSizePoints =
                                1m,

                            MaximumGapSizePoints =
                                1.99m
                        });

                    AddCandidate(
                        candidates,
                        population,
                        new FvgCandidateRule
                        {
                            RuleName =
                                $"61+ min | {entryModel} | {targetR:0.##}R",

                            EntryModel =
                                entryModel,

                            TargetR =
                                targetR,

                            MinimumMinutesToEntry =
                                61
                        });

                    AddCandidate(
                        candidates,
                        population,
                        new FvgCandidateRule
                        {
                            RuleName =
                                $"5-8 ticks | {entryModel} | {targetR:0.##}R",

                            EntryModel =
                                entryModel,

                            TargetR =
                                targetR,

                            MinimumRiskTicks =
                                5m,

                            MaximumRiskTicks =
                                8m
                        });

                    // ------------------------------------------------
                    // FIRST MULTI-FEATURE RESEARCH RULE
                    //
                    // This does NOT become active automatically.
                    // ------------------------------------------------

                    AddCandidate(
                        candidates,
                        population,
                        new FvgCandidateRule
                        {
                            RuleName =
                                $"Bearish | 1-1.99 pt | 5-8 ticks | {entryModel} | {targetR:0.##}R",

                            EntryModel =
                                entryModel,

                            TargetR =
                                targetR,

                            Direction =
                                FvgDirection.Bearish,

                            MinimumGapSizePoints =
                                1m,

                            MaximumGapSizePoints =
                                1.99m,

                            MinimumRiskTicks =
                                5m,

                            MaximumRiskTicks =
                                8m
                        });
                }
            }

            // ========================================================
            // REMOVE EMPTY DUPLICATES
            // ========================================================

            List<FvgCandidateRule> distinctCandidates =
                candidates
                    .Where(x =>
                        x.Trades > 0)
                    .GroupBy(x =>
                        BuildRuleSignature(x))
                    .Select(group =>
                        group
                            .OrderByDescending(x =>
                                x.ResearchScore)
                            .First())
                    .OrderByDescending(x =>
                        x.ResearchScore)
                    .ThenByDescending(x =>
                        x.DistinctFvgs)
                    .ThenByDescending(x =>
                        x.ExpectancyR)
                    .ToList();

            return new FvgCandidateDiscoveryReport
            {
                LearningRecordsEvaluated =
                    population.Count,

                DistinctFvgsEvaluated =
                    population
                        .Select(x => x.FvgId)
                        .Distinct()
                        .Count(),

                CandidateRulesTested =
                    distinctCandidates.Count,

                RulesMeetingMinimumSample =
                    distinctCandidates.Count(x =>
                        x.MeetsMinimumSample),

                PositiveExpectancyRules =
                    distinctCandidates.Count(x =>
                        x.PositiveExpectancy),

                PromisingRules =
                    distinctCandidates.Count(x =>
                        x.Status ==
                        CandidateRuleStatus.PromisingCandidate),

                MinimumSampleRequired =
                    MinimumSampleRequired,

                DatasetWarning =
                    "Current results are research candidates only. " +
                    "They were discovered on the same dataset used " +
                    "to evaluate them and require testing on unseen " +
                    "historical days before promotion.",

                RankedCandidates =
                    distinctCandidates
            };
        }

        // ============================================================
        // ADD CANDIDATE
        // ============================================================

        private static void AddCandidate(
            ICollection<FvgCandidateRule> candidates,
            IReadOnlyList<FvgFeatureRecord> population,
            FvgCandidateRule rule)
        {
            List<FvgFeatureRecord> matches =
                population
                    .Where(record =>
                        MatchesRule(
                            record,
                            rule))
                    .OrderBy(record =>
                        record.EntryTimeUtc)
                    .ToList();

            EvaluateRule(
                rule,
                matches);

            candidates.Add(
                rule);
        }

        // ============================================================
        // GAP RULE
        // ============================================================

        private static void AddGapRule(
            ICollection<FvgCandidateRule> candidates,
            IReadOnlyList<FvgFeatureRecord> population,
            MesEntryModel entryModel,
            decimal targetR,
            decimal minimum,
            decimal? maximum,
            string label)
        {
            AddCandidate(
                candidates,
                population,
                new FvgCandidateRule
                {
                    RuleName =
                        $"{label} | {entryModel} | {targetR:0.##}R",

                    EntryModel =
                        entryModel,

                    TargetR =
                        targetR,

                    MinimumGapSizePoints =
                        minimum,

                    MaximumGapSizePoints =
                        maximum
                });
        }

        // ============================================================
        // DELAY RULE
        // ============================================================

        private static void AddDelayRule(
            ICollection<FvgCandidateRule> candidates,
            IReadOnlyList<FvgFeatureRecord> population,
            MesEntryModel entryModel,
            decimal targetR,
            int minimum,
            int? maximum,
            string label)
        {
            AddCandidate(
                candidates,
                population,
                new FvgCandidateRule
                {
                    RuleName =
                        $"{label} | {entryModel} | {targetR:0.##}R",

                    EntryModel =
                        entryModel,

                    TargetR =
                        targetR,

                    MinimumMinutesToEntry =
                        minimum,

                    MaximumMinutesToEntry =
                        maximum
                });
        }

        // ============================================================
        // RISK RULE
        // ============================================================

        private static void AddRiskRule(
            ICollection<FvgCandidateRule> candidates,
            IReadOnlyList<FvgFeatureRecord> population,
            MesEntryModel entryModel,
            decimal targetR,
            decimal minimum,
            decimal? maximum,
            string label)
        {
            AddCandidate(
                candidates,
                population,
                new FvgCandidateRule
                {
                    RuleName =
                        $"{label} | {entryModel} | {targetR:0.##}R",

                    EntryModel =
                        entryModel,

                    TargetR =
                        targetR,

                    MinimumRiskTicks =
                        minimum,

                    MaximumRiskTicks =
                        maximum
                });
        }

        // ============================================================
        // RULE MATCHING
        // ============================================================

        private static bool MatchesRule(
            FvgFeatureRecord record,
            FvgCandidateRule rule)
        {
            if (record.EntryModel !=
                rule.EntryModel)
            {
                return false;
            }

            if (record.TargetR !=
                rule.TargetR)
            {
                return false;
            }

            if (rule.Direction.HasValue &&
                record.Direction !=
                rule.Direction.Value)
            {
                return false;
            }

            if (rule.SessionBucket.HasValue &&
                record.SessionBucket !=
                rule.SessionBucket.Value)
            {
                return false;
            }

            if (rule.MinimumGapSizePoints.HasValue &&
                record.GapSizePoints <
                rule.MinimumGapSizePoints.Value)
            {
                return false;
            }

            if (rule.MaximumGapSizePoints.HasValue &&
                record.GapSizePoints >
                rule.MaximumGapSizePoints.Value)
            {
                return false;
            }

            if (rule.MinimumMinutesToEntry.HasValue &&
                record.MinutesFromConfirmationToEntry <
                rule.MinimumMinutesToEntry.Value)
            {
                return false;
            }

            if (rule.MaximumMinutesToEntry.HasValue &&
                record.MinutesFromConfirmationToEntry >
                rule.MaximumMinutesToEntry.Value)
            {
                return false;
            }

            if (rule.MinimumRiskTicks.HasValue &&
                record.RiskTicks <
                rule.MinimumRiskTicks.Value)
            {
                return false;
            }

            if (rule.MaximumRiskTicks.HasValue &&
                record.RiskTicks >
                rule.MaximumRiskTicks.Value)
            {
                return false;
            }

            return true;
        }

        // ============================================================
        // EVALUATE RULE
        // ============================================================

        private static void EvaluateRule(
            FvgCandidateRule rule,
            IReadOnlyList<FvgFeatureRecord> trades)
        {
            rule.MinimumSampleRequired =
                MinimumSampleRequired;

            rule.Trades =
                trades.Count;

            rule.DistinctFvgs =
                trades
                    .Select(x => x.FvgId)
                    .Distinct()
                    .Count();

            rule.Wins =
                trades.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Win);

            rule.Losses =
                trades.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Loss);

            rule.WinRate =
                Percentage(
                    rule.Wins,
                    rule.Trades);

            rule.NetR =
                trades.Sum(x =>
                    x.RealizedR);

            rule.AverageR =
                trades.Count > 0
                    ? trades.Average(x =>
                        x.RealizedR)
                    : 0m;

            rule.ExpectancyR =
                rule.AverageR;

            List<FvgFeatureRecord> winners =
                trades
                    .Where(x =>
                        x.Outcome ==
                        FvgFeatureOutcome.Win)
                    .ToList();

            List<FvgFeatureRecord> losers =
                trades
                    .Where(x =>
                        x.Outcome ==
                        FvgFeatureOutcome.Loss)
                    .ToList();

            rule.AverageWinnerR =
                winners.Count > 0
                    ? winners.Average(x =>
                        x.RealizedR)
                    : 0m;

            rule.AverageLoserR =
                losers.Count > 0
                    ? losers.Average(x =>
                        x.RealizedR)
                    : 0m;

            rule.RawNetProfitLoss =
                trades.Sum(x =>
                    x.NetProfitLoss);

            rule.RawAverageProfitLoss =
                trades.Count > 0
                    ? trades.Average(x =>
                        x.NetProfitLoss)
                    : 0m;

            rule.FixedRisk25NetProfitLoss =
                trades.Sum(x =>
                    x.RealizedR *
                    FixedRisk25);

            rule.FixedRisk25AverageProfitLoss =
                trades.Count > 0
                    ? rule.FixedRisk25NetProfitLoss /
                      trades.Count
                    : 0m;

            rule.FixedRisk50NetProfitLoss =
                trades.Sum(x =>
                    x.RealizedR *
                    FixedRisk50);

            rule.FixedRisk50AverageProfitLoss =
                trades.Count > 0
                    ? rule.FixedRisk50NetProfitLoss /
                      trades.Count
                    : 0m;

            decimal grossWinsR =
                winners.Sum(x =>
                    Math.Max(
                        0m,
                        x.RealizedR));

            decimal grossLossesR =
                Math.Abs(
                    losers.Sum(x =>
                        Math.Min(
                            0m,
                            x.RealizedR)));

            rule.ProfitFactorR =
                grossLossesR > 0m
                    ? grossWinsR /
                      grossLossesR
                    : grossWinsR > 0m
                        ? 999m
                        : 0m;

            rule.MaximumConsecutiveLosses =
                CalculateMaximumConsecutiveLosses(
                    trades);

            rule.MaximumDrawdownR =
                CalculateMaximumDrawdownR(
                    trades);

            rule.MeetsMinimumSample =
                rule.DistinctFvgs >=
                MinimumSampleRequired;

            rule.PositiveExpectancy =
                rule.ExpectancyR > 0m;

            rule.RequiresOutOfSampleValidation =
                true;

            rule.Status =
                DetermineStatus(
                    rule);

            rule.ResearchScore =
                CalculateResearchScore(
                    rule);

            rule.ResearchNotes =
                BuildResearchNotes(
                    rule);
        }

        // ============================================================
        // STATUS
        // ============================================================

        private static CandidateRuleStatus DetermineStatus(
            FvgCandidateRule rule)
        {
            if (!rule.MeetsMinimumSample)
            {
                return CandidateRuleStatus
                    .InsufficientEvidence;
            }

            if (rule.ExpectancyR <= 0m)
            {
                return CandidateRuleStatus
                    .NegativeExpectancy;
            }

            if (rule.ExpectancyR >= 0.25m &&
                rule.WinRate >= 50m &&
                rule.ProfitFactorR >= 1.5m)
            {
                return CandidateRuleStatus
                    .PromisingCandidate;
            }

            if (rule.ExpectancyR > 0m)
            {
                return CandidateRuleStatus
                    .ResearchCandidate;
            }

            return CandidateRuleStatus
                .RequiresValidation;
        }

        // ============================================================
        // RESEARCH SCORE
        //
        // Sample size matters.
        //
        // A 1-for-1 strategy cannot outrank a well-performing strategy
        // with materially more independent observations simply because
        // its raw expectancy is larger.
        // ============================================================

        private static decimal CalculateResearchScore(
            FvgCandidateRule rule)
        {
            if (rule.Trades <= 0)
            {
                return 0m;
            }

            decimal sampleFactor =
                Math.Min(
                    1m,
                    rule.DistinctFvgs /
                    20m);

            decimal expectancyComponent =
                rule.ExpectancyR *
                40m;

            decimal winRateComponent =
                (
                    rule.WinRate /
                    100m
                ) * 25m;

            decimal profitFactorComponent =
                Math.Min(
                    rule.ProfitFactorR,
                    3m)
                / 3m *
                20m;

            decimal drawdownPenalty =
                rule.MaximumDrawdownR *
                2m;

            decimal rawScore =
                expectancyComponent +
                winRateComponent +
                profitFactorComponent -
                drawdownPenalty;

            return Math.Round(
                rawScore *
                sampleFactor,
                4);
        }

        // ============================================================
        // CONSECUTIVE LOSSES
        // ============================================================

        private static int CalculateMaximumConsecutiveLosses(
            IReadOnlyList<FvgFeatureRecord> trades)
        {
            int maximum =
                0;

            int current =
                0;

            foreach (FvgFeatureRecord trade
                     in trades.OrderBy(x =>
                         x.EntryTimeUtc))
            {
                if (trade.Outcome ==
                    FvgFeatureOutcome.Loss)
                {
                    current++;

                    maximum =
                        Math.Max(
                            maximum,
                            current);
                }
                else
                {
                    current =
                        0;
                }
            }

            return maximum;
        }

        // ============================================================
        // MAXIMUM DRAWDOWN IN R
        // ============================================================

        private static decimal CalculateMaximumDrawdownR(
            IReadOnlyList<FvgFeatureRecord> trades)
        {
            decimal equity =
                0m;

            decimal peak =
                0m;

            decimal maximumDrawdown =
                0m;

            foreach (FvgFeatureRecord trade
                     in trades.OrderBy(x =>
                         x.EntryTimeUtc))
            {
                equity +=
                    trade.RealizedR;

                peak =
                    Math.Max(
                        peak,
                        equity);

                decimal drawdown =
                    peak -
                    equity;

                maximumDrawdown =
                    Math.Max(
                        maximumDrawdown,
                        drawdown);
            }

            return maximumDrawdown;
        }

        // ============================================================
        // NOTES
        // ============================================================

        private static string BuildResearchNotes(
            FvgCandidateRule rule)
        {
            if (!rule.MeetsMinimumSample)
            {
                return
                    $"Only {rule.DistinctFvgs} independent FVG(s). " +
                    $"Minimum research threshold is " +
                    $"{rule.MinimumSampleRequired}.";
            }

            if (!rule.PositiveExpectancy)
            {
                return
                    "Current dataset shows negative expectancy.";
            }

            if (rule.Status ==
                CandidateRuleStatus.PromisingCandidate)
            {
                return
                    "Positive in-sample candidate. Must be tested " +
                    "against unseen historical days before any " +
                    "promotion or live use.";
            }

            return
                "Positive in-sample expectancy. More evidence and " +
                "out-of-sample validation required.";
        }

        // ============================================================
        // SIGNATURE
        // ============================================================

        private static string BuildRuleSignature(
            FvgCandidateRule rule)
        {
            return string.Join(
                "|",
                rule.EntryModel,
                rule.TargetR,
                rule.Direction?.ToString() ?? "*",
                rule.SessionBucket?.ToString() ?? "*",
                rule.MinimumGapSizePoints?.ToString() ?? "*",
                rule.MaximumGapSizePoints?.ToString() ?? "*",
                rule.MinimumMinutesToEntry?.ToString() ?? "*",
                rule.MaximumMinutesToEntry?.ToString() ?? "*",
                rule.MinimumRiskTicks?.ToString() ?? "*",
                rule.MaximumRiskTicks?.ToString() ?? "*");
        }

        // ============================================================
        // PERCENTAGE
        // ============================================================

        private static decimal Percentage(
            int numerator,
            int denominator)
        {
            if (denominator <= 0)
            {
                return 0m;
            }

            return Math.Round(
                (
                    (decimal)numerator /
                    denominator
                ) * 100m,
                2);
        }
    }
}