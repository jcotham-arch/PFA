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

            // Remove exact duplicate FVG observations that may already
            // exist before creating the natural-key unique index.
            await RemoveExactDuplicateFvgsAsync(connection);

            await CreateIndexesAsync(connection);
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