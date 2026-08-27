using Microsoft.Data.Sqlite;

namespace PFA_FVG_Scanner.Data
{
    public sealed class PfaDatabase
    {
        private readonly string _connectionString;

        public string DatabasePath { get; }

        public PfaDatabase(IWebHostEnvironment environment)
        {
            string dataDirectory =
                Path.Combine(
                    environment.ContentRootPath,
                    "Data",
                    "Database");

            Directory.CreateDirectory(dataDirectory);

            DatabasePath =
                Path.Combine(
                    dataDirectory,
                    "pfa-market-data.db");

            _connectionString =
                $"Data Source={DatabasePath}";
        }

        public async Task InitializeAsync()
        {
            await using SqliteConnection connection =
                new(_connectionString);

            await connection.OpenAsync();

            await CreateRawMarketEventsTableAsync(connection);
            await CreateCandlesTableAsync(connection);
            await CreateObservationsTableAsync(connection);
            await CreateSetupsTableAsync(connection);
            await CreateOutcomesTableAsync(connection);
            await CreateExperimentsTableAsync(connection);
            await CreateCanonicalTimelineTablesAsync(connection);
            await CreateFeatureStateTablesAsync(connection);
            await CreateUniversalPatternReferenceTablesAsync(connection);
            await CreateUniversalMarketRecordTablesAsync(connection);
            await CreateMarketSequenceTablesAsync(connection);
            await CreateStrategyRegistryTablesAsync(connection);
            await CreateGeneralResearchTablesAsync(connection);

            // Remove exact duplicate FVG observations that may already
            // exist before creating the natural-key unique index.
            await RemoveExactDuplicateFvgsAsync(connection);

            await CreateIndexesAsync(connection);
        }

