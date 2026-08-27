using System.Text.Json.Serialization;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Contracts;
using PFA_FVG_Scanner.Domain.Features;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.MarketState;
using PFA_FVG_Scanner.Domain.Patterns;
using PFA_FVG_Scanner.Domain.Patterns.Fvg;
using PFA_FVG_Scanner.Domain.Patterns.Liquidity;
using PFA_FVG_Scanner.Domain.Patterns.Breakouts;
using PFA_FVG_Scanner.Domain.Sessions;
using PFA_FVG_Scanner.Domain.Sequences;
using PFA_FVG_Scanner.Domain.Timeline;
using PFA_FVG_Scanner.MarketData;
using PFA_FVG_Scanner.Services;

var builder = WebApplication.CreateBuilder(args);

// Local desktop development must not depend on permission to write to the
// protected Windows Event Log. Production logging providers remain unchanged.
if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

// ------------------------------------------------------------
// CONTROLLERS + JSON
// ------------------------------------------------------------

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

// ------------------------------------------------------------
// OPENAPI + HTTP CLIENT
// ------------------------------------------------------------

builder.Services.AddOpenApi();

builder.Services.AddHttpClient();

// ------------------------------------------------------------
// DATABASE
// ------------------------------------------------------------

builder.Services.AddSingleton<PfaDatabase>();

builder.Services.AddSingleton<CandleRepository>();

builder.Services.AddSingleton<RawMarketEventRepository>();

builder.Services.AddSingleton<ObservationRepository>();

builder.Services.AddSingleton<FvgOutcomeRepository>();
builder.Services.AddSingleton<UniversalMarketRecordRepository>();
builder.Services.AddSingleton<MarketSequenceRepository>();

// Additive Phase 1 foundations. Legacy consumers remain unchanged until
// compatibility parity is established by later migration phases.
builder.Services.AddSingleton<IInstrumentDefinitionRegistry, InstrumentDefinitionRegistry>();
builder.Services.AddSingleton<IContractResolver, ContractResolver>();
builder.Services.AddSingleton<ITradingSessionService, LegacyUtcTradingSessionService>();
builder.Services.AddSingleton<ICanonicalBarCanonicalizer, CanonicalBarCanonicalizer>();
builder.Services.AddSingleton<CanonicalTimelineRepository>();
builder.Services.AddSingleton<CanonicalMarketDataIngestionService>();
builder.Services.AddSingleton<IFeatureDefinitionRegistry, FeatureDefinitionRegistry>();
builder.Services.AddSingleton<LegacyFvgFeatureAdapter>();
builder.Services.AddSingleton<IMarketStateEngine, MarketStateEngine>();
builder.Services.AddSingleton<FeatureStateRepository>();
builder.Services.AddSingleton<IMarketPatternModuleRegistry, MarketPatternModuleRegistry>();
builder.Services.AddSingleton<FvgPatternModule>();
builder.Services.AddSingleton<LiquiditySweepPatternModule>();
builder.Services.AddSingleton<RangeBreakoutPatternModule>();
builder.Services.AddSingleton<FailedBreakoutPatternModule>();
builder.Services.AddSingleton<MarketChartService>();
builder.Services.AddSingleton<IMarketSequenceDefinitionRegistry, MarketSequenceDefinitionRegistry>();
builder.Services.AddSingleton<IMarketSequenceEngine, MarketSequenceEngine>();

// ------------------------------------------------------------
// CORE ANALYSIS / RESEARCH SERVICES
// ------------------------------------------------------------

builder.Services.AddSingleton<FvgDetectionService>();

builder.Services.AddSingleton<FvgTrackingService>();

builder.Services.AddSingleton<CandleProcessingService>();

builder.Services.AddSingleton<FiveMinuteCandleAggregator>();

builder.Services.AddSingleton<MarketDataGapService>();

builder.Services.AddSingleton<MassiveBackfillService>();

builder.Services.AddSingleton<HistoricalFvgReplayService>();

