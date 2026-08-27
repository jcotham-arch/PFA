using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class FvgOutOfSampleValidationService
    {
        // ============================================================
        // INITIAL VALIDATION GATES
        //
        // These are deliberately conservative.
        //
        // Passing these gates still does NOT automatically authorize
        // live execution. It only means the candidate can advance to
        // the next research stage.
        // ============================================================

        private const int RequiredDistinctFvgs =
            20;

        private const int RequiredTradingDays =
            5;

        private const decimal RequiredMinimumExpectancyR =
            0.10m;

        private const decimal RequiredMinimumProfitFactor =
            1.25m;

        private const decimal RequiredMinimumPositiveDayPercentage =
            60m;

        private const decimal MaximumAllowedDrawdownR =
            6m;

        private const decimal FixedRisk25 =
            25m;

        private const decimal FixedRisk50 =
            50m;

        // ============================================================
        // FREEZE DISCOVERED RULE
        //
        // This converts a discovery candidate into an immutable-style
        // validation definition.
        //
        // The validation engine never optimizes these parameters.
        // ============================================================

        public FrozenFvgCandidate FreezeCandidate(
            FvgCandidateRule candidate)
        {
            if (candidate is null)
            {
                throw new ArgumentNullException(
                    nameof(candidate));
            }

            return new FrozenFvgCandidate
            {
                CandidateName =
                    candidate.RuleName,

                EntryModel =
                    candidate.EntryModel,

                TargetR =
                    candidate.TargetR,

                Direction =
                    candidate.Direction,

                SessionBucket =
                    candidate.SessionBucket,

                MinimumGapSizePoints =
                    candidate.MinimumGapSizePoints,

                MaximumGapSizePoints =
                    candidate.MaximumGapSizePoints,

                MinimumMinutesToEntry =
                    candidate.MinimumMinutesToEntry,

                MaximumMinutesToEntry =
                    candidate.MaximumMinutesToEntry,

                MinimumRiskTicks =
                    candidate.MinimumRiskTicks,

                MaximumRiskTicks =
                    candidate.MaximumRiskTicks,

                DiscoveryTrades =
                    candidate.Trades,

                DiscoveryDistinctFvgs =
                    candidate.DistinctFvgs,

                DiscoveryWinRate =
                    candidate.WinRate,

                DiscoveryExpectancyR =
                    candidate.ExpectancyR,

                DiscoveryProfitFactorR =
                    candidate.ProfitFactorR,

                DiscoveryMaximumDrawdownR =
                    candidate.MaximumDrawdownR,

                FrozenAtUtc =
                    DateTime.UtcNow,

                SourceEngineVersion =
                    candidate.EngineVersion
            };
        }

        // ============================================================
        // VALIDATE
        //
        // IMPORTANT:
        //
        // validationRecords must come from data that was NOT used to
        // discover the candidate.
        //
        // The candidate parameters are never modified here.
        // ============================================================

        public FvgOutOfSampleValidationReport Validate(
            FrozenFvgCandidate candidate,
            IReadOnlyList<FvgFeatureRecord> validationRecords,
            DateTime validationStartUtc,
            DateTime validationEndUtc)
        {
            if (candidate is null)
            {
                throw new ArgumentNullException(
                    nameof(candidate));
            }

            validationRecords ??=
                Array.Empty<FvgFeatureRecord>();

            if (validationEndUtc <=
                validationStartUtc)
            {
                throw new ArgumentException(
                    "Validation end must be after validation start.");
            }

            validationStartUtc =
                EnsureUtc(
                    validationStartUtc);

            validationEndUtc =
                EnsureUtc(
                    validationEndUtc);

            // ========================================================
            // CLEAN VALIDATION POPULATION
            // ========================================================

            List<FvgFeatureRecord> cleanPopulation =
                validationRecords
                    .Where(x =>
                        x.IncludedInLearningPopulation)
                    .Where(x =>
                        x.EntryTimeUtc >=
                        validationStartUtc)
                    .Where(x =>
                        x.EntryTimeUtc <=
                        validationEndUtc)
                    .ToList();

            // ========================================================
            // APPLY FROZEN RULE
            // ========================================================

            List<FvgFeatureRecord> matches =
                cleanPopulation
                    .Where(x =>
                        MatchesFrozenCandidate(
                            x,
                            candidate))
                    .OrderBy(x =>
                        x.EntryTimeUtc)
                    .ToList();

            int wins =
                matches.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Win);

            int losses =
                matches.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Loss);

            int distinctFvgs =
                matches
                    .Select(x =>
                        x.FvgId)
                    .Distinct()
                    .Count();

            decimal netR =
                matches.Sum(x =>
                    x.RealizedR);

            decimal expectancyR =
                matches.Count > 0
                    ? matches.Average(x =>
                        x.RealizedR)
                    : 0m;

            List<FvgFeatureRecord> winners =
                matches
                    .Where(x =>
                        x.Outcome ==
                        FvgFeatureOutcome.Win)
                    .ToList();

            List<FvgFeatureRecord> losers =
                matches
                    .Where(x =>
                        x.Outcome ==
                        FvgFeatureOutcome.Loss)
                    .ToList();

            decimal averageWinnerR =
                winners.Count > 0
                    ? winners.Average(x =>
                        x.RealizedR)
                    : 0m;

            decimal averageLoserR =
                losers.Count > 0
                    ? losers.Average(x =>
                        x.RealizedR)
                    : 0m;

            decimal grossWinningR =
                winners.Sum(x =>
                    Math.Max(
                        0m,
                        x.RealizedR));

            decimal grossLosingR =
                Math.Abs(
                    losers.Sum(x =>
                        Math.Min(
                            0m,
                            x.RealizedR)));

            decimal profitFactorR =
                grossLosingR > 0m
                    ? grossWinningR /
                      grossLosingR
                    : grossWinningR > 0m
                        ? 999m
                        : 0m;

            decimal rawOneMesNet =
                matches.Sum(x =>
                    x.NetProfitLoss);

            decimal fixedRisk25Net =
                matches.Sum(x =>
                    x.RealizedR *
                    FixedRisk25);

            decimal fixedRisk50Net =
                matches.Sum(x =>
                    x.RealizedR *
                    FixedRisk50);

            int maximumConsecutiveLosses =
                CalculateMaximumConsecutiveLosses(
                    matches);

            decimal maximumDrawdownR =
                CalculateMaximumDrawdownR(
                    matches);

            // ========================================================
            // DAILY STABILITY
            //
            // UTC day buckets for the initial implementation.
            //
            // We can later upgrade this to CME trading-session dates.
            // ========================================================

            List<FvgValidationDayResult> dailyResults =
                matches
                    .GroupBy(x =>
                        x.EntryTimeUtc.Date)
                    .OrderBy(group =>
                        group.Key)
                    .Select(group =>
                        BuildDailyResult(
                            group.Key,
                            group
                                .OrderBy(x =>
                                    x.EntryTimeUtc)
                                .ToList()))
                    .ToList();

            int positiveDays =
                dailyResults.Count(x =>
                    x.WasPositiveDay);

            int negativeDays =
                dailyResults.Count(x =>
                    !x.WasPositiveDay);

            decimal positiveDayPercentage =
                Percentage(
                    positiveDays,
                    dailyResults.Count);

            // ========================================================
            // DISCOVERY COMPARISON
            // ========================================================

            decimal expectancyRetentionPercentage =
                candidate.DiscoveryExpectancyR > 0m
                    ? Math.Round(
                        expectancyR /
                        candidate.DiscoveryExpectancyR *
                        100m,
                        2)
                    : 0m;

            decimal winRate =
                Percentage(
                    wins,
                    matches.Count);

            decimal winRateChangePercentagePoints =
                winRate -
                candidate.DiscoveryWinRate;

            // ========================================================
            // PROMOTION GATES
            // ========================================================

            bool passedSampleGate =
                distinctFvgs >=
                RequiredDistinctFvgs;

            bool passedDayCountGate =
                dailyResults.Count >=
                RequiredTradingDays;

            bool passedExpectancyGate =
                expectancyR >=
                RequiredMinimumExpectancyR;

            bool passedProfitFactorGate =
                profitFactorR >=
                RequiredMinimumProfitFactor;

            bool passedPositiveDaysGate =
                positiveDayPercentage >=
                RequiredMinimumPositiveDayPercentage;

            bool passedDrawdownGate =
                maximumDrawdownR <=
                MaximumAllowedDrawdownR;

            bool passedAllPromotionGates =
                passedSampleGate &&
                passedDayCountGate &&
                passedExpectancyGate &&
                passedProfitFactorGate &&
                passedPositiveDaysGate &&
                passedDrawdownGate;

            ValidationDecision decision =
                DetermineDecision(
                    matches.Count,
                    distinctFvgs,
                    passedSampleGate,
                    passedDayCountGate,
                    passedExpectancyGate,
                    passedProfitFactorGate,
                    passedPositiveDaysGate,
                    passedDrawdownGate,
                    passedAllPromotionGates);

            string decisionReason =
                BuildDecisionReason(
                    decision,
                    distinctFvgs,
                    dailyResults.Count,
                    expectancyR,
                    profitFactorR,
                    positiveDayPercentage,
                    maximumDrawdownR);

            return new FvgOutOfSampleValidationReport
            {
                Candidate =
                    candidate,

                ValidationStartUtc =
                    validationStartUtc,

                ValidationEndUtc =
                    validationEndUtc,

                DaysWithEligibleTrades =
                    dailyResults.Count,

                TotalValidationRecordsEvaluated =
                    cleanPopulation.Count,

                MatchingTrades =
                    matches.Count,

                DistinctFvgs =
                    distinctFvgs,

                Wins =
                    wins,

                Losses =
                    losses,

                WinRate =
                    winRate,

                NetR =
                    netR,

                ExpectancyR =
                    expectancyR,

                AverageWinnerR =
                    averageWinnerR,

                AverageLoserR =
                    averageLoserR,

                ProfitFactorR =
                    profitFactorR,

                MaximumConsecutiveLosses =
                    maximumConsecutiveLosses,

                MaximumDrawdownR =
                    maximumDrawdownR,

                RawOneMesNetProfitLoss =
                    rawOneMesNet,

                FixedRisk25NetProfitLoss =
                    fixedRisk25Net,

                FixedRisk50NetProfitLoss =
                    fixedRisk50Net,

                PositiveDays =
                    positiveDays,

                NegativeDays =
                    negativeDays,

                PositiveDayPercentage =
                    positiveDayPercentage,

                DailyResults =
                    dailyResults,

                ExpectancyRetentionPercentage =
                    expectancyRetentionPercentage,

                WinRateChangePercentagePoints =
                    winRateChangePercentagePoints,

                RequiredDistinctFvgs =
                    RequiredDistinctFvgs,

                RequiredTradingDays =
                    RequiredTradingDays,

                RequiredMinimumExpectancyR =
                    RequiredMinimumExpectancyR,

                RequiredMinimumProfitFactor =
                    RequiredMinimumProfitFactor,

                RequiredMinimumPositiveDayPercentage =
                    RequiredMinimumPositiveDayPercentage,

                MaximumAllowedDrawdownR =
                    MaximumAllowedDrawdownR,

                PassedSampleGate =
                    passedSampleGate,

                PassedDayCountGate =
                    passedDayCountGate,

                PassedExpectancyGate =
                    passedExpectancyGate,

                PassedProfitFactorGate =
                    passedProfitFactorGate,

                PassedPositiveDaysGate =
                    passedPositiveDaysGate,

                PassedDrawdownGate =
                    passedDrawdownGate,

                PassedAllPromotionGates =
                    passedAllPromotionGates,

                Decision =
                    decision,

                DecisionReason =
                    decisionReason,

                // ----------------------------------------------------
                // PASSING VALIDATION STILL DOES NOT ENABLE LIVE TRADE.
                //
                // The candidate would advance into the next controlled
                // sandbox/promotion stage.
                // ----------------------------------------------------

                CanActivateStrategy =
                    false,

                NextRequiredStage =
                    passedAllPromotionGates
                        ? "SandboxPromotionReview"
                        : "OutOfSampleValidation",

                EngineVersion =
                    "1.0.0"
            };
        }

        // ============================================================
        // APPLY FROZEN CANDIDATE
        // ============================================================

        private static bool MatchesFrozenCandidate(
            FvgFeatureRecord record,
            FrozenFvgCandidate candidate)
        {
            if (record.EntryModel !=
                candidate.EntryModel)
            {
                return false;
            }

            if (record.TargetR !=
                candidate.TargetR)
            {
                return false;
            }

            if (candidate.Direction.HasValue &&
                record.Direction !=
                candidate.Direction.Value)
            {
                return false;
            }

            if (candidate.SessionBucket.HasValue &&
                record.SessionBucket !=
                candidate.SessionBucket.Value)
            {
                return false;
            }

            if (candidate.MinimumGapSizePoints.HasValue &&
                record.GapSizePoints <
                candidate.MinimumGapSizePoints.Value)
            {
                return false;
            }

            if (candidate.MaximumGapSizePoints.HasValue &&
                record.GapSizePoints >
                candidate.MaximumGapSizePoints.Value)
            {
                return false;
            }

            if (candidate.MinimumMinutesToEntry.HasValue &&
                record.MinutesFromConfirmationToEntry <
                candidate.MinimumMinutesToEntry.Value)
            {
                return false;
            }

            if (candidate.MaximumMinutesToEntry.HasValue &&
                record.MinutesFromConfirmationToEntry >
                candidate.MaximumMinutesToEntry.Value)
            {
                return false;
            }

            if (candidate.MinimumRiskTicks.HasValue &&
                record.RiskTicks <
                candidate.MinimumRiskTicks.Value)
            {
                return false;
            }

            if (candidate.MaximumRiskTicks.HasValue &&
                record.RiskTicks >
                candidate.MaximumRiskTicks.Value)
            {
                return false;
            }

            return true;
        }

        // ============================================================
        // DAILY RESULT
        // ============================================================

        private static FvgValidationDayResult BuildDailyResult(
            DateTime dateUtc,
            IReadOnlyList<FvgFeatureRecord> trades)
        {
            int wins =
                trades.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Win);

            int losses =
                trades.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Loss);

            decimal netR =
                trades.Sum(x =>
                    x.RealizedR);

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

            decimal grossWinningR =
                winners.Sum(x =>
                    Math.Max(
                        0m,
                        x.RealizedR));

            decimal grossLosingR =
                Math.Abs(
                    losers.Sum(x =>
                        Math.Min(
                            0m,
                            x.RealizedR)));

            decimal profitFactor =
                grossLosingR > 0m
                    ? grossWinningR /
                      grossLosingR
                    : grossWinningR > 0m
                        ? 999m
                        : 0m;

            return new FvgValidationDayResult
            {
                ValidationDateUtc =
                    DateTime.SpecifyKind(
                        dateUtc,
                        DateTimeKind.Utc),

                Trades =
                    trades.Count,

                DistinctFvgs =
                    trades
                        .Select(x =>
                            x.FvgId)
                        .Distinct()
                        .Count(),

                Wins =
                    wins,

                Losses =
                    losses,

                WinRate =
                    Percentage(
                        wins,
                        trades.Count),

                NetR =
                    netR,

                ExpectancyR =
                    trades.Count > 0
                        ? netR /
                          trades.Count
                        : 0m,

                ProfitFactorR =
                    profitFactor,

                MaximumDrawdownR =
                    CalculateMaximumDrawdownR(
                        trades),

                RawOneMesNetProfitLoss =
                    trades.Sum(x =>
                        x.NetProfitLoss),

                FixedRisk25NetProfitLoss =
                    netR *
                    FixedRisk25,

                FixedRisk50NetProfitLoss =
                    netR *
                    FixedRisk50,

                WasPositiveDay =
                    netR > 0m
            };
        }

        // ============================================================
        // DECISION
        // ============================================================

        private static ValidationDecision DetermineDecision(
            int trades,
            int distinctFvgs,
            bool sampleGate,
            bool dayGate,
            bool expectancyGate,
            bool profitFactorGate,
            bool positiveDaysGate,
            bool drawdownGate,
            bool allGates)
        {
            if (trades == 0 ||
                distinctFvgs < 5)
            {
                return ValidationDecision
                    .InsufficientEvidence;
            }

            if (allGates)
            {
                return ValidationDecision
                    .PassedValidation;
            }

            if (!sampleGate ||
                !dayGate)
            {
                return ValidationDecision
                    .ContinueValidation;
            }

            if (!expectancyGate ||
                !profitFactorGate ||
                !positiveDaysGate ||
                !drawdownGate)
            {
                return ValidationDecision
                    .FailedValidation;
            }

            return ValidationDecision
                .ContinueValidation;
        }

        // ============================================================
        // DECISION REASON
        // ============================================================

        private static string BuildDecisionReason(
            ValidationDecision decision,
            int distinctFvgs,
            int tradingDays,
            decimal expectancyR,
            decimal profitFactor,
            decimal positiveDayPercentage,
            decimal maximumDrawdownR)
        {
            return decision switch
            {
                ValidationDecision.InsufficientEvidence =>
                    $"Only {distinctFvgs} independent validation FVG(s) " +
                    "have qualified. More unseen data is required.",

                ValidationDecision.ContinueValidation =>
                    $"Validation currently contains {distinctFvgs} " +
                    $"independent FVG(s) across {tradingDays} trading " +
                    "day(s). Continue collecting unseen evidence.",

                ValidationDecision.FailedValidation =>
                    $"Candidate failed one or more validation gates. " +
                    $"Expectancy={expectancyR:0.###}R, " +
                    $"profit factor={profitFactor:0.###}, " +
                    $"positive days={positiveDayPercentage:0.##}%, " +
                    $"maximum drawdown={maximumDrawdownR:0.###}R.",

                ValidationDecision.PassedValidation =>
                    $"Candidate passed all current out-of-sample gates: " +
                    $"{distinctFvgs} independent FVG(s), " +
                    $"{tradingDays} trading day(s), " +
                    $"{expectancyR:0.###}R expectancy, " +
                    $"{profitFactor:0.###} profit factor, " +
                    $"{positiveDayPercentage:0.##}% positive days, " +
                    $"{maximumDrawdownR:0.###}R maximum drawdown.",

                _ =>
                    "Validation state could not be determined."
            };
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

            foreach (FvgFeatureRecord trade in
                     trades.OrderBy(x =>
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
        // MAXIMUM DRAWDOWN
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

            foreach (FvgFeatureRecord trade in
                     trades.OrderBy(x =>
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