        private static async Task CreateGeneralResearchTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS GeneralResearchRuns
                (
                    ResearchRunId TEXT PRIMARY KEY,
                    ResearchEngineVersion TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    DatasetId TEXT NOT NULL,
                    DatasetManifestJson TEXT NOT NULL,
                    SearchSpaceId TEXT NOT NULL,
                    SearchSpaceVersion TEXT NOT NULL,
                    SearchSpaceJson TEXT NOT NULL,
                    DeclaredCandidateCount INTEGER NOT NULL,
                    MultipleComparisonMethod TEXT NOT NULL,
                    RandomSeed INTEGER,
                    PopulationJson TEXT NOT NULL,
                    InputManifestJson TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    CompletedAtUtc TEXT,
                    FailureReason TEXT,
                    CanActivateStrategy INTEGER NOT NULL CHECK (CanActivateStrategy = 0)
                );
                CREATE TABLE IF NOT EXISTS GeneralResearchHypotheses
                (
                    HypothesisId TEXT NOT NULL,
                    ResearchRunId TEXT NOT NULL,
                    Signature TEXT NOT NULL,
                    FamilyId TEXT NOT NULL,
                    DefinitionJson TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    SampleSize INTEGER NOT NULL,
                    IndependentEvents INTEGER NOT NULL,
                    MetricsJson TEXT NOT NULL,
                    SourceReference TEXT NOT NULL,
                    CanActivateStrategy INTEGER NOT NULL CHECK (CanActivateStrategy = 0),
                    PRIMARY KEY (ResearchRunId, HypothesisId),
                    UNIQUE (ResearchRunId, Signature),
                    FOREIGN KEY (ResearchRunId) REFERENCES GeneralResearchRuns(ResearchRunId)
                );
                INSERT OR IGNORE INTO CanonicalMigrationJournal
                    (MigrationId, AppliedAtUtc, Description)
                VALUES ('PHASE11_GENERAL_RESEARCH_1', datetime('now'),
                    'Add immutable reproducible research runs and complete hypothesis search-space retention.');
                CREATE INDEX IF NOT EXISTS IX_GeneralResearchRuns_Dataset
                    ON GeneralResearchRuns (DatasetId, CreatedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_GeneralResearchHypotheses_Status
                    ON GeneralResearchHypotheses (ResearchRunId, Status);
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateStrategyRegistryTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS StrategyDefinitions
                (
                    StrategyId TEXT NOT NULL,
                    StrategyVersion TEXT NOT NULL,
                    FamilyId TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    Environment TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    DefinitionJson TEXT NOT NULL,
                    EngineManifestJson TEXT NOT NULL,
                    DiscoveryDatasetId TEXT NOT NULL,
                    ValidationDatasetId TEXT NOT NULL,
                    Author TEXT NOT NULL,
                    CompatibilitySource TEXT,
                    CreatedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (StrategyId, StrategyVersion)
                );
                CREATE TABLE IF NOT EXISTS StrategyRequirements
                (
                    StrategyId TEXT NOT NULL,
                    StrategyVersion TEXT NOT NULL,
                    RequirementType TEXT NOT NULL,
                    ReferenceId TEXT NOT NULL,
                    ReferenceVersion TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    IsRequired INTEGER NOT NULL,
                    PRIMARY KEY (StrategyId, StrategyVersion, RequirementType, ReferenceId, Role),
                    FOREIGN KEY (StrategyId, StrategyVersion)
                        REFERENCES StrategyDefinitions(StrategyId, StrategyVersion)
                );
                CREATE TABLE IF NOT EXISTS StrategyEvidenceLinks
                (
                    StrategyId TEXT NOT NULL,
                    StrategyVersion TEXT NOT NULL,
                    EvidenceType TEXT NOT NULL,
                    EvidenceId TEXT NOT NULL,
                    DatasetId TEXT NOT NULL,
                    KnownAtUtc TEXT NOT NULL,
                    PRIMARY KEY (StrategyId, StrategyVersion, EvidenceType, EvidenceId),
                    FOREIGN KEY (StrategyId, StrategyVersion)
                        REFERENCES StrategyDefinitions(StrategyId, StrategyVersion)
                );
                CREATE TABLE IF NOT EXISTS StrategyLifecycleEvents
                (
                    LifecycleEventId TEXT PRIMARY KEY,
                    StrategyId TEXT NOT NULL,
                    StrategyVersion TEXT NOT NULL,
                    FromStatus TEXT,
                    ToStatus TEXT NOT NULL,
                    Reason TEXT NOT NULL,
                    Actor TEXT NOT NULL,
                    OccurredAtUtc TEXT NOT NULL,
                    FOREIGN KEY (StrategyId, StrategyVersion)
                        REFERENCES StrategyDefinitions(StrategyId, StrategyVersion)
                );
                INSERT OR IGNORE INTO CanonicalMigrationJournal
                    (MigrationId, AppliedAtUtc, Description)
                VALUES ('PHASE10_STRATEGY_REGISTRY_1', datetime('now'),
                    'Add immutable strategy versions, requirements, evidence links and guarded lifecycle history.');
                CREATE INDEX IF NOT EXISTS IX_StrategyDefinitions_Family
                    ON StrategyDefinitions (FamilyId, StrategyId, StrategyVersion);
                CREATE INDEX IF NOT EXISTS IX_StrategyLifecycle_StrategyTime
                    ON StrategyLifecycleEvents (StrategyId, StrategyVersion, OccurredAtUtc);
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateMarketSequenceTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS MarketSequenceDefinitions
                (
                    SequenceDefinitionId TEXT NOT NULL,
                    Version TEXT NOT NULL,
                    DisplayName TEXT NOT NULL,
                    MaximumTransitionSeconds INTEGER NOT NULL,
                    RequireSameDirection INTEGER NOT NULL,
                    DefinitionJson TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (SequenceDefinitionId, Version)
                );
                CREATE TABLE IF NOT EXISTS MarketSequenceInstances
                (
                    SequenceInstanceId TEXT PRIMARY KEY,
                    SequenceDefinitionId TEXT NOT NULL,
                    SequenceDefinitionVersion TEXT NOT NULL,
                    InstrumentId TEXT NOT NULL,
                    ContractId TEXT,
                    Timeframe TEXT NOT NULL,
                    TradingSessionId TEXT NOT NULL,
                    TradingDate TEXT NOT NULL,
                    State TEXT NOT NULL,
                    CurrentStageIndex INTEGER NOT NULL,
                    StartedAtUtc TEXT NOT NULL,
                    UpdatedAtUtc TEXT NOT NULL,
                    PointInTimeConfidence TEXT NOT NULL,
                    TerminationReason TEXT,
                    CreatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (SequenceDefinitionId, SequenceDefinitionVersion)
                        REFERENCES MarketSequenceDefinitions(SequenceDefinitionId, Version)
                );
                CREATE TABLE IF NOT EXISTS MarketSequenceMembers
                (
                    SequenceInstanceId TEXT NOT NULL,
                    ObservationId TEXT NOT NULL,
                    ObservationRevision INTEGER NOT NULL,
                    Role TEXT NOT NULL,
                    Ordinal INTEGER NOT NULL,
                    JoinedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (SequenceInstanceId, Ordinal),
                    FOREIGN KEY (SequenceInstanceId) REFERENCES MarketSequenceInstances(SequenceInstanceId)
                );
                CREATE TABLE IF NOT EXISTS MarketSequenceTransitions
                (
                    SequenceInstanceId TEXT NOT NULL,
                    Ordinal INTEGER NOT NULL,
                    FromRole TEXT NOT NULL,
                    ToRole TEXT NOT NULL,
                    OccurredAtUtc TEXT NOT NULL,
                    DurationMilliseconds INTEGER NOT NULL,
                    PointInTimeConfidence TEXT NOT NULL,
                    PRIMARY KEY (SequenceInstanceId, Ordinal),
                    FOREIGN KEY (SequenceInstanceId) REFERENCES MarketSequenceInstances(SequenceInstanceId)
                );
                INSERT OR IGNORE INTO CanonicalMigrationJournal
                    (MigrationId, AppliedAtUtc, Description)
                VALUES ('PHASE7_SEQUENCE_INTELLIGENCE_1', datetime('now'),
                    'Add immutable sequence definitions, instances, members and ordered transitions.');
                CREATE INDEX IF NOT EXISTS IX_MarketSequences_Instrument_Date
                    ON MarketSequenceInstances (InstrumentId, TradingDate, StartedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_MarketSequenceMembers_Observation
                    ON MarketSequenceMembers (ObservationId, ObservationRevision);
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateUniversalMarketRecordTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS UniversalMarketObservations
                (
                    ObservationId TEXT NOT NULL,
                    Revision INTEGER NOT NULL,
                    ModuleId TEXT NOT NULL,
                    ModuleVersion TEXT NOT NULL,
                    PatternType TEXT NOT NULL,
                    InstrumentId TEXT NOT NULL,
                    ContractId TEXT,
                    Timeframe TEXT NOT NULL,
                    Direction TEXT NOT NULL,
                    FormationTimeUtc TEXT NOT NULL,
                    KnownAtUtc TEXT NOT NULL,
                    LifecycleState TEXT NOT NULL,
                    PayloadSchema TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    SourceReferencesJson TEXT NOT NULL,
                    QualityFlags INTEGER NOT NULL,
                    ContentHash TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (ObservationId, Revision)
                );
                CREATE TABLE IF NOT EXISTS UniversalObservationLifecycleEvents
                (
                    LifecycleEventId TEXT PRIMARY KEY,
                    ObservationId TEXT NOT NULL,
                    ObservationRevision INTEGER NOT NULL,
                    LifecycleState TEXT NOT NULL,
                    OccurredAtUtc TEXT NOT NULL,
                    Reason TEXT NOT NULL,
                    FOREIGN KEY (ObservationId, ObservationRevision)
                        REFERENCES UniversalMarketObservations(ObservationId, Revision)
                );
                CREATE TABLE IF NOT EXISTS UniversalMarketOutcomes
                (
                    OutcomeId TEXT PRIMARY KEY,
                    ObservationId TEXT NOT NULL,
                    OutcomeVersion TEXT NOT NULL,
                    EvaluatedThroughUtc TEXT NOT NULL,
                    SamplesEvaluated INTEGER NOT NULL,
                    PayloadSchema TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    QualityFlags INTEGER NOT NULL,
                    CreatedAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS UniversalOutcomeMetrics
                (
                    OutcomeId TEXT NOT NULL,
                    MetricName TEXT NOT NULL,
                    HorizonMinutes INTEGER NOT NULL,
                    Value TEXT NOT NULL,
                    Unit TEXT NOT NULL,
                    MeasuredAtUtc TEXT,
                    PRIMARY KEY (OutcomeId, MetricName, HorizonMinutes),
                    FOREIGN KEY (OutcomeId) REFERENCES UniversalMarketOutcomes(OutcomeId)
                );
                CREATE TABLE IF NOT EXISTS UniversalOutcomeEvents
                (
                    OutcomeEventId TEXT PRIMARY KEY,
                    OutcomeId TEXT NOT NULL,
                    ObservationId TEXT NOT NULL,
                    EventType TEXT NOT NULL,
                    OccurredAtUtc TEXT NOT NULL,
                    Ordinal INTEGER NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    FOREIGN KEY (OutcomeId) REFERENCES UniversalMarketOutcomes(OutcomeId)
                );
                CREATE TABLE IF NOT EXISTS UniversalObservationRelationships
                (
                    RelationshipId TEXT PRIMARY KEY,
                    FromObservationId TEXT NOT NULL,
                    ToObservationId TEXT NOT NULL,
                    RelationshipType TEXT NOT NULL,
                    KnownAtUtc TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL
                );
                INSERT OR IGNORE INTO UniversalMarketObservations
                    (ObservationId, Revision, ModuleId, ModuleVersion, PatternType, InstrumentId,
                     ContractId, Timeframe, Direction, FormationTimeUtc, KnownAtUtc, LifecycleState,
                     PayloadSchema, PayloadJson, SourceReferencesJson, QualityFlags, ContentHash, CreatedAtUtc)
                SELECT ObservationId, 1, 'fvg', 'legacy-1.0.0', 'FairValueGap', Symbol,
                       NULL, Timeframe, Direction, MarketTimeUtc, MarketTimeUtc, 'Detected',
                       'pfa.fvg.observation/legacy', COALESCE(MetadataJson, '{}'), '[]', 0,
                       'legacy:' || ObservationId, CreatedAtUtc
                FROM Observations WHERE ObservationType = 'FVG';
                INSERT OR IGNORE INTO UniversalObservationLifecycleEvents
                    (LifecycleEventId, ObservationId, ObservationRevision, LifecycleState, OccurredAtUtc, Reason)
                SELECT 'legacy-' || ObservationId, ObservationId, 1, 'Detected', MarketTimeUtc,
                       'phase6-legacy-backfill'
                FROM Observations WHERE ObservationType = 'FVG';
                INSERT OR IGNORE INTO CanonicalMigrationJournal
                    (MigrationId, AppliedAtUtc, Description)
                VALUES ('PHASE6_UNIVERSAL_MARKET_RECORDS_1', datetime('now'),
                    'Add immutable universal observations, outcomes, metrics, chronology, lifecycle and relationships.');
                CREATE INDEX IF NOT EXISTS IX_UniversalObservations_Module_Time
                    ON UniversalMarketObservations (ModuleId, InstrumentId, FormationTimeUtc);
                CREATE INDEX IF NOT EXISTS IX_UniversalOutcomes_Observation
                    ON UniversalMarketOutcomes (ObservationId, EvaluatedThroughUtc);
                CREATE UNIQUE INDEX IF NOT EXISTS UX_UniversalOutcomeEvents_Order
                    ON UniversalOutcomeEvents (OutcomeId, Ordinal);
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateUniversalPatternReferenceTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS UniversalPatternObservationReferences
                (
                    PatternObservationId TEXT PRIMARY KEY,
                    ModuleId TEXT NOT NULL,
                    ModuleVersion TEXT NOT NULL,
                    PatternType TEXT NOT NULL,
                    LegacyObservationId TEXT NOT NULL UNIQUE,
                    CreatedAtUtc TEXT NOT NULL,
                    FOREIGN KEY (LegacyObservationId) REFERENCES Observations(ObservationId)
                );
                INSERT OR IGNORE INTO UniversalPatternObservationReferences
                    (PatternObservationId, ModuleId, ModuleVersion, PatternType,
                     LegacyObservationId, CreatedAtUtc)
                SELECT ObservationId, 'fvg', 'legacy-1.0.0', 'FairValueGap',
                       ObservationId, datetime('now')
                FROM Observations
                WHERE ObservationType = 'FVG';
                INSERT OR IGNORE INTO CanonicalMigrationJournal
                    (MigrationId, AppliedAtUtc, Description)
                VALUES ('PHASE5_FVG_PATTERN_MODULE_1', datetime('now'),
                    'Additive universal pattern references for preserved legacy FVG observations.');
                CREATE INDEX IF NOT EXISTS IX_UniversalPatternReferences_Module
                    ON UniversalPatternObservationReferences (ModuleId, ModuleVersion);
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateFeatureStateTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS FeatureDefinitions
                (
                    FeatureDefinitionId TEXT NOT NULL,
                    Version TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    ValueType TEXT NOT NULL,
                    Unit TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    InputRequirement TEXT NOT NULL,
                    LookbackTicks INTEGER NOT NULL,
                    Description TEXT NOT NULL,
                    PRIMARY KEY (FeatureDefinitionId, Version)
                );
                CREATE TABLE IF NOT EXISTS MarketStateSnapshots
                (
                    SnapshotId TEXT PRIMARY KEY,
                    InstrumentId TEXT NOT NULL,
                    ContractId TEXT,
                    AsOfUtc TEXT NOT NULL,
                    KnownAtUtc TEXT NOT NULL,
                    DataRevision TEXT NOT NULL,
                    EngineVersion TEXT NOT NULL,
                    TradingSessionId TEXT NOT NULL,
                    QualityFlags INTEGER NOT NULL,
                    SourceReferencesJson TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS FeatureValues
                (
                    FeatureValueId TEXT PRIMARY KEY,
                    SnapshotId TEXT NOT NULL,
                    FeatureDefinitionId TEXT NOT NULL,
                    FeatureDefinitionVersion TEXT NOT NULL,
                    SubjectId TEXT NOT NULL,
                    InstrumentId TEXT NOT NULL,
                    AsOfUtc TEXT NOT NULL,
                    KnownAtUtc TEXT NOT NULL,
                    Value TEXT NOT NULL,
                    EngineVersion TEXT NOT NULL,
                    DataRevision TEXT NOT NULL,
                    QualityFlags INTEGER NOT NULL,
                    SourceReferencesJson TEXT NOT NULL,
                    FOREIGN KEY (SnapshotId) REFERENCES MarketStateSnapshots(SnapshotId),
                    FOREIGN KEY (FeatureDefinitionId, FeatureDefinitionVersion)
                        REFERENCES FeatureDefinitions(FeatureDefinitionId, Version)
                );
                INSERT OR IGNORE INTO CanonicalMigrationJournal
                    (MigrationId, AppliedAtUtc, Description)
                VALUES ('PHASE3_FEATURE_STATE_1', datetime('now'),
                    'Additive versioned feature definitions, values and immutable market-state snapshots.');
                CREATE INDEX IF NOT EXISTS IX_FeatureValues_Instrument_KnownAt
                    ON FeatureValues (InstrumentId, KnownAtUtc, FeatureDefinitionId);
                CREATE INDEX IF NOT EXISTS IX_MarketStateSnapshots_Instrument_AsOf
                    ON MarketStateSnapshots (InstrumentId, AsOfUtc);
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateCanonicalTimelineTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS CanonicalBars
                (
                    CanonicalBarId TEXT NOT NULL,
                    Revision INTEGER NOT NULL,
                    InstrumentId TEXT NOT NULL,
                    ContractId TEXT,
                    ProviderSymbol TEXT NOT NULL,
                    Timeframe TEXT NOT NULL,
                    OpenTimeUtc TEXT NOT NULL,
                    CloseTimeUtc TEXT NOT NULL,
                    Open TEXT NOT NULL,
                    High TEXT NOT NULL,
                    Low TEXT NOT NULL,
                    Close TEXT NOT NULL,
                    Volume TEXT NOT NULL,
                    IsComplete INTEGER NOT NULL,
                    TradingSessionId TEXT NOT NULL,
                    TradingDate TEXT NOT NULL,
                    CanonicalizationVersion TEXT NOT NULL,
                    TransformationVersion TEXT NOT NULL,
                    CorrectionState TEXT NOT NULL,
                    QualityFlags INTEGER NOT NULL,
                    RevisionEffectiveUtc TEXT NOT NULL,
                    ContentHash TEXT NOT NULL,
                    PRIMARY KEY (CanonicalBarId, Revision)
                );

                CREATE TABLE IF NOT EXISTS CanonicalBarSources
                (
                    SourceId TEXT PRIMARY KEY,
                    CanonicalBarId TEXT NOT NULL,
                    Revision INTEGER NOT NULL,
                    Provider TEXT NOT NULL,
                    ProviderSymbol TEXT NOT NULL,
                    SourceEventType TEXT NOT NULL,
                    SourceResolution TEXT NOT NULL,
                    SourceTimestampUtc TEXT NOT NULL,
                    ReceivedTimestampUtc TEXT NOT NULL,
                    SourceVersion TEXT NOT NULL,
                    IngestionRunId TEXT NOT NULL,
                    RawReference TEXT,
                    FOREIGN KEY (CanonicalBarId, Revision)
                        REFERENCES CanonicalBars(CanonicalBarId, Revision)
                );

                CREATE TABLE IF NOT EXISTS CanonicalMigrationJournal
                (
                    MigrationId TEXT PRIMARY KEY,
                    AppliedAtUtc TEXT NOT NULL,
                    Description TEXT NOT NULL
                );

                INSERT OR IGNORE INTO CanonicalMigrationJournal
                    (MigrationId, AppliedAtUtc, Description)
                VALUES
                    ('PHASE2_CANONICAL_TIMELINE_1', datetime('now'),
                     'Additive canonical bars, revisions, lineage, provenance and quality flags.');

                CREATE INDEX IF NOT EXISTS IX_CanonicalBars_Instrument_Time
                    ON CanonicalBars (InstrumentId, Timeframe, OpenTimeUtc, Revision);
                CREATE INDEX IF NOT EXISTS IX_CanonicalBarSources_Bar
                    ON CanonicalBarSources (CanonicalBarId, Revision);
                """;
            await ExecuteAsync(connection, sql);
        }

        public SqliteConnection CreateConnection()
        {
            return new SqliteConnection(_connectionString);
        }

        private static async Task CreateRawMarketEventsTableAsync(
            SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS RawMarketEvents
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,

                    Provider TEXT NOT NULL,
                    Symbol TEXT,
                    EventType TEXT,

                    MarketTimestampUtc TEXT,
                    ReceivedTimestampUtc TEXT NOT NULL,

                    LatencyMilliseconds INTEGER,

                    RawPayload TEXT NOT NULL,

                    CreatedAtUtc TEXT NOT NULL
                );
                """;

            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateCandlesTableAsync(
            SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS Candles
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,

                    Symbol TEXT NOT NULL,
                    Timeframe TEXT NOT NULL,

                    OpenTimeUtc TEXT NOT NULL,
                    CloseTimeUtc TEXT,

                    Open TEXT NOT NULL,
                    High TEXT NOT NULL,
                    Low TEXT NOT NULL,
                    Close TEXT NOT NULL,

                    Volume TEXT NOT NULL,

                    Provider TEXT NOT NULL,

                    IsComplete INTEGER NOT NULL,

                    SourceVersion TEXT NOT NULL,

                    CreatedAtUtc TEXT NOT NULL,

                    UNIQUE
                    (
                        Symbol,
                        Timeframe,
                        OpenTimeUtc,
                        Provider
                    )
                );
                """;

            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateObservationsTableAsync(
            SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS Observations
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,

                    ObservationId TEXT NOT NULL UNIQUE,

                    Symbol TEXT NOT NULL,
                    Timeframe TEXT NOT NULL,

                    ObservationType TEXT NOT NULL,

                    MarketTimeUtc TEXT NOT NULL,

                    Direction TEXT,

                    Value1 TEXT,
                    Value2 TEXT,
                    Value3 TEXT,

                    EngineVersion TEXT NOT NULL,

                    MetadataJson TEXT,

                    CreatedAtUtc TEXT NOT NULL
                );
                """;

            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateSetupsTableAsync(
            SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS Setups
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,

                    SetupId TEXT NOT NULL UNIQUE,

                    Symbol TEXT NOT NULL,
                    Timeframe TEXT NOT NULL,

                    SetupType TEXT NOT NULL,
                    Direction TEXT,

                    FormationTimeUtc TEXT NOT NULL,

                    EngineVersion TEXT NOT NULL,

                    SnapshotJson TEXT NOT NULL,

                    CreatedAtUtc TEXT NOT NULL
                );
                """;

            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateOutcomesTableAsync(
            SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS Outcomes
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,

                    OutcomeId TEXT NOT NULL UNIQUE,
                    SetupId TEXT NOT NULL,

                    FirstTouchTimeUtc TEXT,
                    FirstTouchPrice TEXT,

                    TwentyFivePercentFillTimeUtc TEXT,
                    FiftyPercentFillTimeUtc TEXT,
                    SeventyFivePercentFillTimeUtc TEXT,
                    FullFillTimeUtc TEXT,

                    MaximumFavorableExcursion TEXT,
                    MaximumAdverseExcursion TEXT,

                    HighestPriceAfterSetup TEXT,
                    LowestPriceAfterSetup TEXT,

                    Return5Minutes TEXT,
                    Return15Minutes TEXT,
                    Return30Minutes TEXT,
                    Return60Minutes TEXT,

                    SetupLifetimeMinutes INTEGER,

                    OutcomeJson TEXT,

                    CreatedAtUtc TEXT NOT NULL,

                    FOREIGN KEY (SetupId)
                        REFERENCES Setups(SetupId)
                );
                """;

            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateExperimentsTableAsync(
            SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS Experiments
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,

                    ExperimentId TEXT NOT NULL UNIQUE,

                    Name TEXT NOT NULL,

                    StrategyType TEXT NOT NULL,

                    StrategyVersion TEXT NOT NULL,

                    ParametersJson TEXT NOT NULL,

                    DatasetStartUtc TEXT,
                    DatasetEndUtc TEXT,

                    ResultJson TEXT,

                    CreatedAtUtc TEXT NOT NULL
                );
                """;

            await ExecuteAsync(connection, sql);
        }

        private static async Task RemoveExactDuplicateFvgsAsync(
            SqliteConnection connection)
        {
            const string sql = """
                DELETE FROM Observations
                WHERE
                    ObservationType = 'FVG'
                    AND Id NOT IN
                    (
                        SELECT MAX(Id)
                        FROM Observations
                        WHERE ObservationType = 'FVG'
                        GROUP BY
                            Symbol,
                            Timeframe,
                            ObservationType,
                            MarketTimeUtc,
                            IFNULL(Direction, ''),
                            CAST(Value1 AS REAL),
                            CAST(Value2 AS REAL),
                            CAST(Value3 AS REAL),
                            EngineVersion
                    );
                """;

            await ExecuteAsync(
                connection,
                sql);
        }

        private static async Task CreateIndexesAsync(
            SqliteConnection connection)
        {
            string[] indexes =
            {
                """
                CREATE INDEX IF NOT EXISTS
                    IX_RawMarketEvents_Symbol_Time
                ON RawMarketEvents
                    (Symbol, MarketTimestampUtc);
                """,

                """
                CREATE INDEX IF NOT EXISTS
                    IX_Candles_Symbol_Timeframe_Time
                ON Candles
                    (Symbol, Timeframe, OpenTimeUtc);
                """,

                """
                CREATE INDEX IF NOT EXISTS
                    IX_Observations_Symbol_Type_Time
                ON Observations
                    (Symbol, ObservationType, MarketTimeUtc);
                """,

                """
                CREATE UNIQUE INDEX IF NOT EXISTS
                    UX_Observations_FvgNaturalKey
                ON Observations
                (
                    Symbol,
                    Timeframe,
                    ObservationType,
                    MarketTimeUtc,
                    IFNULL(Direction, ''),
                    CAST(Value1 AS REAL),
                    CAST(Value2 AS REAL),
                    CAST(Value3 AS REAL),
                    EngineVersion
                )
                WHERE ObservationType = 'FVG';
                """,

                """
                CREATE INDEX IF NOT EXISTS
                    IX_Setups_Symbol_Type_Time
                ON Setups
                    (Symbol, SetupType, FormationTimeUtc);
                """,

                """
                CREATE INDEX IF NOT EXISTS
                    IX_Outcomes_SetupId
                ON Outcomes
                    (SetupId);
                """
            };

            foreach (string sql in indexes)
            {
                await ExecuteAsync(
                    connection,
                    sql);
            }
        }

        private static async Task ExecuteAsync(
            SqliteConnection connection,
            string sql)
        {
            await using SqliteCommand command =
                connection.CreateCommand();

            command.CommandText =
                sql;

            await command.ExecuteNonQueryAsync();
        }
    }
}