builder.Services.AddSingleton<MesExecutionNormalizationService>();

builder.Services.AddSingleton<MesScenarioEngine>();

builder.Services.AddSingleton<FvgFeatureAnalysisService>();

builder.Services.AddSingleton<FvgCandidateRuleDiscoveryService>();

// ------------------------------------------------------------
// CROSS-DAY EVIDENCE
//
// THIS REGISTRATION FIXES THE CURRENT ERROR.
// ------------------------------------------------------------

builder.Services.AddSingleton<FvgCrossDayEvidenceService>();

// ------------------------------------------------------------
// OUT-OF-SAMPLE VALIDATION
// ------------------------------------------------------------

builder.Services.AddSingleton<FvgOutOfSampleValidationService>();

// ------------------------------------------------------------
// HISTORICAL REBUILD
// ------------------------------------------------------------

builder.Services.AddSingleton<HistoricalCandleRebuildService>();

builder.Services.AddSingleton<FvgQualificationService>();

// ------------------------------------------------------------
// SIMULATED PROVIDER
// ------------------------------------------------------------

builder.Services.AddSingleton<SimulatedMarketDataProvider>();

// ------------------------------------------------------------
// TRADOVATE PROVIDER
// ------------------------------------------------------------

var tradovateOptions =
    new TradovateOptions();

builder.Configuration
    .GetSection("Tradovate")
    .Bind(tradovateOptions);

builder.Services.AddSingleton(
    tradovateOptions);

builder.Services.AddSingleton<
    TradovateMarketDataProvider>();

// ------------------------------------------------------------
// MASSIVE PROVIDER
// ------------------------------------------------------------

var massiveOptions =
    new MassiveOptions();

builder.Configuration
    .GetSection("Massive")
    .Bind(massiveOptions);

builder.Services.AddSingleton(
    massiveOptions);

builder.Services.AddSingleton<
    MassiveMarketDataProvider>();

// ------------------------------------------------------------
// ACTIVE MARKET DATA PROVIDER
// ------------------------------------------------------------

builder.Services.AddSingleton<
    IMarketDataProvider>(
    serviceProvider =>
    {
        string provider =
            builder.Configuration[
                "MarketData:Provider"]
            ?? "Simulated";

        if (provider.Equals(
                "Massive",
                StringComparison.OrdinalIgnoreCase))
        {
            return serviceProvider
                .GetRequiredService<
                    MassiveMarketDataProvider>();
        }

        if (provider.Equals(
                "Tradovate",
                StringComparison.OrdinalIgnoreCase))
        {
            return serviceProvider
                .GetRequiredService<
                    TradovateMarketDataProvider>();
        }

        return serviceProvider
            .GetRequiredService<
                SimulatedMarketDataProvider>();
    });

// ------------------------------------------------------------
// MARKET DATA PIPELINE
// ------------------------------------------------------------

builder.Services.AddSingleton<
    MarketDataPipelineService>();

// ------------------------------------------------------------
// WATCHDOG
// ------------------------------------------------------------

builder.Services.AddSingleton<
    MarketDataWatchdogService>();

builder.Services.AddHostedService(
    serviceProvider =>
        serviceProvider.GetRequiredService<
            MarketDataWatchdogService>());

// ------------------------------------------------------------
// BUILD APPLICATION
// ------------------------------------------------------------

var app =
    builder.Build();

// ------------------------------------------------------------
// INITIALIZE DATABASE
// ------------------------------------------------------------

using (IServiceScope scope =
       app.Services.CreateScope())
{
    PfaDatabase database =
        scope.ServiceProvider
            .GetRequiredService<PfaDatabase>();

    await database.InitializeAsync();
}

// ------------------------------------------------------------
// DEVELOPMENT
// ------------------------------------------------------------

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ------------------------------------------------------------
// HTTP PIPELINE
// ------------------------------------------------------------

app.UseDefaultFiles();

app.UseStaticFiles();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
