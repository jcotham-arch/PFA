using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class FvgFeatureAnalysisService
    {
        private const decimal MesTickSize =
            0.25m;

        private const decimal MesDollarsPerPointPerContract =
            5.00m;

        // ============================================================
        // BUILD LEARNING POPULATION
        //
        // IMPORTANT:
        //
        // We use ONLY 1-contract scenarios as the statistical baseline.
        //
        // 2-contract scenarios are the same underlying market event
        // with different position sizing and must not be counted as
        // independent evidence.
        // ============================================================

        public IReadOnlyList<FvgFeatureRecord> BuildFeatureRecords(
            IReadOnlyList<FairValueGap> fvgs,
            IReadOnlyList<FvgOutcome> outcomes,
            IReadOnlyList<MesTradeScenario> scenarios)
        {
            fvgs ??=
                Array.Empty<FairValueGap>();

            outcomes ??=
                Array.Empty<FvgOutcome>();

            scenarios ??=
                Array.Empty<MesTradeScenario>();

            Dictionary<Guid, FairValueGap> fvgById =
                fvgs
                    .GroupBy(x => x.Id)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First());

            Dictionary<Guid, FvgOutcome> outcomeById =
                outcomes
                    .GroupBy(x => x.OutcomeId)
                    .ToDictionary(
                        group => group.Key,
                        group => group.First());

            var records =
                new List<FvgFeatureRecord>();

            foreach (MesTradeScenario scenario in scenarios)
            {
                if (scenario.Contracts != 1)
                {
                    continue;
                }

                if (!fvgById.TryGetValue(
                        scenario.FvgId,
                        out FairValueGap? fvg))
                {
                    continue;
                }

                if (!outcomeById.TryGetValue(
                        scenario.OutcomeId,
                        out FvgOutcome? outcome))
                {
                    continue;
                }

                FvgFeatureRecord? record =
                    CreateFeatureRecord(
                        fvg,
                        outcome,
                        scenario);

                if (record is not null)
                {
                    records.Add(
                        record);
                }
            }

            return records
                .OrderBy(x => x.EntryTimeUtc)
                .ThenBy(x => x.EntryModel)
                .ThenBy(x => x.TargetR)
                .ToList();
        }

        // ============================================================
        // CREATE ONE FEATURE RECORD
        // ============================================================

        private static FvgFeatureRecord? CreateFeatureRecord(
            FairValueGap fvg,
            FvgOutcome outcome,
            MesTradeScenario scenario)
        {
            bool isResolved =
                scenario.Status ==
                    MesScenarioStatus.TargetHit ||
                scenario.Status ==
                    MesScenarioStatus.StopHit;

            bool executionKnown =
                !scenario.IntrabarSequenceUnknown;

            bool pricesValid =
                scenario.AllExecutionPricesValid;

            bool hasEntry =
                scenario.EntryTriggered &&
                scenario.EntryTimeUtc.HasValue &&
                scenario.EntryPrice.HasValue;

            bool hasStop =
                scenario.StopPrice.HasValue &&
                scenario.RiskPoints.HasValue &&
                scenario.RiskTicks.HasValue;

            bool hasTarget =
                scenario.TargetPrice.HasValue;

            bool hasOutcome =
                scenario.WasProfitable.HasValue &&
                scenario.RealizedR.HasValue &&
                scenario.GrossProfitLoss.HasValue &&
                scenario.NetProfitLoss.HasValue;

            bool include =
                isResolved &&
                executionKnown &&
                pricesValid &&
                hasEntry &&
                hasStop &&
                hasTarget &&
                hasOutcome;

            string exclusionReason =
                DetermineExclusionReason(
                    scenario,
                    isResolved,
                    executionKnown,
                    pricesValid,
                    hasEntry,
                    hasStop,
                    hasTarget,
                    hasOutcome);

            // --------------------------------------------------------
            // We still return only legitimate Win/Loss records.
            //
            // NoEntry, EndOfData, ambiguous, malformed, or incomplete
            // scenarios stay OUT of the learning population entirely.
            // --------------------------------------------------------

            if (!include)
            {
                return null;
            }

            DateTime entryTimeUtc =
                EnsureUtc(
                    scenario.EntryTimeUtc!.Value);

            DateTime confirmationTimeUtc =
                EnsureUtc(
                    outcome.ConfirmationTimeUtc);

            int minutesToEntry =
                Math.Max(
                    0,
                    (int)Math.Round(
                        (
                            entryTimeUtc -
                            confirmationTimeUtc
                        ).TotalMinutes));

            decimal riskPoints =
                scenario.RiskPoints!.Value;

            decimal riskTicks =
                scenario.RiskTicks!.Value;

            decimal grossDollarRiskOneContract =
                riskPoints *
                MesDollarsPerPointPerContract;

            return new FvgFeatureRecord
            {
                // ====================================================
                // IDENTITY
                // ====================================================

                FvgId =
                    fvg.Id,

                OutcomeId =
                    outcome.OutcomeId,

                ScenarioId =
                    scenario.ScenarioId,

                Symbol =
                    scenario.Symbol,

                Timeframe =
                    scenario.Timeframe,

                // ====================================================
                // FVG CHARACTERISTICS
                // ====================================================

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

                GapSizePoints =
                    fvg.GapSize,

                GapSizeTicks =
                    fvg.GapSize /
                    MesTickSize,

                // ====================================================
                // TIME / SESSION
                // ====================================================

                FormationHourUtc =
                    EnsureUtc(
                        fvg.FormationTimeUtc)
                    .Hour,

                FormationMinuteUtc =
                    EnsureUtc(
                        fvg.FormationTimeUtc)
                    .Minute,

                SessionBucket =
                    GetSessionBucket(
                        EnsureUtc(
                            fvg.FormationTimeUtc)),

                // ====================================================
                // ENTRY MODEL
                // ====================================================

                EntryModel =
                    scenario.EntryModel,

                TargetR =
                    scenario.TargetR,

                EffectiveTargetR =
                    scenario.EffectiveTargetR,

                // ====================================================
                // EXECUTION
                // ====================================================

                EntryTimeUtc =
                    entryTimeUtc,

                MinutesFromConfirmationToEntry =
                    minutesToEntry,

                EntryPrice =
                    scenario.EntryPrice!.Value,

                StopPrice =
                    scenario.StopPrice!.Value,

                RiskPoints =
                    riskPoints,

                RiskTicks =
                    riskTicks,

                GrossDollarRiskOneContract =
                    grossDollarRiskOneContract,

                // ====================================================
                // NORMALIZATION
                // ====================================================

                TheoreticalEntryPrice =
                    scenario.TheoreticalEntryPrice,

                EntryNormalizationPoints =
                    scenario.EntryNormalizationPoints,

                TheoreticalStopPrice =
                    scenario.TheoreticalStopPrice,

                StopNormalizationPoints =
                    scenario.StopNormalizationPoints,

                TheoreticalTargetPrice =
                    scenario.TheoreticalTargetPrice,

                TargetPrice =
                    scenario.TargetPrice!.Value,

                TargetNormalizationPoints =
                    scenario.TargetNormalizationPoints,

                // ====================================================
                // OUTCOME LABELS
                // ====================================================

                Outcome =
                    scenario.WasProfitable == true
                        ? FvgFeatureOutcome.Win
                        : FvgFeatureOutcome.Loss,

                RealizedR =
                    scenario.RealizedR!.Value,

                GrossProfitLoss =
                    scenario.GrossProfitLoss!.Value,

                NetProfitLoss =
                    scenario.NetProfitLoss!.Value,

                TargetHitTimeUtc =
                    scenario.TargetHitTimeUtc,

                StopHitTimeUtc =
                    scenario.StopHitTimeUtc,

                // ====================================================
                // POST-ENTRY DIAGNOSTICS
                //
                // These remain analysis-only fields.
                // ====================================================

                MaximumFavorableExcursionPoints =
                    scenario.MaximumFavorableExcursionPoints,

                MaximumAdverseExcursionPoints =
                    scenario.MaximumAdverseExcursionPoints,

                MaximumFavorableR =
                    scenario.MaximumFavorableR,

                MaximumAdverseR =
                    scenario.MaximumAdverseR,

                MinuteCandlesEvaluatedAfterEntry =
                    scenario.MinuteCandlesEvaluatedAfterEntry,

                // ====================================================
                // DATA QUALITY
                // ====================================================

                ExecutionPricesValid =
                    pricesValid,

                IntrabarSequenceWasKnown =
                    executionKnown,

                IncludedInLearningPopulation =
                    true,

                ExclusionReason =
                    exclusionReason,

                FeatureEngineVersion =
                    "1.0.0",

                ScenarioEngineVersion =
                    scenario.EngineVersion
            };
        }

        // ============================================================
        // ANALYZE FEATURE RECORDS
        // ============================================================

        public FvgFeatureAnalysisReport Analyze(
            IReadOnlyList<FvgFeatureRecord> records)
        {
            records ??=
                Array.Empty<FvgFeatureRecord>();

            List<FvgFeatureRecord> population =
                records
                    .Where(x =>
                        x.IncludedInLearningPopulation)
                    .ToList();

            int wins =
                population.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Win);

            int losses =
                population.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Loss);

            return new FvgFeatureAnalysisReport
            {
                TotalLearningRecords =
                    population.Count,

                Wins =
                    wins,

                Losses =
                    losses,

                WinRate =
                    CalculatePercentage(
                        wins,
                        population.Count),

                AverageRealizedR =
                    population.Count > 0
                        ? population.Average(
                            x => x.RealizedR)
                        : 0m,

                NetProfitLoss =
                    population.Sum(
                        x => x.NetProfitLoss),

                ByEntryModel =
                    BuildEntryModelAnalysis(
                        population),

                ByTargetR =
                    BuildTargetRAnalysis(
                        population),

                ByDirection =
                    BuildDirectionAnalysis(
                        population),

                BySession =
                    BuildSessionAnalysis(
                        population),

                ByGapSize =
                    BuildGapSizeAnalysis(
                        population),

                ByEntryDelay =
                    BuildEntryDelayAnalysis(
                        population),

                ByRiskTicks =
                    BuildRiskAnalysis(
                        population),

                RankedStrategies =
                    BuildRankedStrategies(
                        population)
            };
        }

        // ============================================================
        // ENTRY MODEL ANALYSIS
        // ============================================================

        private static IReadOnlyList<FvgFeatureGroupResult>
            BuildEntryModelAnalysis(
                IReadOnlyList<FvgFeatureRecord> records)
        {
            return records
                .GroupBy(x =>
                    x.EntryModel.ToString())
                .Select(group =>
                    BuildGroupResult(
                        group.Key,
                        group))
                .OrderByDescending(x =>
                    x.ExpectancyR)
                .ToList();
        }

        // ============================================================
        // TARGET R ANALYSIS
        // ============================================================

        private static IReadOnlyList<FvgFeatureGroupResult>
            BuildTargetRAnalysis(
                IReadOnlyList<FvgFeatureRecord> records)
        {
            return records
                .GroupBy(x =>
                    x.TargetR)
                .Select(group =>
                    BuildGroupResult(
                        $"{group.Key:0.##}R",
                        group))
                .OrderByDescending(x =>
                    x.ExpectancyR)
                .ToList();
        }

        // ============================================================
        // DIRECTION ANALYSIS
        // ============================================================

        private static IReadOnlyList<FvgFeatureGroupResult>
            BuildDirectionAnalysis(
                IReadOnlyList<FvgFeatureRecord> records)
        {
            return records
                .GroupBy(x =>
                    x.Direction.ToString())
                .Select(group =>
                    BuildGroupResult(
                        group.Key,
                        group))
                .OrderByDescending(x =>
                    x.ExpectancyR)
                .ToList();
        }

        // ============================================================
        // SESSION ANALYSIS
        // ============================================================

        private static IReadOnlyList<FvgFeatureGroupResult>
            BuildSessionAnalysis(
                IReadOnlyList<FvgFeatureRecord> records)
        {
            return records
                .GroupBy(x =>
                    x.SessionBucket.ToString())
                .Select(group =>
                    BuildGroupResult(
                        group.Key,
                        group))
                .OrderByDescending(x =>
                    x.ExpectancyR)
                .ToList();
        }

        // ============================================================
        // GAP SIZE ANALYSIS
        // ============================================================

        private static IReadOnlyList<FvgFeatureGroupResult>
            BuildGapSizeAnalysis(
                IReadOnlyList<FvgFeatureRecord> records)
        {
            return records
                .GroupBy(x =>
                    GetGapSizeBucket(
                        x.GapSizePoints))
                .Select(group =>
                    BuildGroupResult(
                        group.Key,
                        group))
                .OrderByDescending(x =>
                    x.ExpectancyR)
                .ToList();
        }

        // ============================================================
        // ENTRY DELAY ANALYSIS
        // ============================================================

        private static IReadOnlyList<FvgFeatureGroupResult>
            BuildEntryDelayAnalysis(
                IReadOnlyList<FvgFeatureRecord> records)
        {
            return records
                .GroupBy(x =>
                    GetEntryDelayBucket(
                        x.MinutesFromConfirmationToEntry))
                .Select(group =>
                    BuildGroupResult(
                        group.Key,
                        group))
                .OrderByDescending(x =>
                    x.ExpectancyR)
                .ToList();
        }

        // ============================================================
        // RISK ANALYSIS
        // ============================================================

        private static IReadOnlyList<FvgFeatureGroupResult>
            BuildRiskAnalysis(
                IReadOnlyList<FvgFeatureRecord> records)
        {
            return records
                .GroupBy(x =>
                    GetRiskTickBucket(
                        x.RiskTicks))
                .Select(group =>
                    BuildGroupResult(
                        group.Key,
                        group))
                .OrderByDescending(x =>
                    x.ExpectancyR)
                .ToList();
        }

        // ============================================================
        // STRATEGY COMBINATION RANKING
        //
        // This is the first truly useful table:
        //
        // entry model + target R
        //
        // We deliberately do NOT yet optimize on dozens of combined
        // features because our current sample is too small.
        // ============================================================

        private static IReadOnlyList<FvgStrategyRankResult>
            BuildRankedStrategies(
                IReadOnlyList<FvgFeatureRecord> records)
        {
            return records
                .GroupBy(x => new
                {
                    x.EntryModel,
                    x.TargetR
                })
                .Select(group =>
                {
                    int trades =
                        group.Count();

                    int wins =
                        group.Count(x =>
                            x.Outcome ==
                            FvgFeatureOutcome.Win);

                    int losses =
                        group.Count(x =>
                            x.Outcome ==
                            FvgFeatureOutcome.Loss);

                    decimal netR =
                        group.Sum(x =>
                            x.RealizedR);

                    decimal expectancyR =
                        trades > 0
                            ? netR / trades
                            : 0m;

                    return new FvgStrategyRankResult
                    {
                        EntryModel =
                            group.Key.EntryModel,

                        TargetR =
                            group.Key.TargetR,

                        Trades =
                            trades,

                        Wins =
                            wins,

                        Losses =
                            losses,

                        WinRate =
                            CalculatePercentage(
                                wins,
                                trades),

                        NetR =
                            netR,

                        ExpectancyR =
                            expectancyR,

                        NetProfitLoss =
                            group.Sum(
                                x => x.NetProfitLoss),

                        AverageMinutesToEntry =
                            trades > 0
                                ? (decimal)group.Average(
                                    x =>
                                        x.MinutesFromConfirmationToEntry)
                                : 0m,

                        AverageGapSizePoints =
                            trades > 0
                                ? group.Average(
                                    x =>
                                        x.GapSizePoints)
                                : 0m,

                        AverageRiskTicks =
                            trades > 0
                                ? group.Average(
                                    x =>
                                        x.RiskTicks)
                                : 0m
                    };
                })
                .OrderByDescending(x =>
                    x.ExpectancyR)
                .ThenByDescending(x =>
                    x.Trades)
                .ToList();
        }

        // ============================================================
        // GROUP RESULT
        // ============================================================

        private static FvgFeatureGroupResult BuildGroupResult(
            string name,
            IEnumerable<FvgFeatureRecord> source)
        {
            List<FvgFeatureRecord> records =
                source.ToList();

            int trades =
                records.Count;

            int wins =
                records.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Win);

            int losses =
                records.Count(x =>
                    x.Outcome ==
                    FvgFeatureOutcome.Loss);

            decimal netR =
                records.Sum(x =>
                    x.RealizedR);

            return new FvgFeatureGroupResult
            {
                Name =
                    name,

                Trades =
                    trades,

                Wins =
                    wins,

                Losses =
                    losses,

                WinRate =
                    CalculatePercentage(
                        wins,
                        trades),

                NetR =
                    netR,

                ExpectancyR =
                    trades > 0
                        ? netR / trades
                        : 0m,

                NetProfitLoss =
                    records.Sum(
                        x => x.NetProfitLoss),

                AverageGapSizePoints =
                    trades > 0
                        ? records.Average(
                            x =>
                                x.GapSizePoints)
                        : 0m,

                AverageMinutesToEntry =
                    trades > 0
                        ? (decimal)records.Average(
                            x =>
                                x.MinutesFromConfirmationToEntry)
                        : 0m,

                AverageRiskTicks =
                    trades > 0
                        ? records.Average(
                            x =>
                                x.RiskTicks)
                        : 0m
            };
        }

        // ============================================================
        // SESSION BUCKET
        //
        // UTC-based for now.
        //
        // We will later upgrade this to exchange/session-aware
        // timestamps instead of baking in local assumptions.
        // ============================================================

        private static FvgSessionBucket GetSessionBucket(
            DateTime utc)
        {
            int hour =
                utc.Hour;

            if (hour < 8)
            {
                return FvgSessionBucket.Overnight;
            }

            if (hour < 13)
            {
                return FvgSessionBucket.Premarket;
            }

            if (hour < 16)
            {
                return FvgSessionBucket.RegularMorning;
            }

            if (hour < 18)
            {
                return FvgSessionBucket.RegularMidday;
            }

            if (hour < 20)
            {
                return FvgSessionBucket.RegularAfternoon;
            }

            return FvgSessionBucket.PostMarket;
        }

        // ============================================================
        // GAP SIZE BUCKET
        // ============================================================

        private static string GetGapSizeBucket(
            decimal points)
        {
            if (points < 1m)
            {
                return "<1.0 pt";
            }

            if (points < 2m)
            {
                return "1.0-1.99 pts";
            }

            if (points < 3m)
            {
                return "2.0-2.99 pts";
            }

            if (points < 5m)
            {
                return "3.0-4.99 pts";
            }

            return "5.0+ pts";
        }

        // ============================================================
        // ENTRY DELAY BUCKET
        // ============================================================

        private static string GetEntryDelayBucket(
            int minutes)
        {
            if (minutes <= 5)
            {
                return "0-5 min";
            }

            if (minutes <= 15)
            {
                return "6-15 min";
            }

            if (minutes <= 30)
            {
                return "16-30 min";
            }

            if (minutes <= 60)
            {
                return "31-60 min";
            }

            return "61+ min";
        }

        // ============================================================
        // RISK TICK BUCKET
        // ============================================================

        private static string GetRiskTickBucket(
            decimal ticks)
        {
            if (ticks <= 4m)
            {
                return "1-4 ticks";
            }

            if (ticks <= 8m)
            {
                return "5-8 ticks";
            }

            if (ticks <= 12m)
            {
                return "9-12 ticks";
            }

            if (ticks <= 20m)
            {
                return "13-20 ticks";
            }

            return "21+ ticks";
        }

        // ============================================================
        // EXCLUSION REASON
        // ============================================================

        private static string DetermineExclusionReason(
            MesTradeScenario scenario,
            bool isResolved,
            bool executionKnown,
            bool pricesValid,
            bool hasEntry,
            bool hasStop,
            bool hasTarget,
            bool hasOutcome)
        {
            if (scenario.Contracts != 1)
            {
                return "Non-baseline contract quantity.";
            }

            if (scenario.Status ==
                MesScenarioStatus.NoEntry)
            {
                return "No executable entry.";
            }

            if (scenario.Status ==
                MesScenarioStatus.EndOfData)
            {
                return "Outcome unresolved at end of data.";
            }

            if (!executionKnown)
            {
                return "Intrabar execution sequence unknown.";
            }

            if (!pricesValid)
            {
                return "Invalid MES execution price.";
            }

            if (!hasEntry)
            {
                return "Missing executable entry data.";
            }

            if (!hasStop)
            {
                return "Missing stop/risk data.";
            }

            if (!hasTarget)
            {
                return "Missing target data.";
            }

            if (!hasOutcome)
            {
                return "Missing resolved outcome data.";
            }

            if (!isResolved)
            {
                return "Scenario is not resolved.";
            }

            return string.Empty;
        }

        // ============================================================
        // PERCENTAGE
        // ============================================================

        private static decimal CalculatePercentage(
            int numerator,
            int denominator)
        {
            if (denominator <= 0)
            {
                return 0m;
            }

            return
                Math.Round(
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

    // ================================================================
    // ANALYSIS REPORT
    // ================================================================

    public sealed class FvgFeatureAnalysisReport
    {
        public int TotalLearningRecords { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal WinRate { get; set; }

        public decimal AverageRealizedR { get; set; }

        public decimal NetProfitLoss { get; set; }

        public IReadOnlyList<FvgFeatureGroupResult> ByEntryModel
        {
            get;
            set;
        } = Array.Empty<FvgFeatureGroupResult>();

        public IReadOnlyList<FvgFeatureGroupResult> ByTargetR
        {
            get;
            set;
        } = Array.Empty<FvgFeatureGroupResult>();

        public IReadOnlyList<FvgFeatureGroupResult> ByDirection
        {
            get;
            set;
        } = Array.Empty<FvgFeatureGroupResult>();

        public IReadOnlyList<FvgFeatureGroupResult> BySession
        {
            get;
            set;
        } = Array.Empty<FvgFeatureGroupResult>();

        public IReadOnlyList<FvgFeatureGroupResult> ByGapSize
        {
            get;
            set;
        } = Array.Empty<FvgFeatureGroupResult>();

        public IReadOnlyList<FvgFeatureGroupResult> ByEntryDelay
        {
            get;
            set;
        } = Array.Empty<FvgFeatureGroupResult>();

        public IReadOnlyList<FvgFeatureGroupResult> ByRiskTicks
        {
            get;
            set;
        } = Array.Empty<FvgFeatureGroupResult>();

        public IReadOnlyList<FvgStrategyRankResult> RankedStrategies
        {
            get;
            set;
        } = Array.Empty<FvgStrategyRankResult>();
    }

    // ================================================================
    // GENERIC FEATURE GROUP
    // ================================================================

    public sealed class FvgFeatureGroupResult
    {
        public string Name { get; set; } =
            string.Empty;

        public int Trades { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal WinRate { get; set; }

        public decimal NetR { get; set; }

        public decimal ExpectancyR { get; set; }

        public decimal NetProfitLoss { get; set; }

        public decimal AverageGapSizePoints { get; set; }

        public decimal AverageMinutesToEntry { get; set; }

        public decimal AverageRiskTicks { get; set; }
    }

    // ================================================================
    // ENTRY + TARGET STRATEGY RANKING
    // ================================================================

    public sealed class FvgStrategyRankResult
    {
        public MesEntryModel EntryModel { get; set; }

        public decimal TargetR { get; set; }

        public int Trades { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public decimal WinRate { get; set; }

        public decimal NetR { get; set; }

        public decimal ExpectancyR { get; set; }

        public decimal NetProfitLoss { get; set; }

        public decimal AverageMinutesToEntry { get; set; }

        public decimal AverageGapSizePoints { get; set; }

        public decimal AverageRiskTicks { get; set; }
    }
}