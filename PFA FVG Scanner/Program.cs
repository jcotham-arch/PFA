using System.Text.Json.Serialization;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.MarketData;
using PFA_FVG_Scanner.Services;

var builder = WebApplication.CreateBuilder(args);

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

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();