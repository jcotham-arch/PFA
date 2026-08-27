using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class HistoricalCandleRebuildService
    {
        private readonly PfaDatabase _database;
        private readonly CandleRepository _candleRepository;
        private readonly FvgDetectionService _fvgDetectionService;
        private readonly ObservationRepository _observationRepository;
        private readonly HistoricalFvgReplayService _historicalFvgReplayService;
        private readonly MesScenarioEngine _mesScenarioEngine;
        private readonly FvgFeatureAnalysisService _featureAnalysisService;
        private readonly FvgCandidateRuleDiscoveryService _candidateRuleDiscoveryService;

        public HistoricalCandleRebuildService(
            PfaDatabase database,
            CandleRepository candleRepository,
            FvgDetectionService fvgDetectionService,
            ObservationRepository observationRepository,
            HistoricalFvgReplayService historicalFvgReplayService,
            MesScenarioEngine mesScenarioEngine,
            FvgFeatureAnalysisService featureAnalysisService,
            FvgCandidateRuleDiscoveryService candidateRuleDiscoveryService)
        {
            _database =
                database;

            _candleRepository =
                candleRepository;

            _fvgDetectionService =
                fvgDetectionService;

            _observationRepository =
                observationRepository;

            _historicalFvgReplayService =
                historicalFvgReplayService;

            _mesScenarioEngine =
                mesScenarioEngine;

            _featureAnalysisService =
                featureAnalysisService;

            _candidateRuleDiscoveryService =
                candidateRuleDiscoveryService;
        }

        public async Task<HistoricalRebuildResult>
            RebuildFiveMinuteCandlesAsync(
                string symbol,
                DateTime startUtc,
                DateTime endUtc,
                CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException(
                    "Symbol is required.",
                    nameof(symbol));
            }

            if (endUtc <= startUtc)
            {
                throw new ArgumentException(
                    "End time must be after start time.");
            }

            symbol =
                symbol
                    .Trim()
                    .ToUpperInvariant();

            startUtc =
                NormalizeToMinute(
                    startUtc);

            endUtc =
                NormalizeToMinute(
                    endUtc);

            IReadOnlyList<Candle> oneMinuteCandles =
                await LoadOneMinuteCandlesAsync(
                    symbol,
                    startUtc,
                    endUtc,
                    cancellationToken);

            var grouped =
                oneMinuteCandles
                    .GroupBy(
                        candle =>
                            GetFiveMinuteBucketStart(
                                candle.OpenTimeUtc))
                    .OrderBy(
                        group =>
                            group.Key)
                    .ToList();

            var rebuiltCandles =
                new List<Candle>();

            foreach (var group in grouped)
            {
                List<Candle> minutes =
                    group
                        .OrderBy(
                            candle =>
                                candle.OpenTimeUtc)
                        .ToList();

                if (!IsCompleteFiveMinuteBucket(
                        group.Key,
                        minutes))
                {
                    continue;
                }

                Candle rebuilt =
                    new()
                    {
                        Symbol =
                            symbol,

                        Timeframe =
                            "5m",

                        OpenTimeUtc =
                            group.Key,

                        Open =
                            minutes.First().Open,

                        High =
                            minutes.Max(
                                candle =>
                                    candle.High),

                        Low =
                            minutes.Min(
                                candle =>
                                    candle.Low),

                        Close =
                            minutes.Last().Close,

                        Volume =
                            minutes.Sum(
                                candle =>
                                    candle.Volume),

                        IsClosed =
                            true
                    };

                await _candleRepository.SaveAsync(
                    rebuilt,
                    "Massive Historical Rebuild",
                    cancellationToken);

                rebuiltCandles.Add(
                    rebuilt);
            }

            var detectedFvgs =
                new List<FairValueGap>();

            var evaluatedOutcomes =
                new List<FvgOutcome>();

            var mesScenarios =
                new List<MesTradeScenario>();

            int observationsDeletedBeforeReplay =
                0;

            if (rebuiltCandles.Count > 0)
            {
                observationsDeletedBeforeReplay =
                    await _observationRepository
                        .DeleteFvgsInMarketWindowAsync(
                            symbol,
                            "5m",
                            startUtc,
                            endUtc,
                            cancellationToken);
            }

            for (int i = 2;
                 i < rebuiltCandles.Count;
                 i++)
            {
                Candle candle1 =
                    rebuiltCandles[i - 2];

                Candle candle2 =
                    rebuiltCandles[i - 1];

                Candle candle3 =
                    rebuiltCandles[i];

                bool candle2IsNext =
                    candle2.OpenTimeUtc ==
                    candle1.OpenTimeUtc.AddMinutes(5);

                bool candle3IsNext =
                    candle3.OpenTimeUtc ==
                    candle2.OpenTimeUtc.AddMinutes(5);

                if (!candle2IsNext ||
                    !candle3IsNext)
                {
                    continue;
                }

                FairValueGap? detected =
                    _fvgDetectionService.Detect(
                        candle1,
                        candle2,
                        candle3);

                if (detected is null)
                {
                    continue;
                }

                _observationRepository.SaveFvg(
                    detected);

                detectedFvgs.Add(
                    detected);

                FvgOutcome outcome =
                    _historicalFvgReplayService
                        .Evaluate(
                            detected,
                            oneMinuteCandles);

                evaluatedOutcomes.Add(
                    outcome);

                IReadOnlyList<MesTradeScenario> scenarios =
                    _mesScenarioEngine
                        .EvaluateAll(
                            detected,
                            outcome,
                            oneMinuteCandles);

                mesScenarios.AddRange(
                    scenarios);
            }

            int noEntryScenarios =
                mesScenarios.Count(
                    scenario =>
                        scenario.Status ==
                        MesScenarioStatus.NoEntry);

            int targetHitScenarios =
                mesScenarios.Count(
                    scenario =>
                        scenario.Status ==
                        MesScenarioStatus.TargetHit);

            int stopHitScenarios =
                mesScenarios.Count(
                    scenario =>
                        scenario.Status ==
                        MesScenarioStatus.StopHit);

            int ambiguousScenarios =
                mesScenarios.Count(
                    scenario =>
                        scenario.IntrabarSequenceUnknown);

            int endOfDataScenarios =
                mesScenarios.Count(
                    scenario =>
                        scenario.Status ==
                        MesScenarioStatus.EndOfData);

            int executableScenarios =
                mesScenarios.Count(
                    scenario =>
                        scenario.EntryTriggered);

            int resolvedScenarios =
                mesScenarios.Count(
                    scenario =>
                        (
                            scenario.Status ==
                                MesScenarioStatus.TargetHit ||
                            scenario.Status ==
                                MesScenarioStatus.StopHit
                        ) &&
                        !scenario.IntrabarSequenceUnknown);

            List<MesTradeScenario> oneContractResolved =
                mesScenarios
                    .Where(
                        scenario =>
                            scenario.Contracts == 1 &&
                            (
                                scenario.Status ==
                                    MesScenarioStatus.TargetHit ||
                                scenario.Status ==
                                    MesScenarioStatus.StopHit
                            ) &&
                            !scenario.IntrabarSequenceUnknown &&
                            scenario.NetProfitLoss.HasValue)
                    .ToList();

            decimal oneContractNetProfitLoss =
                oneContractResolved.Sum(
                    scenario =>
                        scenario.NetProfitLoss ?? 0m);

            int oneContractWins =
                oneContractResolved.Count(
                    scenario =>
                        scenario.WasProfitable == true);

            int oneContractLosses =
                oneContractResolved.Count(
                    scenario =>
                        scenario.WasProfitable == false);

            List<MesTradeScenario> twoContractResolved =
                mesScenarios
                    .Where(
                        scenario =>
                            scenario.Contracts == 2 &&
                            (
                                scenario.Status ==
                                    MesScenarioStatus.TargetHit ||
                                scenario.Status ==
                                    MesScenarioStatus.StopHit
                            ) &&
                            !scenario.IntrabarSequenceUnknown &&
                            scenario.NetProfitLoss.HasValue)
                    .ToList();

            decimal twoContractNetProfitLoss =
                twoContractResolved.Sum(
                    scenario =>
                        scenario.NetProfitLoss ?? 0m);

            int twoContractWins =
                twoContractResolved.Count(
                    scenario =>
                        scenario.WasProfitable == true);

            int twoContractLosses =
                twoContractResolved.Count(
                    scenario =>
                        scenario.WasProfitable == false);

            IReadOnlyList<FvgFeatureRecord> featureRecords =
                _featureAnalysisService
                    .BuildFeatureRecords(
                        detectedFvgs,
                        evaluatedOutcomes,
                        mesScenarios);

            FvgFeatureAnalysisReport featureAnalysis =
                _featureAnalysisService
                    .Analyze(
                        featureRecords);

            FvgCandidateDiscoveryReport candidateDiscovery =
                _candidateRuleDiscoveryService
                    .Discover(
                        featureRecords);

            return new HistoricalRebuildResult
            {
                Symbol =
                    symbol,

                StartUtc =
                    startUtc,

                EndUtc =
                    endUtc,

                OneMinuteCandlesLoaded =
                    oneMinuteCandles.Count,

                FiveMinuteCandlesBuilt =
                    rebuiltCandles.Count,

                FiveMinuteCandles =
                    rebuiltCandles,

                ObservationsDeletedBeforeReplay =
                    observationsDeletedBeforeReplay,

                FvgsDetected =
                    detectedFvgs.Count,

                DetectedFvgs =
                    detectedFvgs,

                OutcomesEvaluated =
                    evaluatedOutcomes.Count,

                EvaluatedOutcomes =
                    evaluatedOutcomes,

                MesScenariosEvaluated =
                    mesScenarios.Count,

                MesScenarios =
                    mesScenarios,

                ScenarioSummary =
                    new MesScenarioSummary
                    {
                        TotalScenarios =
                            mesScenarios.Count,

                        ExecutableScenarios =
                            executableScenarios,

                        ResolvedScenarios =
                            resolvedScenarios,

                        NoEntryScenarios =
                            noEntryScenarios,

                        TargetHitScenarios =
                            targetHitScenarios,

                        StopHitScenarios =
                            stopHitScenarios,

                        AmbiguousScenarios =
                            ambiguousScenarios,

                        EndOfDataScenarios =
                            endOfDataScenarios,

                        OneContractResolvedScenarios =
                            oneContractResolved.Count,

                        OneContractWins =
                            oneContractWins,

                        OneContractLosses =
                            oneContractLosses,

                        OneContractNetProfitLoss =
                            oneContractNetProfitLoss,

                        TwoContractResolvedScenarios =
                            twoContractResolved.Count,

                        TwoContractWins =
                            twoContractWins,

                        TwoContractLosses =
                            twoContractLosses,

                        TwoContractNetProfitLoss =
                            twoContractNetProfitLoss
                    },

                FeatureRecords =
                    featureRecords,

                FeatureAnalysis =
                    featureAnalysis,

                CandidateDiscovery =
                    candidateDiscovery
            };
        }

        private async Task<IReadOnlyList<Candle>>
            LoadOneMinuteCandlesAsync(
                string symbol,
                DateTime startUtc,
                DateTime endUtc,
                CancellationToken cancellationToken)
        {
            var candles =
                new List<Candle>();

            await using SqliteConnection connection =
                _database.CreateConnection();

            await connection.OpenAsync(
                cancellationToken);

            await using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText = """
                SELECT
                    Symbol,
                    Timeframe,
                    OpenTimeUtc,
                    Open,
                    High,
                    Low,
                    Close,
                    Volume,
                    IsComplete
                FROM Candles
                WHERE
                    Symbol = $symbol
                    AND Timeframe = '1m'
                    AND OpenTimeUtc >= $startUtc
                    AND OpenTimeUtc <= $endUtc
                ORDER BY OpenTimeUtc ASC;
                """;

            command.Parameters.AddWithValue(
                "$symbol",
                symbol);

            command.Parameters.AddWithValue(
                "$startUtc",
                startUtc.ToString("O"));

            command.Parameters.AddWithValue(
                "$endUtc",
                endUtc.ToString("O"));

            await using SqliteDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (await reader.ReadAsync(
                       cancellationToken))
            {
                candles.Add(
                    new Candle
                    {
                        Symbol =
                            reader.GetString(0),

                        Timeframe =
                            reader.GetString(1),

                        OpenTimeUtc =
                            DateTime.Parse(
                                reader.GetString(2),
                                null,
                                System.Globalization
                                    .DateTimeStyles
                                    .RoundtripKind),

                        Open =
                            decimal.Parse(
                                reader.GetString(3),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        High =
                            decimal.Parse(
                                reader.GetString(4),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        Low =
                            decimal.Parse(
                                reader.GetString(5),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        Close =
                            decimal.Parse(
                                reader.GetString(6),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        Volume =
                            decimal.Parse(
                                reader.GetString(7),
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),

                        IsClosed =
                            reader.GetInt32(8) == 1
                    });
            }

            return candles;
        }

        private static bool IsCompleteFiveMinuteBucket(
            DateTime bucketStartUtc,
            IReadOnlyList<Candle> candles)
        {
            if (candles.Count != 5)
            {
                return false;
            }

            for (int i = 0;
                 i < 5;
                 i++)
            {
                DateTime expectedTime =
                    bucketStartUtc.AddMinutes(i);

                if (candles[i].OpenTimeUtc !=
                    expectedTime)
                {
                    return false;
                }
            }

            return true;
        }

        private static DateTime GetFiveMinuteBucketStart(
            DateTime openTimeUtc)
        {
            openTimeUtc =
                NormalizeToMinute(
                    openTimeUtc);

            int minute =
                openTimeUtc.Minute -
                (openTimeUtc.Minute % 5);

            return new DateTime(
                openTimeUtc.Year,
                openTimeUtc.Month,
                openTimeUtc.Day,
                openTimeUtc.Hour,
                minute,
                0,
                DateTimeKind.Utc);
        }

        private static DateTime NormalizeToMinute(
            DateTime value)
        {
            if (value.Kind ==
                DateTimeKind.Unspecified)
            {
                value =
                    DateTime.SpecifyKind(
                        value,
                        DateTimeKind.Utc);
            }
            else if (value.Kind !=
                     DateTimeKind.Utc)
            {
                value =
                    value.ToUniversalTime();
            }

            return new DateTime(
                value.Year,
                value.Month,
                value.Day,
                value.Hour,
                value.Minute,
                0,
                DateTimeKind.Utc);
        }
    }

    public sealed class HistoricalRebuildResult
    {
        public string Symbol { get; set; } =
            string.Empty;

        public DateTime StartUtc { get; set; }

        public DateTime EndUtc { get; set; }

        public int OneMinuteCandlesLoaded { get; set; }

        public int FiveMinuteCandlesBuilt { get; set; }

        public IReadOnlyList<Candle> FiveMinuteCandles
        {
            get;
            set;
        } = Array.Empty<Candle>();

        public int ObservationsDeletedBeforeReplay { get; set; }

        public int FvgsDetected { get; set; }

        public IReadOnlyList<FairValueGap> DetectedFvgs
        {
            get;
            set;
        } = Array.Empty<FairValueGap>();

        public int OutcomesEvaluated { get; set; }

        public IReadOnlyList<FvgOutcome> EvaluatedOutcomes
        {
            get;
            set;
        } = Array.Empty<FvgOutcome>();

        public int MesScenariosEvaluated { get; set; }

        public IReadOnlyList<MesTradeScenario> MesScenarios
        {
            get;
            set;
        } = Array.Empty<MesTradeScenario>();

        public MesScenarioSummary ScenarioSummary
        {
            get;
            set;
        } = new();

        public IReadOnlyList<FvgFeatureRecord> FeatureRecords
        {
            get;
            set;
        } = Array.Empty<FvgFeatureRecord>();

        public FvgFeatureAnalysisReport FeatureAnalysis
        {
            get;
            set;
        } = new();

        public FvgCandidateDiscoveryReport CandidateDiscovery
        {
            get;
            set;
        } = new();
    }

    public sealed class MesScenarioSummary
    {
        public int TotalScenarios { get; set; }

        public int ExecutableScenarios { get; set; }

        public int ResolvedScenarios { get; set; }

        public int NoEntryScenarios { get; set; }

        public int TargetHitScenarios { get; set; }

        public int StopHitScenarios { get; set; }

        public int AmbiguousScenarios { get; set; }

        public int EndOfDataScenarios { get; set; }

        public int OneContractResolvedScenarios { get; set; }

        public int OneContractWins { get; set; }

        public int OneContractLosses { get; set; }

        public decimal OneContractNetProfitLoss { get; set; }

        public int TwoContractResolvedScenarios { get; set; }

        public int TwoContractWins { get; set; }

        public int TwoContractLosses { get; set; }

        public decimal TwoContractNetProfitLoss { get; set; }
    }
}