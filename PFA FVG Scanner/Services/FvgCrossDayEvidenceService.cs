using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    // ================================================================
    // CROSS-DAY EVIDENCE STATUS
    // ================================================================

    public enum FvgCrossDayEvidenceStatus
    {
        InsufficientEvidence,

        Unstable,

        Watchlist,

        PersistentCandidate,

        PersistentNegative
    }

    // ================================================================
    // ONE DAY OF INPUT
    //
    // The controller will eventually create one of these for each
    // independent historical trading day.
    //
    // The discovery report for each day is generated independently.
    // ================================================================

    public sealed class FvgCrossDayEvidenceInput
    {
        public DateTime TradingDateUtc { get; set; }

        public string Symbol { get; set; } =
            string.Empty;

        public FvgCandidateDiscoveryReport CandidateDiscovery
        {
            get;
            set;
        } = new();
    }

    // ================================================================
    // ONE RULE ON ONE DAY
    // ================================================================

    public sealed class FvgCrossDayRuleDayResult
    {
        public DateTime TradingDateUtc { get; set; }

        public int Trades { get; set; }

        public int DistinctFvgs { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal WinRate { get; set; }

        public decimal NetR { get; set; }

        public decimal ExpectancyR { get; set; }

        public decimal ProfitFactorR { get; set; }

        public decimal MaximumDrawdownR { get; set; }

        public decimal RawNetProfitLoss { get; set; }

        public decimal FixedRisk25NetProfitLoss { get; set; }

        public decimal FixedRisk50NetProfitLoss { get; set; }

        public CandidateRuleStatus OriginalDailyStatus { get; set; }

        public bool WasPositive { get; set; }

        public bool WasNegative { get; set; }

        public bool WasFlat { get; set; }
    }

    // ================================================================
    // AGGREGATED CROSS-DAY RULE
    // ================================================================

    public sealed class FvgCrossDayRuleEvidence
    {
        // ------------------------------------------------------------
        // RULE IDENTITY
        // ------------------------------------------------------------

        public string RuleSignature { get; set; } =
            string.Empty;

        public string RuleName { get; set; } =
            string.Empty;

        public MesEntryModel EntryModel { get; set; }

        public decimal TargetR { get; set; }

        public FvgDirection? Direction { get; set; }

        public FvgSessionBucket? SessionBucket { get; set; }

        public decimal? MinimumGapSizePoints { get; set; }

        public decimal? MaximumGapSizePoints { get; set; }

        public int? MinimumMinutesToEntry { get; set; }

        public int? MaximumMinutesToEntry { get; set; }

        public decimal? MinimumRiskTicks { get; set; }

        public decimal? MaximumRiskTicks { get; set; }

        // ------------------------------------------------------------
        // DAY COVERAGE
        // ------------------------------------------------------------

        public int TotalDaysInDataset { get; set; }

        public int DaysObserved { get; set; }

        public int PositiveDays { get; set; }

        public int NegativeDays { get; set; }

        public int FlatDays { get; set; }

        public decimal PositiveDayPercentage { get; set; }

        public decimal NegativeDayPercentage { get; set; }

        // ------------------------------------------------------------
        // TOTAL SAMPLE
        // ------------------------------------------------------------

        public int TotalTrades { get; set; }

        public int TotalDistinctFvgs { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal WinRate { get; set; }

        // ------------------------------------------------------------
        // PERFORMANCE
        // ------------------------------------------------------------

        public decimal NetR { get; set; }

        public decimal ExpectancyR { get; set; }

        public decimal AverageDailyExpectancyR { get; set; }

        public decimal ProfitFactorR { get; set; }

        public decimal AverageWinnerR { get; set; }

        public decimal AverageLoserR { get; set; }

        // ------------------------------------------------------------
        // STABILITY
        // ------------------------------------------------------------

        public decimal BestDayR { get; set; }

        public decimal WorstDayR { get; set; }

        public decimal MaximumObservedDailyDrawdownR { get; set; }

        public decimal CrossDayMaximumDrawdownR { get; set; }

        public decimal ExpectancyStandardDeviation { get; set; }

        public decimal ExpectancyStabilityScore { get; set; }

        // ------------------------------------------------------------
        // CAPITAL MODELS
        // ------------------------------------------------------------

        public decimal RawNetProfitLoss { get; set; }

        public decimal FixedRisk25NetProfitLoss { get; set; }

        public decimal FixedRisk50NetProfitLoss { get; set; }

        // ------------------------------------------------------------
        // EVIDENCE GATES
        // ------------------------------------------------------------

        public int RequiredObservedDays { get; set; }

        public int RequiredDistinctFvgs { get; set; }

        public decimal RequiredPositiveDayPercentage { get; set; }

        public decimal RequiredMinimumExpectancyR { get; set; }

        public decimal RequiredMinimumProfitFactor { get; set; }

        public bool PassedDayCoverageGate { get; set; }

        public bool PassedSampleGate { get; set; }

        public bool PassedPositiveDaysGate { get; set; }

        public bool PassedExpectancyGate { get; set; }

        public bool PassedProfitFactorGate { get; set; }

        public bool PassedPersistenceGates { get; set; }

        // ------------------------------------------------------------
        // CLASSIFICATION
        // ------------------------------------------------------------

        public FvgCrossDayEvidenceStatus Status { get; set; }

        public decimal PersistenceScore { get; set; }

        public string EvidenceSummary { get; set; } =
            string.Empty;

        public bool CanAdvanceToFrozenValidation { get; set; }

        public bool CanActivateStrategy { get; set; } =
            false;

        // ------------------------------------------------------------
        // PER-DAY DETAIL
        // ------------------------------------------------------------

        public IReadOnlyList<FvgCrossDayRuleDayResult> DailyResults
        {
            get;
            set;
        } = Array.Empty<FvgCrossDayRuleDayResult>();
    }

    // ================================================================
    // COMPLETE REPORT
    // ================================================================

    public sealed class FvgCrossDayEvidenceReport
    {
        public string Symbol { get; set; } =
            string.Empty;

        public DateTime StartDateUtc { get; set; }

        public DateTime EndDateUtc { get; set; }

        public int TradingDaysEvaluated { get; set; }

        public int UniqueRulesObserved { get; set; }

        public int PersistentCandidateCount { get; set; }

        public int WatchlistCount { get; set; }

        public int UnstableCount { get; set; }

        public int PersistentNegativeCount { get; set; }

        public int InsufficientEvidenceCount { get; set; }

        public IReadOnlyList<FvgCrossDayRuleEvidence>
            PersistentCandidates
        {
            get;
            set;
        } = Array.Empty<FvgCrossDayRuleEvidence>();

        public IReadOnlyList<FvgCrossDayRuleEvidence>
            Watchlist
        {
            get;
            set;
        } = Array.Empty<FvgCrossDayRuleEvidence>();

        public IReadOnlyList<FvgCrossDayRuleEvidence>
            PersistentNegativeRules
        {
            get;
            set;
        } = Array.Empty<FvgCrossDayRuleEvidence>();

        public IReadOnlyList<FvgCrossDayRuleEvidence>
            AllRules
        {
            get;
            set;
        } = Array.Empty<FvgCrossDayRuleEvidence>();

        public string ResearchState { get; set; } =
            string.Empty;

        public bool CanActivateAnyStrategy { get; set; } =
            false;

        public string NextRequiredStage { get; set; } =
            "FrozenOutOfSampleValidation";

        public string EngineVersion { get; set; } =
            "1.0.0";
    }

    // ================================================================
    // SERVICE
    // ================================================================

    public sealed class FvgCrossDayEvidenceService
    {
        // ============================================================
        // CROSS-DAY GATES
        //
        // Discovery can still notice a pattern at 5 observations.
        //
        // Cross-day learning is intentionally harder.
        // ============================================================

        private const int RequiredObservedDays =
            3;

        private const int RequiredDistinctFvgs =
            20;

        private const decimal RequiredPositiveDayPercentage =
            60m;

        private const decimal RequiredMinimumExpectancyR =
            0.10m;

        private const decimal RequiredMinimumProfitFactor =
            1.25m;

        // ============================================================
        // ANALYZE
        // ============================================================

        public FvgCrossDayEvidenceReport Analyze(
            IReadOnlyList<FvgCrossDayEvidenceInput> days)
        {
            days ??=
                Array.Empty<FvgCrossDayEvidenceInput>();

            List<FvgCrossDayEvidenceInput> cleanDays =
                days
                    .Where(x =>
                        x.CandidateDiscovery is not null)
                    .OrderBy(x =>
                        x.TradingDateUtc)
                    .ToList();

            if (cleanDays.Count == 0)
            {
                return new FvgCrossDayEvidenceReport
                {
                    ResearchState =
                        "No cross-day evidence was supplied."
                };
            }

            string symbol =
                cleanDays
                    .Select(x =>
                        x.Symbol)
                    .FirstOrDefault(x =>
                        !string.IsNullOrWhiteSpace(x))
                ?? string.Empty;

            // ========================================================
            // COLLECT EVERY RULE FROM EVERY DAY
            // ========================================================

            var observations =
                new List<CrossDayRuleObservation>();

            foreach (FvgCrossDayEvidenceInput day
                     in cleanDays)
            {
                foreach (FvgCandidateRule rule
                         in day
                             .CandidateDiscovery
                             .RankedCandidates)
                {
                    if (rule.Trades <= 0)
                    {
                        continue;
                    }

                    observations.Add(
                        new CrossDayRuleObservation
                        {
                            TradingDateUtc =
                                EnsureUtc(
                                    day.TradingDateUtc),

                            Rule =
                                rule,

                            Signature =
                                BuildRuleSignature(
                                    rule)
                        });
                }
            }

            // ========================================================
            // GROUP IDENTICAL RULES ACROSS DAYS
            // ========================================================

            List<FvgCrossDayRuleEvidence> evidence =
                observations
                    .GroupBy(x =>
                        x.Signature)
                    .Select(group =>
                        BuildEvidence(
                            group.Key,
                            group.ToList(),
                            cleanDays.Count))
                    .OrderByDescending(x =>
                        x.PersistenceScore)
                    .ThenByDescending(x =>
                        x.DaysObserved)
                    .ThenByDescending(x =>
                        x.TotalDistinctFvgs)
                    .ThenByDescending(x =>
                        x.ExpectancyR)
                    .ToList();

            List<FvgCrossDayRuleEvidence> persistent =
                evidence
                    .Where(x =>
                        x.Status ==
                        FvgCrossDayEvidenceStatus
                            .PersistentCandidate)
                    .ToList();

            List<FvgCrossDayRuleEvidence> watchlist =
                evidence
                    .Where(x =>
                        x.Status ==
                        FvgCrossDayEvidenceStatus
                            .Watchlist)
                    .ToList();

            List<FvgCrossDayRuleEvidence> unstable =
                evidence
                    .Where(x =>
                        x.Status ==
                        FvgCrossDayEvidenceStatus
                            .Unstable)
                    .ToList();

            List<FvgCrossDayRuleEvidence> negative =
                evidence
                    .Where(x =>
                        x.Status ==
                        FvgCrossDayEvidenceStatus
                            .PersistentNegative)
                    .OrderBy(x =>
                        x.ExpectancyR)
                    .ThenByDescending(x =>
                        x.DaysObserved)
                    .ToList();

            List<FvgCrossDayRuleEvidence> insufficient =
                evidence
                    .Where(x =>
                        x.Status ==
                        FvgCrossDayEvidenceStatus
                            .InsufficientEvidence)
                    .ToList();

            DateTime startDate =
                cleanDays.Min(x =>
                    EnsureUtc(
                        x.TradingDateUtc));

            DateTime endDate =
                cleanDays.Max(x =>
                    EnsureUtc(
                        x.TradingDateUtc));

            return new FvgCrossDayEvidenceReport
            {
                Symbol =
                    symbol,

                StartDateUtc =
                    startDate,

                EndDateUtc =
                    endDate,

                TradingDaysEvaluated =
                    cleanDays.Count,

                UniqueRulesObserved =
                    evidence.Count,

                PersistentCandidateCount =
                    persistent.Count,

                WatchlistCount =
                    watchlist.Count,

                UnstableCount =
                    unstable.Count,

                PersistentNegativeCount =
                    negative.Count,

                InsufficientEvidenceCount =
                    insufficient.Count,

                PersistentCandidates =
                    persistent,

                Watchlist =
                    watchlist,

                PersistentNegativeRules =
                    negative,

                AllRules =
                    evidence,

                ResearchState =
                    BuildResearchState(
                        persistent,
                        watchlist,
                        cleanDays.Count),

                // ----------------------------------------------------
                // Cross-day evidence NEVER directly activates
                // a strategy.
                // ----------------------------------------------------

                CanActivateAnyStrategy =
                    false,

                NextRequiredStage =
                    persistent.Count > 0
                        ? "FrozenOutOfSampleValidation"
                        : "ContinueCrossDayEvidenceCollection",

                EngineVersion =
                    "1.0.0"
            };
        }

        // ============================================================
        // BUILD AGGREGATED RULE
        // ============================================================

        private static FvgCrossDayRuleEvidence BuildEvidence(
            string signature,
            IReadOnlyList<CrossDayRuleObservation> observations,
            int totalDaysInDataset)
        {
            FvgCandidateRule representative =
                observations
                    .First()
                    .Rule;

            List<FvgCrossDayRuleDayResult> dailyResults =
                observations
                    .OrderBy(x =>
                        x.TradingDateUtc)
                    .Select(x =>
                        BuildDailyResult(
                            x.TradingDateUtc,
                            x.Rule))
                    .ToList();

            int daysObserved =
                dailyResults.Count;

            int positiveDays =
                dailyResults.Count(x =>
                    x.WasPositive);

            int negativeDays =
                dailyResults.Count(x =>
                    x.WasNegative);

            int flatDays =
                dailyResults.Count(x =>
                    x.WasFlat);

            int totalTrades =
                dailyResults.Sum(x =>
                    x.Trades);

            int totalDistinctFvgs =
                dailyResults.Sum(x =>
                    x.DistinctFvgs);

            int wins =
                dailyResults.Sum(x =>
                    x.Wins);

            int losses =
                dailyResults.Sum(x =>
                    x.Losses);

            decimal netR =
                dailyResults.Sum(x =>
                    x.NetR);

            decimal expectancyR =
                totalTrades > 0
                    ? netR /
                      totalTrades
                    : 0m;

            decimal averageDailyExpectancy =
                dailyResults.Count > 0
                    ? dailyResults.Average(x =>
                        x.ExpectancyR)
                    : 0m;

            decimal positiveDayPercentage =
                Percentage(
                    positiveDays,
                    daysObserved);

            decimal negativeDayPercentage =
                Percentage(
                    negativeDays,
                    daysObserved);

            decimal grossWinningR =
                observations.Sum(x =>
                    Math.Max(
                        0m,
                        x.Rule.AverageWinnerR) *
                    x.Rule.Wins);

            decimal grossLosingR =
                observations.Sum(x =>
                    Math.Abs(
                        Math.Min(
                            0m,
                            x.Rule.AverageLoserR)) *
                    x.Rule.Losses);

            decimal profitFactor =
                grossLosingR > 0m
                    ? grossWinningR /
                      grossLosingR
                    : grossWinningR > 0m
                        ? 999m
                        : 0m;

            decimal averageWinnerR =
                wins > 0
                    ? grossWinningR /
                      wins
                    : 0m;

            decimal averageLoserR =
                losses > 0
                    ? -(grossLosingR /
                        losses)
                    : 0m;

            decimal bestDayR =
                dailyResults.Count > 0
                    ? dailyResults.Max(x =>
                        x.NetR)
                    : 0m;

            decimal worstDayR =
                dailyResults.Count > 0
                    ? dailyResults.Min(x =>
                        x.NetR)
                    : 0m;

            decimal maxObservedDailyDrawdown =
                dailyResults.Count > 0
                    ? dailyResults.Max(x =>
                        x.MaximumDrawdownR)
                    : 0m;

            decimal crossDayDrawdown =
                CalculateCrossDayMaximumDrawdownR(
                    dailyResults);

            decimal standardDeviation =
                CalculateStandardDeviation(
                    dailyResults.Select(x =>
                        x.ExpectancyR));

            decimal stabilityScore =
                CalculateStabilityScore(
                    expectancyR,
                    standardDeviation,
                    positiveDayPercentage);

            bool passedDayCoverage =
                daysObserved >=
                RequiredObservedDays;

            bool passedSample =
                totalDistinctFvgs >=
                RequiredDistinctFvgs;

            bool passedPositiveDays =
                positiveDayPercentage >=
                RequiredPositiveDayPercentage;

            bool passedExpectancy =
                expectancyR >=
                RequiredMinimumExpectancyR;

            bool passedProfitFactor =
                profitFactor >=
                RequiredMinimumProfitFactor;

            bool passedPersistence =
                passedDayCoverage &&
                passedSample &&
                passedPositiveDays &&
                passedExpectancy &&
                passedProfitFactor;

            FvgCrossDayEvidenceStatus status =
                DetermineStatus(
                    daysObserved,
                    totalDistinctFvgs,
                    positiveDayPercentage,
                    negativeDayPercentage,
                    expectancyR,
                    profitFactor,
                    passedPersistence);

            decimal persistenceScore =
                CalculatePersistenceScore(
                    daysObserved,
                    totalDaysInDataset,
                    totalDistinctFvgs,
                    positiveDayPercentage,
                    expectancyR,
                    profitFactor,
                    standardDeviation,
                    crossDayDrawdown,
                    status);

            return new FvgCrossDayRuleEvidence
            {
                RuleSignature =
                    signature,

                RuleName =
                    representative.RuleName,

                EntryModel =
                    representative.EntryModel,

                TargetR =
                    representative.TargetR,

                Direction =
                    representative.Direction,

                SessionBucket =
                    representative.SessionBucket,

                MinimumGapSizePoints =
                    representative.MinimumGapSizePoints,

                MaximumGapSizePoints =
                    representative.MaximumGapSizePoints,

                MinimumMinutesToEntry =
                    representative.MinimumMinutesToEntry,

                MaximumMinutesToEntry =
                    representative.MaximumMinutesToEntry,

                MinimumRiskTicks =
                    representative.MinimumRiskTicks,

                MaximumRiskTicks =
                    representative.MaximumRiskTicks,

                TotalDaysInDataset =
                    totalDaysInDataset,

                DaysObserved =
                    daysObserved,

                PositiveDays =
                    positiveDays,

                NegativeDays =
                    negativeDays,

                FlatDays =
                    flatDays,

                PositiveDayPercentage =
                    positiveDayPercentage,

                NegativeDayPercentage =
                    negativeDayPercentage,

                TotalTrades =
                    totalTrades,

                TotalDistinctFvgs =
                    totalDistinctFvgs,

                Wins =
                    wins,

                Losses =
                    losses,

                WinRate =
                    Percentage(
                        wins,
                        totalTrades),

                NetR =
                    netR,

                ExpectancyR =
                    expectancyR,

                AverageDailyExpectancyR =
                    averageDailyExpectancy,

                ProfitFactorR =
                    profitFactor,

                AverageWinnerR =
                    averageWinnerR,

                AverageLoserR =
                    averageLoserR,

                BestDayR =
                    bestDayR,

                WorstDayR =
                    worstDayR,

                MaximumObservedDailyDrawdownR =
                    maxObservedDailyDrawdown,

                CrossDayMaximumDrawdownR =
                    crossDayDrawdown,

                ExpectancyStandardDeviation =
                    standardDeviation,

                ExpectancyStabilityScore =
                    stabilityScore,

                RawNetProfitLoss =
                    dailyResults.Sum(x =>
                        x.RawNetProfitLoss),

                FixedRisk25NetProfitLoss =
                    dailyResults.Sum(x =>
                        x.FixedRisk25NetProfitLoss),

                FixedRisk50NetProfitLoss =
                    dailyResults.Sum(x =>
                        x.FixedRisk50NetProfitLoss),

                RequiredObservedDays =
                    RequiredObservedDays,

                RequiredDistinctFvgs =
                    RequiredDistinctFvgs,

                RequiredPositiveDayPercentage =
                    RequiredPositiveDayPercentage,

                RequiredMinimumExpectancyR =
                    RequiredMinimumExpectancyR,

                RequiredMinimumProfitFactor =
                    RequiredMinimumProfitFactor,

                PassedDayCoverageGate =
                    passedDayCoverage,

                PassedSampleGate =
                    passedSample,

                PassedPositiveDaysGate =
                    passedPositiveDays,

                PassedExpectancyGate =
                    passedExpectancy,

                PassedProfitFactorGate =
                    passedProfitFactor,

                PassedPersistenceGates =
                    passedPersistence,

                Status =
                    status,

                PersistenceScore =
                    persistenceScore,

                EvidenceSummary =
                    BuildEvidenceSummary(
                        status,
                        daysObserved,
                        totalDistinctFvgs,
                        positiveDays,
                        negativeDays,
                        positiveDayPercentage,
                        expectancyR,
                        profitFactor),

                CanAdvanceToFrozenValidation =
                    status ==
                    FvgCrossDayEvidenceStatus
                        .PersistentCandidate,

                CanActivateStrategy =
                    false,

                DailyResults =
                    dailyResults
            };
        }

        // ============================================================
        // DAILY RESULT
        // ============================================================

        private static FvgCrossDayRuleDayResult BuildDailyResult(
            DateTime dateUtc,
            FvgCandidateRule rule)
        {
            decimal netR =
                rule.NetR;

            return new FvgCrossDayRuleDayResult
            {
                TradingDateUtc =
                    EnsureUtc(
                        dateUtc),

                Trades =
                    rule.Trades,

                DistinctFvgs =
                    rule.DistinctFvgs,

                Wins =
                    rule.Wins,

                Losses =
                    rule.Losses,

                WinRate =
                    rule.WinRate,

                NetR =
                    netR,

                ExpectancyR =
                    rule.ExpectancyR,

                ProfitFactorR =
                    rule.ProfitFactorR,

                MaximumDrawdownR =
                    rule.MaximumDrawdownR,

                RawNetProfitLoss =
                    rule.RawNetProfitLoss,

                FixedRisk25NetProfitLoss =
                    rule.FixedRisk25NetProfitLoss,

                FixedRisk50NetProfitLoss =
                    rule.FixedRisk50NetProfitLoss,

                OriginalDailyStatus =
                    rule.Status,

                WasPositive =
                    netR > 0m,

                WasNegative =
                    netR < 0m,

                WasFlat =
                    netR == 0m
            };
        }

        // ============================================================
        // CLASSIFICATION
        // ============================================================

        private static FvgCrossDayEvidenceStatus DetermineStatus(
            int daysObserved,
            int distinctFvgs,
            decimal positiveDayPercentage,
            decimal negativeDayPercentage,
            decimal expectancyR,
            decimal profitFactor,
            bool passedPersistence)
        {
            if (daysObserved <
                    RequiredObservedDays ||
                distinctFvgs <
                    RequiredDistinctFvgs)
            {
                return FvgCrossDayEvidenceStatus
                    .InsufficientEvidence;
            }

            // --------------------------------------------------------
            // PERSISTENT NEGATIVE
            //
            // We deliberately make this fairly strict.
            // --------------------------------------------------------

            if (expectancyR < 0m &&
                profitFactor < 1m &&
                negativeDayPercentage >= 60m)
            {
                return FvgCrossDayEvidenceStatus
                    .PersistentNegative;
            }

            if (passedPersistence)
            {
                return FvgCrossDayEvidenceStatus
                    .PersistentCandidate;
            }

            // --------------------------------------------------------
            // WATCHLIST
            //
            // Positive overall but not sufficiently persistent yet.
            // --------------------------------------------------------

            if (expectancyR > 0m &&
                profitFactor > 1m &&
                positiveDayPercentage >= 40m)
            {
                return FvgCrossDayEvidenceStatus
                    .Watchlist;
            }

            return FvgCrossDayEvidenceStatus
                .Unstable;
        }

        // ============================================================
        // PERSISTENCE SCORE
        //
        // This is a research ranking score.
        //
        // It is NOT:
        //
        // - a probability
        // - an expected return guarantee
        // - permission to trade
        // ============================================================

        private static decimal CalculatePersistenceScore(
            int daysObserved,
            int totalDays,
            int distinctFvgs,
            decimal positiveDayPercentage,
            decimal expectancyR,
            decimal profitFactor,
            decimal expectancyStandardDeviation,
            decimal drawdownR,
            FvgCrossDayEvidenceStatus status)
        {
            if (daysObserved <= 0 ||
                totalDays <= 0)
            {
                return 0m;
            }

            decimal dayCoverage =
                Math.Min(
                    1m,
                    (decimal)daysObserved /
                    totalDays);

            decimal sampleFactor =
                Math.Min(
                    1m,
                    (decimal)distinctFvgs /
                    50m);

            decimal positiveDayFactor =
                positiveDayPercentage /
                100m;

            decimal expectancyComponent =
                expectancyR *
                35m;

            decimal profitFactorComponent =
                Math.Min(
                    profitFactor,
                    3m)
                / 3m *
                20m;

            decimal persistenceComponent =
                positiveDayFactor *
                25m;

            decimal coverageComponent =
                dayCoverage *
                10m;

            decimal volatilityPenalty =
                expectancyStandardDeviation *
                5m;

            decimal drawdownPenalty =
                drawdownR *
                1.5m;

            decimal rawScore =
                expectancyComponent +
                profitFactorComponent +
                persistenceComponent +
                coverageComponent -
                volatilityPenalty -
                drawdownPenalty;

            decimal score =
                rawScore *
                sampleFactor;

            if (status ==
                FvgCrossDayEvidenceStatus.PersistentNegative)
            {
                score =
                    Math.Min(
                        score,
                        -1m);
            }

            return Math.Round(
                score,
                4);
        }

        // ============================================================
        // STABILITY SCORE
        //
        // Higher = stronger expectancy with lower day-to-day
        // variability and better positive-day persistence.
        // ============================================================

        private static decimal CalculateStabilityScore(
            decimal expectancyR,
            decimal standardDeviation,
            decimal positiveDayPercentage)
        {
            if (expectancyR <= 0m)
            {
                return 0m;
            }

            decimal denominator =
                1m +
                standardDeviation;

            decimal score =
                expectancyR /
                denominator *
                (
                    positiveDayPercentage /
                    100m
                );

            return Math.Round(
                score,
                4);
        }

        // ============================================================
        // CROSS-DAY DRAWDOWN
        //
        // This is calculated from each day's NetR.
        //
        // It is different from the true trade-by-trade drawdown.
        // The underlying daily MaximumDrawdownR is also retained.
        // ============================================================

        private static decimal CalculateCrossDayMaximumDrawdownR(
            IReadOnlyList<FvgCrossDayRuleDayResult> days)
        {
            decimal equity =
                0m;

            decimal peak =
                0m;

            decimal maximumDrawdown =
                0m;

            foreach (FvgCrossDayRuleDayResult day
                     in days.OrderBy(x =>
                         x.TradingDateUtc))
            {
                equity +=
                    day.NetR;

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
        // STANDARD DEVIATION
        // ============================================================

        private static decimal CalculateStandardDeviation(
            IEnumerable<decimal> values)
        {
            List<decimal> data =
                values.ToList();

            if (data.Count <= 1)
            {
                return 0m;
            }

            decimal average =
                data.Average();

            decimal variance =
                data.Average(
                    value =>
                    {
                        decimal difference =
                            value -
                            average;

                        return
                            difference *
                            difference;
                    });

            double sqrt =
                Math.Sqrt(
                    (double)variance);

            return Math.Round(
                (decimal)sqrt,
                6);
        }

        // ============================================================
        // RULE SIGNATURE
        //
        // Identical strategy definitions across different days must
        // produce the exact same signature.
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
        // EVIDENCE SUMMARY
        // ============================================================

        private static string BuildEvidenceSummary(
            FvgCrossDayEvidenceStatus status,
            int daysObserved,
            int distinctFvgs,
            int positiveDays,
            int negativeDays,
            decimal positiveDayPercentage,
            decimal expectancyR,
            decimal profitFactor)
        {
            return status switch
            {
                FvgCrossDayEvidenceStatus.PersistentCandidate =>
                    $"Persistent candidate across {daysObserved} day(s) " +
                    $"and {distinctFvgs} independent FVG(s). " +
                    $"{positiveDays} positive day(s), " +
                    $"{negativeDays} negative day(s), " +
                    $"{positiveDayPercentage:0.##}% positive-day rate, " +
                    $"{expectancyR:0.###}R expectancy, " +
                    $"{profitFactor:0.###} profit factor. " +
                    "Eligible for frozen out-of-sample validation.",

                FvgCrossDayEvidenceStatus.Watchlist =>
                    $"Positive overall across {daysObserved} day(s), " +
                    $"but persistence gates are not fully satisfied. " +
                    $"{positiveDayPercentage:0.##}% positive days, " +
                    $"{expectancyR:0.###}R expectancy, " +
                    $"{profitFactor:0.###} profit factor.",

                FvgCrossDayEvidenceStatus.PersistentNegative =>
                    $"Repeated negative evidence across {daysObserved} " +
                    $"day(s) and {distinctFvgs} independent FVG(s). " +
                    $"{expectancyR:0.###}R expectancy and " +
                    $"{profitFactor:0.###} profit factor. " +
                    "Candidate avoidance behavior.",

                FvgCrossDayEvidenceStatus.Unstable =>
                    $"Sufficient observations exist, but performance " +
                    $"is not persistent across days. " +
                    $"{positiveDayPercentage:0.##}% positive days, " +
                    $"{expectancyR:0.###}R expectancy.",

                _ =>
                    $"Only {daysObserved} qualifying day(s) and " +
                    $"{distinctFvgs} independent FVG(s). " +
                    "More evidence is required."
            };
        }

        // ============================================================
        // OVERALL RESEARCH STATE
        // ============================================================

        private static string BuildResearchState(
            IReadOnlyList<FvgCrossDayRuleEvidence> persistent,
            IReadOnlyList<FvgCrossDayRuleEvidence> watchlist,
            int days)
        {
            if (persistent.Count > 0)
            {
                return
                    $"{persistent.Count} rule(s) passed the current " +
                    $"cross-day persistence gates across {days} " +
                    "trading day(s). These rules may advance to frozen " +
                    "out-of-sample validation. None are authorized for " +
                    "live activation.";
            }

            if (watchlist.Count > 0)
            {
                return
                    $"No rule passed all cross-day persistence gates. " +
                    $"{watchlist.Count} rule(s) remain on the research " +
                    $"watchlist after {days} trading day(s). Continue " +
                    "collecting independent evidence.";
            }

            return
                $"No persistent positive rule currently survives the " +
                $"cross-day evidence screen after {days} trading day(s).";
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

        // ============================================================
        // INTERNAL OBSERVATION
        // ============================================================

        private sealed class CrossDayRuleObservation
        {
            public DateTime TradingDateUtc { get; set; }

            public string Signature { get; set; } =
                string.Empty;

            public FvgCandidateRule Rule { get; set; } =
                new();
        }
    }
}