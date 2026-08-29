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
                $"Data Source={DatabasePath};Cache=Shared;Default Timeout=30;Pooling=True";
        }

        public async Task InitializeAsync()
        {
            await using SqliteConnection connection =
                new(_connectionString);

            await connection.OpenAsync();

            await ConfigureConcurrencyAsync(connection);

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
            await MigrateUniversalOutcomeMetricIdentityAsync(connection);
            await CreateMarketSequenceTablesAsync(connection);
            await CreateStrategyRegistryTablesAsync(connection);
            await CreateGeneralResearchTablesAsync(connection);
            await CreateGeneralCrossDayEvidenceTablesAsync(connection);
            await CreateCrossMarketEvidenceTablesAsync(connection);
            await CreateExecutionAmbiguityTablesAsync(connection);
            await CreateHistoricalPipelineTablesAsync(connection);
            await CreateWalkForwardValidationTablesAsync(connection);
            await CreateOrderFlowTablesAsync(connection);
            await CreateSandboxLedgerTablesAsync(connection);
            await CreateGovernanceTablesAsync(connection);
            await CreateForwardCampaignTablesAsync(connection);
            await CreateMachineDiscoveryTablesAsync(connection);
            await CreateCertificationCampaignTablesAsync(connection);
            await CreateAgentResearchDatasetTablesAsync(connection);
            await CreateAgentBaselineTablesAsync(connection);
            await CreateAgentHurdleTablesAsync(connection);
            await CreateActionabilitySegmentResearchTablesAsync(connection);
            await CreatePatternTradeResearchTablesAsync(connection);
            await CreateExploratoryPaperCampaignTablesAsync(connection);
            await CreateAdaptiveScenarioLabTablesAsync(connection);
            await CreateSequenceTradeResearchTablesAsync(connection);
            await CreateTradeJournalTablesAsync(connection);
            await CreateTradeJournalAlignmentTablesAsync(connection);

            // Remove exact duplicate FVG observations that may already
            // exist before creating the natural-key unique index.
            await RemoveExactDuplicateFvgsAsync(connection);

            await CreateIndexesAsync(connection);
        }

        private static async Task ConfigureConcurrencyAsync(SqliteConnection connection)
        {
            await using var command=connection.CreateCommand();command.CommandText="""
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA busy_timeout=30000;
                """;await command.ExecuteNonQueryAsync();
        }

        private static async Task CreatePatternTradeResearchTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS PatternTradeResearchRuns
                (RunId TEXT PRIMARY KEY,EngineVersion TEXT NOT NULL,AsOfUtc TEXT NOT NULL,ObservationCount INTEGER NOT NULL,
                 HypothesisCount INTEGER NOT NULL,SampleCount INTEGER NOT NULL,ContentHash TEXT NOT NULL,RunJson TEXT NOT NULL,
                 CreatedAtUtc TEXT NOT NULL,CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0));
                CREATE TABLE IF NOT EXISTS PatternTradeResearchSamples
                (RunId TEXT NOT NULL,SampleId TEXT NOT NULL,HypothesisId TEXT NOT NULL,ObservationId TEXT NOT NULL,
                 InstrumentId TEXT NOT NULL,ModuleId TEXT NOT NULL,Split TEXT NOT NULL,Outcome TEXT NOT NULL,NetR TEXT,
                 ContentHash TEXT NOT NULL,SampleJson TEXT NOT NULL,PRIMARY KEY(RunId,SampleId),
                 FOREIGN KEY(RunId) REFERENCES PatternTradeResearchRuns(RunId));
                CREATE INDEX IF NOT EXISTS IX_PatternTradeResearchSamples_Hypothesis
                    ON PatternTradeResearchSamples(RunId,HypothesisId,Split,Outcome);
                CREATE TRIGGER IF NOT EXISTS TR_PatternTradeResearchRuns_NoUpdate BEFORE UPDATE ON PatternTradeResearchRuns
                    BEGIN SELECT RAISE(ABORT,'Pattern trade research runs are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_PatternTradeResearchRuns_NoDelete BEFORE DELETE ON PatternTradeResearchRuns
                    BEGIN SELECT RAISE(ABORT,'Pattern trade research runs are immutable');END;
                """;await ExecuteAsync(connection,sql);
        }

        private static async Task CreateSequenceTradeResearchTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS SequenceTradeResearchRuns
                (RunId TEXT PRIMARY KEY,EngineVersion TEXT NOT NULL,SourcePatternTradeRunId TEXT NOT NULL,AsOfUtc TEXT NOT NULL,
                 SequenceCompletionCount INTEGER NOT NULL,ContextSampleCount INTEGER NOT NULL,ContentHash TEXT NOT NULL,
                 RunJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0),
                 FOREIGN KEY(SourcePatternTradeRunId) REFERENCES PatternTradeResearchRuns(RunId));
                CREATE TABLE IF NOT EXISTS SequenceTradeResearchSamples
                (RunId TEXT NOT NULL,ContextSampleId TEXT NOT NULL,SourceSampleId TEXT NOT NULL,SequenceInstanceId TEXT NOT NULL,
                 SequenceDefinitionId TEXT NOT NULL,Role TEXT NOT NULL,HypothesisId TEXT NOT NULL,ObservationId TEXT NOT NULL,
                 Split TEXT NOT NULL,Outcome TEXT NOT NULL,NetR TEXT,SequenceKnownAtUtc TEXT NOT NULL,DecisionTimeUtc TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,PRIMARY KEY(RunId,ContextSampleId),
                 FOREIGN KEY(RunId) REFERENCES SequenceTradeResearchRuns(RunId));
                CREATE INDEX IF NOT EXISTS IX_SequenceTradeResearchSamples_Summary
                    ON SequenceTradeResearchSamples(RunId,SequenceDefinitionId,HypothesisId,Split);
                CREATE TRIGGER IF NOT EXISTS TR_SequenceTradeResearchRuns_NoUpdate BEFORE UPDATE ON SequenceTradeResearchRuns
                    BEGIN SELECT RAISE(ABORT,'Sequence trade research runs are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_SequenceTradeResearchRuns_NoDelete BEFORE DELETE ON SequenceTradeResearchRuns
                    BEGIN SELECT RAISE(ABORT,'Sequence trade research runs are immutable');END;
                """;await ExecuteAsync(connection,sql);
        }

        private static async Task CreateExploratoryPaperCampaignTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS ExploratoryPaperCampaigns
                (CampaignId TEXT PRIMARY KEY,CandidateId TEXT NOT NULL,StrategyId TEXT NOT NULL,StrategyVersion TEXT NOT NULL,
                 InstrumentId TEXT NOT NULL,SourcePatternTradeRunId TEXT NOT NULL,HypothesisId TEXT NOT NULL,
                 Mode TEXT NOT NULL,Status TEXT NOT NULL,ExecutionCount INTEGER NOT NULL,ContentHash TEXT NOT NULL,
                 CampaignJson TEXT NOT NULL,StartedAtUtc TEXT NOT NULL,CompletedAtUtc TEXT,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0),
                 FOREIGN KEY(SourcePatternTradeRunId) REFERENCES PatternTradeResearchRuns(RunId));
                CREATE TABLE IF NOT EXISTS ExploratoryPaperExecutions
                (CampaignId TEXT NOT NULL,ExecutionId TEXT NOT NULL,SourceSampleId TEXT NOT NULL,ObservationId TEXT NOT NULL,
                 EntryTimeUtc TEXT NOT NULL,ExitTimeUtc TEXT NOT NULL,Outcome TEXT NOT NULL,NetR TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,ExecutionJson TEXT NOT NULL,PRIMARY KEY(CampaignId,ExecutionId),
                 FOREIGN KEY(CampaignId) REFERENCES ExploratoryPaperCampaigns(CampaignId));
                CREATE TABLE IF NOT EXISTS ExploratoryPaperTelemetrySupplements
                (CampaignId TEXT NOT NULL,ExecutionId TEXT NOT NULL,TimeToMfeMilliseconds INTEGER,
                 TimeToMaeMilliseconds INTEGER,ContentHash TEXT NOT NULL,SupplementJson TEXT NOT NULL,
                 PRIMARY KEY(CampaignId,ExecutionId),
                 FOREIGN KEY(CampaignId,ExecutionId) REFERENCES ExploratoryPaperExecutions(CampaignId,ExecutionId));
                CREATE INDEX IF NOT EXISTS IX_ExploratoryPaperCampaigns_Candidate
                    ON ExploratoryPaperCampaigns(CandidateId,StartedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_ExploratoryPaperExecutions_Time
                    ON ExploratoryPaperExecutions(CampaignId,EntryTimeUtc);
                CREATE TRIGGER IF NOT EXISTS TR_ExploratoryPaperCampaigns_NoUpdate BEFORE UPDATE ON ExploratoryPaperCampaigns
                    BEGIN SELECT RAISE(ABORT,'Exploratory paper campaigns are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_ExploratoryPaperCampaigns_NoDelete BEFORE DELETE ON ExploratoryPaperCampaigns
                    BEGIN SELECT RAISE(ABORT,'Exploratory paper campaigns are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_ExploratoryPaperExecutions_NoUpdate BEFORE UPDATE ON ExploratoryPaperExecutions
                    BEGIN SELECT RAISE(ABORT,'Exploratory paper executions are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_ExploratoryPaperExecutions_NoDelete BEFORE DELETE ON ExploratoryPaperExecutions
                    BEGIN SELECT RAISE(ABORT,'Exploratory paper executions are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_ExploratoryPaperTelemetrySupplements_NoUpdate BEFORE UPDATE ON ExploratoryPaperTelemetrySupplements
                    BEGIN SELECT RAISE(ABORT,'Exploratory paper telemetry supplements are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_ExploratoryPaperTelemetrySupplements_NoDelete BEFORE DELETE ON ExploratoryPaperTelemetrySupplements
                    BEGIN SELECT RAISE(ABORT,'Exploratory paper telemetry supplements are immutable');END;
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('MES_TIER1_EXPLORATORY_PAPER_V1',datetime('now'),
                    'Add immutable MES Tier 1 blind-replay campaigns, execution telemetry and contract variants.');
                """;await ExecuteAsync(connection,sql);
        }

        private static async Task CreateTradeJournalTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS TradeJournalImports
                (ImportId TEXT PRIMARY KEY,ImporterVersion TEXT NOT NULL,SourceFileName TEXT NOT NULL,
                 SourceContentHash TEXT NOT NULL UNIQUE,SourceRows INTEGER NOT NULL,ExecutionCount INTEGER NOT NULL,
                 EpisodeCount INTEGER NOT NULL,EarliestExecutionUtc TEXT NOT NULL,LatestExecutionUtc TEXT NOT NULL,
                 NetProfit TEXT NOT NULL,ManifestJson TEXT NOT NULL,ImportedAtUtc TEXT NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0));
                CREATE TABLE IF NOT EXISTS TradeJournalExecutions
                (ImportId TEXT NOT NULL,ExecutionId TEXT NOT NULL,AccountHash TEXT NOT NULL,InstrumentId TEXT NOT NULL,
                 ContractId TEXT NOT NULL,MovementTimeUtc TEXT NOT NULL,Movement TEXT NOT NULL,SourceRow INTEGER NOT NULL,
                 ContentHash TEXT NOT NULL,ExecutionJson TEXT NOT NULL,PRIMARY KEY(ImportId,ExecutionId),
                 FOREIGN KEY(ImportId) REFERENCES TradeJournalImports(ImportId));
                CREATE TABLE IF NOT EXISTS TradeJournalEpisodes
                (ImportId TEXT NOT NULL,EpisodeId TEXT NOT NULL,InstrumentId TEXT NOT NULL,ContractId TEXT NOT NULL,
                 Direction TEXT NOT NULL,OpenedAtUtc TEXT NOT NULL,ClosedAtUtc TEXT NOT NULL,NetProfit TEXT NOT NULL,
                 Outcome TEXT NOT NULL,ContentHash TEXT NOT NULL,EpisodeJson TEXT NOT NULL,PRIMARY KEY(ImportId,EpisodeId),
                 FOREIGN KEY(ImportId) REFERENCES TradeJournalImports(ImportId));
                CREATE INDEX IF NOT EXISTS IX_TradeJournalEpisodes_Entry
                    ON TradeJournalEpisodes(ImportId,InstrumentId,OpenedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_TradeJournalExecutions_Time
                    ON TradeJournalExecutions(ImportId,InstrumentId,MovementTimeUtc);
                CREATE TRIGGER IF NOT EXISTS TR_TradeJournalImports_NoUpdate BEFORE UPDATE ON TradeJournalImports
                    BEGIN SELECT RAISE(ABORT,'Trade journal imports are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_TradeJournalImports_NoDelete BEFORE DELETE ON TradeJournalImports
                    BEGIN SELECT RAISE(ABORT,'Trade journal imports are immutable');END;
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('TRADE_JOURNAL_IMPORT_V1',datetime('now'),
                    'Add privacy-safe immutable execution-journal imports and reconstructed trade episodes.');
                """;await ExecuteAsync(connection,sql);
        }

        private static async Task CreateAdaptiveScenarioLabTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS AdaptiveScenarioGenerations
                (GenerationId TEXT PRIMARY KEY,GenerationNumber INTEGER NOT NULL,InstrumentId TEXT NOT NULL,
                 SourcePatternTradeRunId TEXT NOT NULL,Status TEXT NOT NULL,DevelopmentCutoffUtc TEXT NOT NULL,
                 EarliestNextBlindTradingDate TEXT NOT NULL,ContentHash TEXT NOT NULL,GenerationJson TEXT NOT NULL,
                 CreatedAtUtc TEXT NOT NULL,CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0));
                CREATE UNIQUE INDEX IF NOT EXISTS IX_AdaptiveScenarioGenerations_Number
                    ON AdaptiveScenarioGenerations(InstrumentId,GenerationNumber);
                CREATE TRIGGER IF NOT EXISTS TR_AdaptiveScenarioGenerations_NoUpdate BEFORE UPDATE ON AdaptiveScenarioGenerations
                    BEGIN SELECT RAISE(ABORT,'Adaptive scenario generations are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_AdaptiveScenarioGenerations_NoDelete BEFORE DELETE ON AdaptiveScenarioGenerations
                    BEGIN SELECT RAISE(ABORT,'Adaptive scenario generations are immutable');END;
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('MES_ADAPTIVE_SCENARIO_LAB_V1',datetime('now'),
                    'Add immutable MES champion/challenger generations with chronological blind-data boundaries.');
                """;await ExecuteAsync(connection,sql);
        }

        private static async Task CreateTradeJournalAlignmentTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS TradeJournalAlignmentReports
                (ReportId TEXT PRIMARY KEY,AlignmentVersion TEXT NOT NULL,ImportId TEXT NOT NULL,
                 EpisodeCount INTEGER NOT NULL,PatternMatchedEpisodes INTEGER NOT NULL,ContentHash TEXT NOT NULL,
                 ReportJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0),
                 FOREIGN KEY(ImportId) REFERENCES TradeJournalImports(ImportId));
                CREATE TABLE IF NOT EXISTS TradeJournalEpisodeAlignments
                (ReportId TEXT NOT NULL,EpisodeId TEXT NOT NULL,InstrumentId TEXT NOT NULL,EntryTimeUtc TEXT NOT NULL,
                 NetProfit TEXT NOT NULL,PatternMatchCount INTEGER NOT NULL,ContentHash TEXT NOT NULL,
                 AlignmentJson TEXT NOT NULL,PRIMARY KEY(ReportId,EpisodeId),
                 FOREIGN KEY(ReportId) REFERENCES TradeJournalAlignmentReports(ReportId));
                CREATE INDEX IF NOT EXISTS IX_TradeJournalEpisodeAlignments_Entry
                    ON TradeJournalEpisodeAlignments(ReportId,InstrumentId,EntryTimeUtc);
                CREATE TRIGGER IF NOT EXISTS TR_TradeJournalAlignmentReports_NoUpdate BEFORE UPDATE ON TradeJournalAlignmentReports
                    BEGIN SELECT RAISE(ABORT,'Trade journal alignment reports are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_TradeJournalAlignmentReports_NoDelete BEFORE DELETE ON TradeJournalAlignmentReports
                    BEGIN SELECT RAISE(ABORT,'Trade journal alignment reports are immutable');END;
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('TRADE_JOURNAL_ALIGNMENT_V1',datetime('now'),
                    'Add immutable point-in-time trade-journal to market-pattern alignment evidence.');
                """;await ExecuteAsync(connection,sql);
        }

        private static async Task CreateAgentResearchDatasetTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS AgentResearchDatasets
                (DatasetId TEXT PRIMARY KEY,DatasetVersion TEXT NOT NULL,DataRevision TEXT NOT NULL,
                 AsOfUtc TEXT NOT NULL,TargetHorizonMinutes INTEGER NOT NULL,ExampleCount INTEGER NOT NULL,
                 TrainCount INTEGER NOT NULL,ValidationCount INTEGER NOT NULL,TestCount INTEGER NOT NULL,
                 EarliestEventUtc TEXT NOT NULL,LatestEventUtc TEXT NOT NULL,ContentHash TEXT NOT NULL,
                 ManifestJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0));
                CREATE TABLE IF NOT EXISTS AgentResearchExamples
                (DatasetId TEXT NOT NULL,ExampleId TEXT NOT NULL,ObservationId TEXT NOT NULL,OutcomeId TEXT NOT NULL,
                 InstrumentId TEXT NOT NULL,ContractId TEXT,Timeframe TEXT NOT NULL,ModuleId TEXT NOT NULL,
                 PatternType TEXT NOT NULL,Direction TEXT NOT NULL,EventTimeUtc TEXT NOT NULL,
                 FeatureKnownAtUtc TEXT NOT NULL,DecisionTimeUtc TEXT NOT NULL,OutcomeKnownAtUtc TEXT NOT NULL,
                 Split TEXT NOT NULL,FeatureJson TEXT NOT NULL,LabelJson TEXT NOT NULL,SourceRevision TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,PRIMARY KEY(DatasetId,ExampleId),
                 FOREIGN KEY(DatasetId) REFERENCES AgentResearchDatasets(DatasetId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('AGENT_RESEARCH_DATASET_V1',datetime('now'),
                    'Add immutable point-in-time generic outcome research datasets and examples.');
                CREATE INDEX IF NOT EXISTS IX_AgentResearchExamples_SplitTime
                    ON AgentResearchExamples(DatasetId,Split,EventTimeUtc);
                CREATE TRIGGER IF NOT EXISTS TR_AgentResearchDatasets_NoUpdate BEFORE UPDATE ON AgentResearchDatasets
                    BEGIN SELECT RAISE(ABORT,'Agent research datasets are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_AgentResearchDatasets_NoDelete BEFORE DELETE ON AgentResearchDatasets
                    BEGIN SELECT RAISE(ABORT,'Agent research datasets are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_AgentResearchExamples_NoUpdate BEFORE UPDATE ON AgentResearchExamples
                    BEGIN SELECT RAISE(ABORT,'Agent research examples are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_AgentResearchExamples_NoDelete BEFORE DELETE ON AgentResearchExamples
                    BEGIN SELECT RAISE(ABORT,'Agent research examples are immutable'); END;
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateAgentBaselineTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS AgentBaselineRuns
                (RunId TEXT PRIMARY KEY,ModelVersion TEXT NOT NULL,DatasetId TEXT NOT NULL,
                 DatasetContentHash TEXT NOT NULL,TargetName TEXT NOT NULL,TrainingSamples INTEGER NOT NULL,
                 GroupCount INTEGER NOT NULL,TrainedAtUtc TEXT NOT NULL,ContentHash TEXT NOT NULL,RunJson TEXT NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0),
                 FOREIGN KEY(DatasetId) REFERENCES AgentResearchDatasets(DatasetId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('AGENT_BASELINE_RUNS_V1',datetime('now'),
                    'Add immutable research-only baseline model runs over frozen agent datasets.');
                CREATE INDEX IF NOT EXISTS IX_AgentBaselineRuns_Dataset ON AgentBaselineRuns(DatasetId,TrainedAtUtc);
                CREATE TRIGGER IF NOT EXISTS TR_AgentBaselineRuns_NoUpdate BEFORE UPDATE ON AgentBaselineRuns
                    BEGIN SELECT RAISE(ABORT,'Agent baseline runs are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_AgentBaselineRuns_NoDelete BEFORE DELETE ON AgentBaselineRuns
                    BEGIN SELECT RAISE(ABORT,'Agent baseline runs are immutable'); END;
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateAgentHurdleTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS AgentHurdleRuns
                (RunId TEXT PRIMARY KEY,ModelVersion TEXT NOT NULL,DatasetId TEXT NOT NULL,DatasetContentHash TEXT NOT NULL,
                 TrainedAtUtc TEXT NOT NULL,ContentHash TEXT NOT NULL,RunJson TEXT NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0),
                 FOREIGN KEY(DatasetId) REFERENCES AgentResearchDatasets(DatasetId));
                CREATE INDEX IF NOT EXISTS IX_AgentHurdleRuns_Dataset ON AgentHurdleRuns(DatasetId,TrainedAtUtc);
                CREATE TRIGGER IF NOT EXISTS TR_AgentHurdleRuns_NoUpdate BEFORE UPDATE ON AgentHurdleRuns
                    BEGIN SELECT RAISE(ABORT,'Agent hurdle runs are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_AgentHurdleRuns_NoDelete BEFORE DELETE ON AgentHurdleRuns
                    BEGIN SELECT RAISE(ABORT,'Agent hurdle runs are immutable');END;
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('AGENT_HURDLE_RUNS_V1',datetime('now'),'Add immutable decomposed-outcome hurdle model runs.');
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateActionabilitySegmentResearchTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS ActionabilitySegmentResearchReports
                (ReportId TEXT PRIMARY KEY,ReportVersion TEXT NOT NULL,DatasetId TEXT NOT NULL,DatasetContentHash TEXT NOT NULL,
                 MinimumSamples INTEGER NOT NULL,ContentHash TEXT NOT NULL,ReportJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0),
                 FOREIGN KEY(DatasetId) REFERENCES AgentResearchDatasets(DatasetId));
                CREATE INDEX IF NOT EXISTS IX_ActionabilitySegmentReports_Dataset
                    ON ActionabilitySegmentResearchReports(DatasetId,CreatedAtUtc);
                CREATE TRIGGER IF NOT EXISTS TR_ActionabilitySegmentReports_NoUpdate BEFORE UPDATE ON ActionabilitySegmentResearchReports
                    BEGIN SELECT RAISE(ABORT,'Actionability segment research reports are immutable');END;
                CREATE TRIGGER IF NOT EXISTS TR_ActionabilitySegmentReports_NoDelete BEFORE DELETE ON ActionabilitySegmentResearchReports
                    BEGIN SELECT RAISE(ABORT,'Actionability segment research reports are immutable');END;
                """;await ExecuteAsync(connection,sql);
        }

        private static async Task MigrateUniversalOutcomeMetricIdentityAsync(SqliteConnection connection)
        {
            await using var inspect = connection.CreateCommand();
            inspect.CommandText = "PRAGMA table_info(UniversalOutcomeMetrics);";
            var primaryKeyColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var reader = await inspect.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    if (reader.GetInt32(5) > 0) primaryKeyColumns.Add(reader.GetString(1));
            if (primaryKeyColumns.Contains("Unit")) return;
            const string sql = """
                CREATE TABLE UniversalOutcomeMetricsV2
                (OutcomeId TEXT NOT NULL,MetricName TEXT NOT NULL,HorizonMinutes INTEGER NOT NULL,
                 Value TEXT NOT NULL,Unit TEXT NOT NULL,MeasuredAtUtc TEXT,
                 PRIMARY KEY(OutcomeId,MetricName,HorizonMinutes,Unit),
                 FOREIGN KEY(OutcomeId) REFERENCES UniversalMarketOutcomes(OutcomeId));
                INSERT INTO UniversalOutcomeMetricsV2
                    (OutcomeId,MetricName,HorizonMinutes,Value,Unit,MeasuredAtUtc)
                SELECT OutcomeId,MetricName,HorizonMinutes,Value,Unit,MeasuredAtUtc FROM UniversalOutcomeMetrics;
                DROP TABLE UniversalOutcomeMetrics;
                ALTER TABLE UniversalOutcomeMetricsV2 RENAME TO UniversalOutcomeMetrics;
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('UNIVERSAL_OUTCOME_METRIC_UNIT_IDENTITY_1',datetime('now'),
                    'Include Unit in universal outcome metric identity so points, ticks, and dollars coexist.');
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateCertificationCampaignTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS CertificationCampaigns
                (CampaignId TEXT PRIMARY KEY,StrategyId TEXT NOT NULL,StrategyVersion TEXT NOT NULL,
                 EvidenceRevision TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,ContentHash TEXT NOT NULL,
                 CampaignJson TEXT NOT NULL,CanPromoteStrategy INTEGER NOT NULL CHECK(CanPromoteStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0));
                CREATE TABLE IF NOT EXISTS CertificationRulePacks
                (CampaignId TEXT NOT NULL,RulePackHash TEXT NOT NULL,FirmId TEXT NOT NULL,ProgramId TEXT NOT NULL,
                 RuleVersion TEXT NOT NULL,SourceReference TEXT NOT NULL,SourceContentHash TEXT NOT NULL,
                 IsOfficiallyVerified INTEGER NOT NULL,RulePackJson TEXT NOT NULL,
                 PRIMARY KEY(CampaignId,RulePackHash),FOREIGN KEY(CampaignId) REFERENCES CertificationCampaigns(CampaignId));
                CREATE TABLE IF NOT EXISTS CertificationResults
                (CampaignId TEXT NOT NULL,ResultId TEXT NOT NULL,RulePackHash TEXT NOT NULL,Status TEXT NOT NULL,
                 EvaluatedAtUtc TEXT NOT NULL,ContentHash TEXT NOT NULL,ResultJson TEXT NOT NULL,
                 CanPromoteStrategy INTEGER NOT NULL CHECK(CanPromoteStrategy=0),
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0),
                 PRIMARY KEY(CampaignId,ResultId),FOREIGN KEY(CampaignId) REFERENCES CertificationCampaigns(CampaignId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE22_CERTIFICATION_CAMPAIGNS_1',datetime('now'),
                    'Add immutable sandbox-only multi-rule-pack certification campaigns and results.');
                CREATE INDEX IF NOT EXISTS IX_CertificationResults_Status ON CertificationResults(Status,EvaluatedAtUtc);
                CREATE TRIGGER IF NOT EXISTS TR_CertificationCampaigns_NoUpdate BEFORE UPDATE ON CertificationCampaigns BEGIN SELECT RAISE(ABORT,'Certification campaigns are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_CertificationCampaigns_NoDelete BEFORE DELETE ON CertificationCampaigns BEGIN SELECT RAISE(ABORT,'Certification campaigns are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_CertificationRulePacks_NoUpdate BEFORE UPDATE ON CertificationRulePacks BEGIN SELECT RAISE(ABORT,'Certification rule packs are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_CertificationResults_NoUpdate BEFORE UPDATE ON CertificationResults BEGIN SELECT RAISE(ABORT,'Certification results are immutable'); END;
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateMachineDiscoveryTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS MachineDiscoveryRuns
                (RunId TEXT PRIMARY KEY,ModelId TEXT NOT NULL,EngineVersion TEXT NOT NULL,ModelVersion TEXT NOT NULL,
                 DatasetId TEXT NOT NULL,DataRevision TEXT NOT NULL,RandomSeed INTEGER NOT NULL,
                 MultipleComparisonMethod TEXT NOT NULL,ManifestHash TEXT NOT NULL,InputContentHash TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,ManifestJson TEXT NOT NULL,ResultJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0));
                CREATE TABLE IF NOT EXISTS MachineFeatureClusters
                (RunId TEXT NOT NULL,ClusterId TEXT NOT NULL,Ordinal INTEGER NOT NULL,TrainingSamples INTEGER NOT NULL,
                 EvaluationSamples INTEGER NOT NULL,RawPValue TEXT NOT NULL,AdjustedPValue TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,ClusterJson TEXT NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),PRIMARY KEY(RunId,ClusterId),
                 FOREIGN KEY(RunId) REFERENCES MachineDiscoveryRuns(RunId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE21_MACHINE_DISCOVERY_1',datetime('now'),'Add immutable reproducible research-only machine discovery runs and feature-cluster hypotheses.');
                CREATE INDEX IF NOT EXISTS IX_MachineDiscovery_Dataset ON MachineDiscoveryRuns(DatasetId,DataRevision,CreatedAtUtc);
                CREATE TRIGGER IF NOT EXISTS TR_MachineDiscoveryRuns_NoUpdate BEFORE UPDATE ON MachineDiscoveryRuns BEGIN SELECT RAISE(ABORT,'Machine discovery runs are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_MachineDiscoveryRuns_NoDelete BEFORE DELETE ON MachineDiscoveryRuns BEGIN SELECT RAISE(ABORT,'Machine discovery runs are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_MachineFeatureClusters_NoUpdate BEFORE UPDATE ON MachineFeatureClusters BEGIN SELECT RAISE(ABORT,'Machine feature clusters are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_MachineFeatureClusters_NoDelete BEFORE DELETE ON MachineFeatureClusters BEGIN SELECT RAISE(ABORT,'Machine feature clusters are immutable'); END;
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateForwardCampaignTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS ForwardCampaigns
                (CampaignId TEXT PRIMARY KEY,AccountId TEXT NOT NULL,InstanceId TEXT NOT NULL,StrategyId TEXT NOT NULL,
                 StrategyVersion TEXT NOT NULL,ExpectationId TEXT NOT NULL,ExpectationContentHash TEXT NOT NULL,
                 CampaignJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,CanPromoteStrategy INTEGER NOT NULL CHECK(CanPromoteStrategy=0));
                CREATE TABLE IF NOT EXISTS ForwardCampaignEvents
                (CampaignEventId TEXT PRIMARY KEY,CampaignId TEXT NOT NULL,EventType TEXT NOT NULL,Status TEXT NOT NULL,
                 OccurredAtUtc TEXT NOT NULL,Actor TEXT NOT NULL,Reason TEXT NOT NULL,ContentHash TEXT NOT NULL,
                 FOREIGN KEY(CampaignId) REFERENCES ForwardCampaigns(CampaignId));
                CREATE TABLE IF NOT EXISTS ForwardHealthSamples
                (SampleId TEXT PRIMARY KEY,CampaignId TEXT NOT NULL,SampledAtUtc TEXT NOT NULL,FeedHealthy INTEGER NOT NULL,
                 FeedStale INTEGER NOT NULL,ContentHash TEXT NOT NULL,SampleJson TEXT NOT NULL,
                 FOREIGN KEY(CampaignId) REFERENCES ForwardCampaigns(CampaignId));
                CREATE TABLE IF NOT EXISTS ForwardDailySnapshots
                (SnapshotId TEXT PRIMARY KEY,CampaignId TEXT NOT NULL,TradingDate TEXT NOT NULL,KnownAtUtc TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,SnapshotJson TEXT NOT NULL,CanPromoteStrategy INTEGER NOT NULL CHECK(CanPromoteStrategy=0),
                 UNIQUE(CampaignId,TradingDate),FOREIGN KEY(CampaignId) REFERENCES ForwardCampaigns(CampaignId));
                CREATE TABLE IF NOT EXISTS ForwardComparisons
                (ComparisonId TEXT PRIMARY KEY,CampaignId TEXT NOT NULL,Status TEXT NOT NULL,ComparedAtUtc TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,ComparisonJson TEXT NOT NULL,CanPromoteStrategy INTEGER NOT NULL CHECK(CanPromoteStrategy=0),
                 FOREIGN KEY(CampaignId) REFERENCES ForwardCampaigns(CampaignId));
                CREATE TABLE IF NOT EXISTS ForwardIncidents
                (IncidentId TEXT PRIMARY KEY,CampaignId TEXT NOT NULL,Category TEXT NOT NULL,OccurredAtUtc TEXT NOT NULL,
                 Summary TEXT NOT NULL,EvidenceJson TEXT NOT NULL,ContentHash TEXT NOT NULL,
                 FOREIGN KEY(CampaignId) REFERENCES ForwardCampaigns(CampaignId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE20_FORWARD_CAMPAIGNS_1',datetime('now'),
                    'Add forward campaigns, health telemetry, closed-day snapshots, comparisons and safe-suspension incidents.');
                CREATE INDEX IF NOT EXISTS IX_ForwardCampaignEvents_Status ON ForwardCampaignEvents(CampaignId,OccurredAtUtc);
                CREATE INDEX IF NOT EXISTS IX_ForwardHealthSamples_CampaignTime ON ForwardHealthSamples(CampaignId,SampledAtUtc);
                CREATE INDEX IF NOT EXISTS IX_ForwardComparisons_CampaignTime ON ForwardComparisons(CampaignId,ComparedAtUtc);
                CREATE TRIGGER IF NOT EXISTS TR_ForwardCampaigns_NoUpdate BEFORE UPDATE ON ForwardCampaigns BEGIN SELECT RAISE(ABORT,'Forward campaigns are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_ForwardCampaigns_NoDelete BEFORE DELETE ON ForwardCampaigns BEGIN SELECT RAISE(ABORT,'Forward campaigns are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_ForwardSnapshots_NoUpdate BEFORE UPDATE ON ForwardDailySnapshots BEGIN SELECT RAISE(ABORT,'Forward snapshots are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_ForwardComparisons_NoUpdate BEFORE UPDATE ON ForwardComparisons BEGIN SELECT RAISE(ABORT,'Forward comparisons are immutable'); END;
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateGovernanceTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS GovernancePolicies
                (PolicyId TEXT NOT NULL,PolicyVersion TEXT NOT NULL,EffectiveFromUtc TEXT NOT NULL,EffectiveToUtc TEXT,
                 ContentHash TEXT NOT NULL,PolicyJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,PRIMARY KEY(PolicyId,PolicyVersion));
                CREATE TABLE IF NOT EXISTS GovernanceApprovalEvents
                (EventId TEXT PRIMARY KEY,ApprovalId TEXT NOT NULL,EventType TEXT NOT NULL,OccurredAtUtc TEXT NOT NULL,
                 Actor TEXT NOT NULL,Reason TEXT NOT NULL,PayloadJson TEXT NOT NULL,ContentHash TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS GovernanceSuspensionEvents
                (EventId TEXT PRIMARY KEY,SuspensionId TEXT NOT NULL,EventType TEXT NOT NULL,OccurredAtUtc TEXT NOT NULL,
                 Actor TEXT NOT NULL,Reason TEXT NOT NULL,PayloadJson TEXT NOT NULL,ContentHash TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS GovernanceEmergencyStopEvents
                (EmergencyStopId TEXT PRIMARY KEY,IsActive INTEGER NOT NULL,Reason TEXT NOT NULL,Actor TEXT NOT NULL,
                 OccurredAtUtc TEXT NOT NULL,PayloadJson TEXT NOT NULL,ContentHash TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS GovernanceDecisions
                (DecisionId TEXT PRIMARY KEY,RequestId TEXT NOT NULL,Outcome TEXT NOT NULL,PolicyId TEXT NOT NULL,
                 PolicyVersion TEXT NOT NULL,AccountId TEXT NOT NULL,InstanceId TEXT NOT NULL,SignalId TEXT NOT NULL,
                 DecidedAtUtc TEXT NOT NULL,ContentHash TEXT NOT NULL,DecisionJson TEXT NOT NULL,
                 CanRouteToRealBroker INTEGER NOT NULL CHECK(CanRouteToRealBroker=0));
                CREATE TABLE IF NOT EXISTS GovernanceIncidents
                (IncidentId TEXT PRIMARY KEY,Severity TEXT NOT NULL,Category TEXT NOT NULL,AccountId TEXT,
                 InstanceId TEXT,OccurredAtUtc TEXT NOT NULL,Summary TEXT NOT NULL,EvidenceJson TEXT NOT NULL,ContentHash TEXT NOT NULL);
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE19_GOVERNANCE_1',datetime('now'),
                    'Add default-deny policy, approval, suspension, emergency-stop, decision and incident audit records.');
                CREATE INDEX IF NOT EXISTS IX_GovernanceDecisions_AccountTime ON GovernanceDecisions(AccountId,DecidedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_GovernanceApprovalEvents_Approval ON GovernanceApprovalEvents(ApprovalId,OccurredAtUtc);
                CREATE INDEX IF NOT EXISTS IX_GovernanceSuspensionEvents_Suspension ON GovernanceSuspensionEvents(SuspensionId,OccurredAtUtc);
                CREATE TRIGGER IF NOT EXISTS TR_GovernancePolicies_NoUpdate BEFORE UPDATE ON GovernancePolicies BEGIN SELECT RAISE(ABORT,'Governance policies are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_GovernancePolicies_NoDelete BEFORE DELETE ON GovernancePolicies BEGIN SELECT RAISE(ABORT,'Governance policies are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_GovernanceDecisions_NoUpdate BEFORE UPDATE ON GovernanceDecisions BEGIN SELECT RAISE(ABORT,'Governance decisions are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_GovernanceDecisions_NoDelete BEFORE DELETE ON GovernanceDecisions BEGIN SELECT RAISE(ABORT,'Governance decisions are immutable'); END;
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateSandboxLedgerTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS SandboxLedgerEvents
                (LedgerEventId TEXT PRIMARY KEY,CommandId TEXT NOT NULL,AccountId TEXT NOT NULL,InstanceId TEXT,
                 Sequence INTEGER NOT NULL,EventType TEXT NOT NULL,OccurredAtUtc TEXT NOT NULL,PayloadJson TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,UNIQUE(AccountId,Sequence),UNIQUE(AccountId,CommandId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE18_SANDBOX_LEDGER_1',datetime('now'),
                    'Add append-only idempotent virtual-account sandbox ledger; no live broker route.');
                CREATE INDEX IF NOT EXISTS IX_SandboxLedger_InstanceSequence ON SandboxLedgerEvents(InstanceId,Sequence);
                CREATE TRIGGER IF NOT EXISTS TR_SandboxLedger_NoUpdate BEFORE UPDATE ON SandboxLedgerEvents
                    BEGIN SELECT RAISE(ABORT,'Sandbox ledger is append-only'); END;
                CREATE TRIGGER IF NOT EXISTS TR_SandboxLedger_NoDelete BEFORE DELETE ON SandboxLedgerEvents
                    BEGIN SELECT RAISE(ABORT,'Sandbox ledger is append-only'); END;
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateOrderFlowTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS OrderFlowEvents
                (CanonicalEventId TEXT PRIMARY KEY,Provider TEXT NOT NULL,ProviderEventId TEXT NOT NULL,
                 InstrumentId TEXT NOT NULL,ContractId TEXT,ProviderSymbol TEXT NOT NULL,EventKind TEXT NOT NULL,
                 EventTimeUtc TEXT NOT NULL,KnownAtUtc TEXT NOT NULL,ProviderSequence INTEGER,Operation TEXT NOT NULL,
                 SupersedesCanonicalEventId TEXT,QualityFlags INTEGER NOT NULL,ContentHash TEXT NOT NULL,EventJson TEXT NOT NULL,
                 UNIQUE(Provider,ProviderEventId));
                CREATE TABLE IF NOT EXISTS OrderFlowClassifiedTrades
                (CanonicalEventId TEXT NOT NULL,ClassifierVersion TEXT NOT NULL,DataRevision TEXT NOT NULL,
                 EventTimeUtc TEXT NOT NULL,KnownAtUtc TEXT NOT NULL,Side TEXT NOT NULL,TradeJson TEXT NOT NULL,
                 PRIMARY KEY(CanonicalEventId,ClassifierVersion,DataRevision),FOREIGN KEY(CanonicalEventId) REFERENCES OrderFlowEvents(CanonicalEventId));
                CREATE TABLE IF NOT EXISTS OrderFlowFeatureSnapshots
                (SnapshotId TEXT PRIMARY KEY,InstrumentId TEXT NOT NULL,ContractId TEXT,WindowStartUtc TEXT NOT NULL,
                 WindowEndUtc TEXT NOT NULL,KnownAtUtc TEXT NOT NULL,TradingSessionId TEXT NOT NULL,FeatureSetVersion TEXT NOT NULL,
                 DataRevision TEXT NOT NULL,ContentHash TEXT NOT NULL,SnapshotJson TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS OrderFlowRetentionPolicies
                (PolicyVersion TEXT PRIMARY KEY,RawEventRetentionDays INTEGER NOT NULL,FeatureRetentionDays INTEGER NOT NULL,
                 AutomaticDeletionEnabled INTEGER NOT NULL CHECK(AutomaticDeletionEnabled=0),CreatedAtUtc TEXT NOT NULL);
                INSERT OR IGNORE INTO OrderFlowRetentionPolicies VALUES('retain-until-source-selected-1.0.0',365,730,0,datetime('now'));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE17_ORDER_FLOW_1',datetime('now'),
                    'Add isolated append-only order-flow events, classifications, feature snapshots and disabled retention policy.');
                CREATE INDEX IF NOT EXISTS IX_OrderFlowEvents_InstrumentTime ON OrderFlowEvents(InstrumentId,EventTimeUtc,KnownAtUtc);
                CREATE INDEX IF NOT EXISTS IX_OrderFlowSnapshots_InstrumentWindow ON OrderFlowFeatureSnapshots(InstrumentId,WindowStartUtc,WindowEndUtc);
                CREATE INDEX IF NOT EXISTS IX_OrderFlowSnapshots_PointInTime ON OrderFlowFeatureSnapshots(InstrumentId,WindowEndUtc,KnownAtUtc,ContractId);
                CREATE TRIGGER IF NOT EXISTS TR_OrderFlowEvents_NoUpdate BEFORE UPDATE ON OrderFlowEvents BEGIN SELECT RAISE(ABORT,'Order-flow events are append-only'); END;
                CREATE TRIGGER IF NOT EXISTS TR_OrderFlowEvents_NoDelete BEFORE DELETE ON OrderFlowEvents BEGIN SELECT RAISE(ABORT,'Order-flow events are append-only'); END;
                CREATE TRIGGER IF NOT EXISTS TR_OrderFlowTrades_NoUpdate BEFORE UPDATE ON OrderFlowClassifiedTrades BEGIN SELECT RAISE(ABORT,'Order-flow classifications are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_OrderFlowTrades_NoDelete BEFORE DELETE ON OrderFlowClassifiedTrades BEGIN SELECT RAISE(ABORT,'Order-flow classifications are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_OrderFlowSnapshots_NoUpdate BEFORE UPDATE ON OrderFlowFeatureSnapshots BEGIN SELECT RAISE(ABORT,'Order-flow snapshots are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_OrderFlowSnapshots_NoDelete BEFORE DELETE ON OrderFlowFeatureSnapshots BEGIN SELECT RAISE(ABORT,'Order-flow snapshots are immutable'); END;
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateWalkForwardValidationTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS WalkForwardPlans
                (PlanId TEXT PRIMARY KEY,PlanVersion TEXT NOT NULL,FrozenSignature TEXT NOT NULL,FrozenParameterHash TEXT NOT NULL,
                 DatasetId TEXT NOT NULL,DataRevision TEXT NOT NULL,PlanJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS WalkForwardFolds
                (PlanId TEXT NOT NULL,FoldId TEXT NOT NULL,Ordinal INTEGER NOT NULL,TrainingStartUtc TEXT NOT NULL,
                 TrainingEndUtc TEXT NOT NULL,ValidationStartUtc TEXT NOT NULL,ValidationEndUtc TEXT NOT NULL,
                 DatasetId TEXT NOT NULL,DataRevision TEXT NOT NULL,PRIMARY KEY(PlanId,FoldId),FOREIGN KEY(PlanId) REFERENCES WalkForwardPlans(PlanId));
                CREATE TABLE IF NOT EXISTS WalkForwardReports
                (ReportId TEXT PRIMARY KEY,PlanId TEXT NOT NULL UNIQUE,Status TEXT NOT NULL,ContentHash TEXT NOT NULL,
                 ReportJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),
                 FOREIGN KEY(PlanId) REFERENCES WalkForwardPlans(PlanId));
                CREATE TABLE IF NOT EXISTS WalkForwardFoldResults
                (ReportId TEXT NOT NULL,FoldId TEXT NOT NULL,Status TEXT NOT NULL,Samples INTEGER NOT NULL,
                 IndependentEvents INTEGER NOT NULL,ExpectancyR TEXT NOT NULL,ProfitFactor TEXT NOT NULL,
                 MaximumDrawdownR TEXT NOT NULL,ObservationContentHash TEXT NOT NULL,ParameterDriftDetected INTEGER NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),PRIMARY KEY(ReportId,FoldId),
                 FOREIGN KEY(ReportId) REFERENCES WalkForwardReports(ReportId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE16_WALK_FORWARD_1',datetime('now'),
                    'Add immutable non-overlapping walk-forward fold plans, results and non-activation enforcement.');
                CREATE INDEX IF NOT EXISTS IX_WalkForward_DatasetRevision ON WalkForwardPlans(DatasetId,DataRevision);
                CREATE TRIGGER IF NOT EXISTS TR_WalkForwardPlans_NoUpdate BEFORE UPDATE ON WalkForwardPlans
                    BEGIN SELECT RAISE(ABORT,'Walk-forward plans are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_WalkForwardPlans_NoDelete BEFORE DELETE ON WalkForwardPlans
                    BEGIN SELECT RAISE(ABORT,'Walk-forward plans are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_WalkForwardFolds_NoUpdate BEFORE UPDATE ON WalkForwardFolds
                    BEGIN SELECT RAISE(ABORT,'Walk-forward folds are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_WalkForwardFolds_NoDelete BEFORE DELETE ON WalkForwardFolds
                    BEGIN SELECT RAISE(ABORT,'Walk-forward folds are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_WalkForwardReports_NoUpdate BEFORE UPDATE ON WalkForwardReports
                    BEGIN SELECT RAISE(ABORT,'Walk-forward reports are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_WalkForwardReports_NoDelete BEFORE DELETE ON WalkForwardReports
                    BEGIN SELECT RAISE(ABORT,'Walk-forward reports are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_WalkForwardFoldResults_NoUpdate BEFORE UPDATE ON WalkForwardFoldResults
                    BEGIN SELECT RAISE(ABORT,'Walk-forward fold results are immutable'); END;
                CREATE TRIGGER IF NOT EXISTS TR_WalkForwardFoldResults_NoDelete BEFORE DELETE ON WalkForwardFoldResults
                    BEGIN SELECT RAISE(ABORT,'Walk-forward fold results are immutable'); END;
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateHistoricalPipelineTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS HistoricalPipelineJobs
                (JobId TEXT PRIMARY KEY,PlanId TEXT NOT NULL UNIQUE,Status TEXT NOT NULL,PlanJson TEXT NOT NULL,
                 CreatedAtUtc TEXT NOT NULL,UpdatedAtUtc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS HistoricalPipelineCheckpoints
                (JobId TEXT NOT NULL,WorkId TEXT NOT NULL,InstrumentId TEXT NOT NULL,ProviderSymbol TEXT NOT NULL,
                 WindowStartUtc TEXT NOT NULL,WindowEndUtc TEXT NOT NULL,Status TEXT NOT NULL,AttemptCount INTEGER NOT NULL,
                 ResultJson TEXT,LastError TEXT,UpdatedAtUtc TEXT NOT NULL,PRIMARY KEY(JobId,WorkId),
                 FOREIGN KEY(JobId) REFERENCES HistoricalPipelineJobs(JobId));
                CREATE TABLE IF NOT EXISTS HistoricalPipelineRuns
                (RunId TEXT PRIMARY KEY,JobId TEXT NOT NULL,Status TEXT NOT NULL,StartedAtUtc TEXT NOT NULL,
                 CompletedAtUtc TEXT,FailureReason TEXT,FOREIGN KEY(JobId) REFERENCES HistoricalPipelineJobs(JobId));
                CREATE TABLE IF NOT EXISTS HistoricalCoverageRecords
                (JobId TEXT NOT NULL,WorkId TEXT NOT NULL,InstrumentId TEXT NOT NULL,ProviderSymbol TEXT NOT NULL,
                 InstrumentDefinitionVersion TEXT NOT NULL,SourceResolution TEXT NOT NULL,RebuildResolution TEXT NOT NULL,WindowStartUtc TEXT NOT NULL,WindowEndUtc TEXT NOT NULL,
                 StartTradingSessionId TEXT NOT NULL,EndTradingSessionId TEXT NOT NULL,BarsReturned INTEGER NOT NULL,
                 BarsSaved INTEGER NOT NULL,RebuiltCandles INTEGER NOT NULL,QualityIssueCount INTEGER NOT NULL,
                 UpdatedAtUtc TEXT NOT NULL,PRIMARY KEY(JobId,WorkId),FOREIGN KEY(JobId,WorkId) REFERENCES HistoricalPipelineCheckpoints(JobId,WorkId));
                CREATE TABLE IF NOT EXISTS HistoricalDatasetManifests
                (ManifestId TEXT PRIMARY KEY,JobId TEXT NOT NULL UNIQUE,PlanId TEXT NOT NULL,Status TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,ManifestJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,
                 FOREIGN KEY(JobId) REFERENCES HistoricalPipelineJobs(JobId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE15_HISTORICAL_PIPELINE_1',datetime('now'),
                    'Add idempotent multi-instrument jobs, per-window checkpoints and reproducible dataset manifests.');
                CREATE INDEX IF NOT EXISTS IX_HistoricalCheckpoints_Status
                    ON HistoricalPipelineCheckpoints(JobId,Status,InstrumentId,WindowStartUtc);
                CREATE INDEX IF NOT EXISTS IX_HistoricalRuns_Job
                    ON HistoricalPipelineRuns(JobId,StartedAtUtc);
                CREATE INDEX IF NOT EXISTS IX_HistoricalCoverage_Instrument
                    ON HistoricalCoverageRecords(InstrumentId,WindowStartUtc,WindowEndUtc);
                """;
            await ExecuteAsync(connection, sql);
        }

        private static async Task CreateExecutionAmbiguityTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS ExecutionEvidenceRequests
                (RequestId TEXT PRIMARY KEY,SubjectId TEXT NOT NULL,InstrumentId TEXT NOT NULL,Direction TEXT NOT NULL,
                 WindowStartUtc TEXT NOT NULL,WindowEndUtc TEXT NOT NULL,StopPrice TEXT NOT NULL,TargetPrice TEXT NOT NULL,
                 OriginalResolution TEXT NOT NULL,ExecutionModelVersion TEXT NOT NULL,DataRevision TEXT NOT NULL,
                 RequestJson TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS ExecutionAmbiguityResults
                (ResultId TEXT PRIMARY KEY,RequestId TEXT NOT NULL,Chronology TEXT NOT NULL,ResolvedAtResolution TEXT,
                 FirstEventTimeUtc TEXT,ResolutionEngineVersion TEXT NOT NULL,ResultJson TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,
                 UsedOptimisticFallback INTEGER NOT NULL CHECK(UsedOptimisticFallback=0),
                 FOREIGN KEY(RequestId) REFERENCES ExecutionEvidenceRequests(RequestId));
                CREATE TABLE IF NOT EXISTS ExecutionResolutionAttempts
                (ResultId TEXT NOT NULL,Ordinal INTEGER NOT NULL,Resolution TEXT NOT NULL,Result TEXT NOT NULL,
                 Reason TEXT NOT NULL,SourceReferencesJson TEXT NOT NULL,PRIMARY KEY(ResultId,Ordinal),
                 FOREIGN KEY(ResultId) REFERENCES ExecutionAmbiguityResults(ResultId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE14_EXECUTION_AMBIGUITY_1',datetime('now'),
                    'Add conservative higher-resolution ambiguity requests, attempts, lineage and results.');
                CREATE INDEX IF NOT EXISTS IX_ExecutionAmbiguity_Subject
                    ON ExecutionEvidenceRequests(SubjectId,WindowStartUtc);
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateCrossMarketEvidenceTablesAsync(SqliteConnection connection)
        {
            const string sql="""
                CREATE TABLE IF NOT EXISTS CrossMarketEvidenceResults
                (ResultId TEXT PRIMARY KEY,PlanId TEXT NOT NULL,PlanVersion TEXT NOT NULL,FrozenSignature TEXT NOT NULL,
                 SourceInstrumentId TEXT NOT NULL,DatasetManifestId TEXT NOT NULL,Classification TEXT NOT NULL,
                 ComparableMarkets INTEGER NOT NULL,PositiveComparableMarkets INTEGER NOT NULL,
                 NegativeComparableMarkets INTEGER NOT NULL,Summary TEXT NOT NULL,PlanJson TEXT NOT NULL,
                 ContentHash TEXT NOT NULL,CreatedAtUtc TEXT NOT NULL,
                 InvalidatesSourceHypothesis INTEGER NOT NULL CHECK(InvalidatesSourceHypothesis=0),
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0));
                CREATE TABLE IF NOT EXISTS CrossMarketInstrumentEvidence
                (ResultId TEXT NOT NULL,InstrumentId TEXT NOT NULL,Comparability TEXT NOT NULL,
                 ComparabilityNotesJson TEXT NOT NULL,Samples INTEGER NOT NULL,IndependentEvents INTEGER NOT NULL,
                 ExpectancyR TEXT NOT NULL,NetR TEXT NOT NULL,AverageMovePoints TEXT NOT NULL,
                 AverageMoveTicks TEXT,AverageMoveDollarsPerContract TEXT,InstrumentDefinitionVersion TEXT NOT NULL,
                 EvidenceReference TEXT NOT NULL,PRIMARY KEY(ResultId,InstrumentId),
                 FOREIGN KEY(ResultId) REFERENCES CrossMarketEvidenceResults(ResultId));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE13_CROSS_MARKET_EVIDENCE_1',datetime('now'),
                    'Add immutable cross-market plans, normalized per-instrument evidence and comparability notes.');
                CREATE INDEX IF NOT EXISTS IX_CrossMarketEvidence_Signature
                    ON CrossMarketEvidenceResults(FrozenSignature,CreatedAtUtc);
                """;
            await ExecuteAsync(connection,sql);
        }

        private static async Task CreateGeneralCrossDayEvidenceTablesAsync(SqliteConnection connection)
        {
            const string sql = """
                CREATE TABLE IF NOT EXISTS GeneralCrossDayEvidenceReports
                (ReportId TEXT PRIMARY KEY,InstrumentId TEXT NOT NULL,EvidenceEngineVersion TEXT NOT NULL,
                 SessionAssignmentVersion TEXT NOT NULL,StartTradingDate TEXT NOT NULL,EndTradingDate TEXT NOT NULL,
                 ExpectedTradingDatesJson TEXT NOT NULL,SourceReference TEXT NOT NULL,ContentHash TEXT NOT NULL,
                 CreatedAtUtc TEXT NOT NULL,CanActivateAnyStrategy INTEGER NOT NULL CHECK(CanActivateAnyStrategy=0));
                CREATE TABLE IF NOT EXISTS GeneralCrossDaySignatureEvidence
                (ReportId TEXT NOT NULL,Signature TEXT NOT NULL,FamilyId TEXT NOT NULL,DefinitionVersion TEXT NOT NULL,
                 DefinitionJson TEXT NOT NULL,Classification TEXT NOT NULL,TotalTradingDays INTEGER NOT NULL,
                 ObservedDays INTEGER NOT NULL,MissingTradingDatesJson TEXT NOT NULL,PositiveDays INTEGER NOT NULL,
                 NegativeDays INTEGER NOT NULL,FlatDays INTEGER NOT NULL,TotalSamples INTEGER NOT NULL,
                 IndependentEvents INTEGER NOT NULL,AggregateMetricsJson TEXT NOT NULL,RegimeIdsJson TEXT NOT NULL,
                 GatesJson TEXT NOT NULL,CanAdvanceToFrozenValidation INTEGER NOT NULL,
                 CanActivateStrategy INTEGER NOT NULL CHECK(CanActivateStrategy=0),PRIMARY KEY(ReportId,Signature),
                 FOREIGN KEY(ReportId) REFERENCES GeneralCrossDayEvidenceReports(ReportId));
                CREATE TABLE IF NOT EXISTS GeneralCrossDayDailyEvidence
                (ReportId TEXT NOT NULL,Signature TEXT NOT NULL,TradingDate TEXT NOT NULL,Samples INTEGER NOT NULL,
                 IndependentEvents INTEGER NOT NULL,MetricsJson TEXT NOT NULL,DailyStatus TEXT NOT NULL,
                 RegimeIdsJson TEXT NOT NULL,PRIMARY KEY(ReportId,Signature,TradingDate),
                 FOREIGN KEY(ReportId,Signature) REFERENCES GeneralCrossDaySignatureEvidence(ReportId,Signature));
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('PHASE12_GENERAL_CROSS_DAY_1',datetime('now'),
                    'Add immutable cross-day reports, signatures, trading dates, regimes, gates and daily evidence.');
                CREATE INDEX IF NOT EXISTS IX_GeneralCrossDay_InstrumentDate
                    ON GeneralCrossDayEvidenceReports(InstrumentId,StartTradingDate,EndTradingDate);
                """;
            await ExecuteAsync(connection, sql);
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
                    PRIMARY KEY (OutcomeId, MetricName, HorizonMinutes, Unit),
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
                CREATE TABLE IF NOT EXISTS CanonicalBarInstrumentResolutions
                (
                    CanonicalBarId TEXT NOT NULL,
                    ResolutionVersion TEXT NOT NULL,
                    InstrumentId TEXT NOT NULL,
                    Evidence TEXT NOT NULL,
                    ResolvedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (CanonicalBarId, ResolutionVersion)
                );
                INSERT OR IGNORE INTO CanonicalBarInstrumentResolutions
                    (CanonicalBarId,ResolutionVersion,InstrumentId,Evidence,ResolvedAtUtc)
                SELECT CanonicalBarId,'root-symbol-resolution-1.0.0',
                    CASE
                        WHEN ProviderSymbol LIKE 'MES%' THEN 'MES' WHEN ProviderSymbol LIKE 'MNQ%' THEN 'MNQ'
                        WHEN ProviderSymbol LIKE 'MYM%' THEN 'MYM' WHEN ProviderSymbol LIKE 'M2K%' THEN 'M2K'
                        WHEN ProviderSymbol LIKE '6E%' THEN '6E' WHEN ProviderSymbol LIKE '6B%' THEN '6B'
                        WHEN ProviderSymbol LIKE '6J%' THEN '6J' WHEN ProviderSymbol LIKE '6A%' THEN '6A'
                        WHEN ProviderSymbol LIKE 'GC%' THEN 'GC' WHEN ProviderSymbol LIKE 'CL%' THEN 'CL'
                        WHEN ProviderSymbol LIKE 'ZN%' THEN 'ZN' WHEN ProviderSymbol LIKE 'SI%' THEN 'SI'
                        WHEN ProviderSymbol LIKE 'HG%' THEN 'HG' WHEN ProviderSymbol LIKE 'NG%' THEN 'NG'
                        WHEN ProviderSymbol LIKE 'ZC%' THEN 'ZC' WHEN ProviderSymbol LIKE 'ZW%' THEN 'ZW'
                        WHEN ProviderSymbol LIKE 'ZS%' THEN 'ZS' END,
                    ProviderSymbol,datetime('now')
                FROM CanonicalBars
                WHERE InstrumentId='UNRESOLVED' AND Revision=1 AND
                    NOT EXISTS (SELECT 1 FROM CanonicalMigrationJournal
                                WHERE MigrationId='CANONICAL_ROOT_RESOLUTION_1') AND
                    (ProviderSymbol LIKE 'MES%' OR ProviderSymbol LIKE 'MNQ%' OR ProviderSymbol LIKE 'MYM%'
                     OR ProviderSymbol LIKE 'M2K%' OR ProviderSymbol LIKE '6E%' OR ProviderSymbol LIKE '6B%'
                     OR ProviderSymbol LIKE '6J%' OR ProviderSymbol LIKE '6A%' OR ProviderSymbol LIKE 'GC%'
                     OR ProviderSymbol LIKE 'CL%' OR ProviderSymbol LIKE 'ZN%' OR ProviderSymbol LIKE 'SI%'
                     OR ProviderSymbol LIKE 'HG%' OR ProviderSymbol LIKE 'NG%' OR ProviderSymbol LIKE 'ZC%'
                     OR ProviderSymbol LIKE 'ZW%' OR ProviderSymbol LIKE 'ZS%');
                CREATE INDEX IF NOT EXISTS IX_CanonicalBarInstrumentResolutions_Instrument
                    ON CanonicalBarInstrumentResolutions (InstrumentId, CanonicalBarId);
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('CANONICAL_ROOT_RESOLUTION_1',datetime('now'),
                    'Additive root-symbol resolution for historically unresolved canonical bars.');
                CREATE TABLE IF NOT EXISTS CanonicalResolvedResearchBars
                (
                    CanonicalBarId TEXT PRIMARY KEY,
                    InstrumentId TEXT NOT NULL,
                    Timeframe TEXT NOT NULL,
                    OpenTimeUtc TEXT NOT NULL,
                    CloseTimeUtc TEXT NOT NULL,
                    Open TEXT NOT NULL,High TEXT NOT NULL,Low TEXT NOT NULL,Close TEXT NOT NULL,Volume TEXT NOT NULL
                );
                INSERT OR IGNORE INTO CanonicalResolvedResearchBars
                    (CanonicalBarId,InstrumentId,Timeframe,OpenTimeUtc,CloseTimeUtc,Open,High,Low,Close,Volume)
                SELECT b.CanonicalBarId,COALESCE(r.InstrumentId,b.InstrumentId),b.Timeframe,b.OpenTimeUtc,b.CloseTimeUtc,
                       b.Open,b.High,b.Low,b.Close,b.Volume
                FROM CanonicalBars b LEFT JOIN CanonicalBarInstrumentResolutions r
                  ON r.CanonicalBarId=b.CanonicalBarId AND r.ResolutionVersion='root-symbol-resolution-1.0.0'
                WHERE b.Revision=1 AND b.IsComplete=1 AND COALESCE(r.InstrumentId,b.InstrumentId)<>'UNRESOLVED'
                  AND NOT EXISTS (SELECT 1 FROM CanonicalMigrationJournal
                                  WHERE MigrationId='CANONICAL_RESEARCH_LOOKUP_1');
                CREATE INDEX IF NOT EXISTS IX_CanonicalResolvedResearchBars_Instrument_Time
                    ON CanonicalResolvedResearchBars(InstrumentId,Timeframe,CloseTimeUtc);
                CREATE INDEX IF NOT EXISTS IX_CanonicalResolvedResearchBars_SeasonalClock
                    ON CanonicalResolvedResearchBars(InstrumentId,Timeframe,strftime('%H:%M',CloseTimeUtc),CloseTimeUtc);
                INSERT OR IGNORE INTO CanonicalMigrationJournal(MigrationId,AppliedAtUtc,Description)
                VALUES('CANONICAL_RESEARCH_LOOKUP_1',datetime('now'),
                    'Materialize resolved revision-one bars for point-in-time research feature lookup.');
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
