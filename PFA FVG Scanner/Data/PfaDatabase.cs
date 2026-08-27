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

            // Remove exact duplicate FVG observations that may already
            // exist before creating the natural-key unique index.
            await RemoveExactDuplicateFvgsAsync(connection);

            await CreateIndexesAsync(connection);
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
