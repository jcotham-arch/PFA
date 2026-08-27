using PFA_FVG_Scanner.Domain.Contracts;
using PFA_FVG_Scanner.Domain.Instruments;
using PFA_FVG_Scanner.Domain.Sessions;

namespace PFA_FVG_Scanner.Tests;

public sealed class PhaseOneFoundationTests
{
    [Theory]
    [InlineData("MES", 0.25, 5, 1.25)]
    [InlineData("MNQ", 0.25, 2, 0.50)]
    [InlineData("GC", 0.10, 100, 10)]
    [InlineData("CL", 0.01, 1000, 10)]
    [InlineData("ZN", 0.015625, 1000, 15.625)]
    [InlineData("6E", 0.00005, 125000, 6.25)]
    public void ReviewedInstrumentEconomicsAreVersioned(
        string root, decimal tick, decimal pointValue, decimal tickValue)
    {
        var definition = new InstrumentDefinitionRegistry().Find(root, new DateOnly(2026, 8, 27));
        Assert.NotNull(definition);
        Assert.Equal(tick, definition.TickSize);
        Assert.Equal(pointValue, definition.PointValue);
        Assert.Equal(tickValue, definition.TickValue);
        Assert.Equal("USD", definition.Currency);
        Assert.Equal("1.0.0", definition.DefinitionVersion);
        Assert.StartsWith("https://www.cmegroup.com/", definition.SpecificationSource);
    }

    [Fact]
    public void DefinitionLookupIsEffectiveDatedAndCaseInsensitive()
    {
        var registry = new InstrumentDefinitionRegistry();
        Assert.Null(registry.Find("mes", new DateOnly(2026, 8, 26)));
        Assert.Equal("MES", registry.Find("mes", new DateOnly(2026, 8, 27))!.InstrumentId);
        Assert.Equal(6, registry.GetAll().Count);
    }

    [Fact]
    public void ContractResolverRejectsUnknownMappingsRatherThanGuessing()
    {
        var result = new ContractResolver().Resolve("Massive", "MESU6");
        Assert.False(result.IsResolved);
        Assert.Equal(ContractResolutionConfidence.Unresolved, result.Confidence);
        Assert.Null(result.ContractId);
        Assert.Contains("No reviewed", result.Reason);
    }

    [Fact]
    public void ContractResolverPreservesProviderAndDatedContractIdentity()
    {
        var contract = new FuturesContract("MES-2026-09", "MES", "MESU6", 2026, 9, "1.0.0");
        var resolver = new ContractResolver(new[]
        {
            new ProviderContractMapping("Massive", "MESU6", contract)
        });
        var result = resolver.Resolve("massive", "mesu6");
        Assert.True(result.IsResolved);
        Assert.Equal("MES", result.InstrumentId);
        Assert.Equal("MES-2026-09", result.ContractId);
        Assert.Equal("1.0.0", result.ResolverVersion);
    }

    [Theory]
    [InlineData(7, TradingSessionSegment.Overnight)]
    [InlineData(8, TradingSessionSegment.Premarket)]
    [InlineData(13, TradingSessionSegment.RegularMorning)]
    [InlineData(16, TradingSessionSegment.RegularMidday)]
    [InlineData(18, TradingSessionSegment.RegularAfternoon)]
    [InlineData(20, TradingSessionSegment.PostMarket)]
    public void LegacySessionAdapterPreservesExistingUtcBuckets(int hour, TradingSessionSegment segment)
    {
        var timestamp = new DateTime(2026, 8, 27, hour, 0, 0, DateTimeKind.Utc);
        var result = new LegacyUtcTradingSessionService().Assign("MES", timestamp);
        Assert.Equal(segment, result.Segment);
        Assert.Equal(new DateOnly(2026, 8, 27), result.Session.TradingDate);
        Assert.Equal(SessionAssignmentQuality.LegacyCompatibility, result.Session.Quality);
        Assert.Equal("legacy-utc-1.0.0", result.AssignmentVersion);
    }

    [Fact]
    public void ContinuousSeriesRequiresNamedRolloverPolicyAndRawPricePreservation()
    {
        var series = new ContinuousSeriesDefinition(
            "MES-CONTINUOUS-RESEARCH", "MES", "1.0.0", "UNRESOLVED", true);
        Assert.Equal("UNRESOLVED", series.RolloverPolicyId);
        Assert.True(series.PreservesRawContractPrices);
    }

    [Theory]
    [InlineData("2026-03-08T07:30:00Z")] // U.S. DST transition Sunday
    [InlineData("2026-11-01T07:30:00Z")] // U.S. DST transition Sunday
    [InlineData("2026-12-25T15:00:00Z")] // holiday
    [InlineData("2026-08-29T15:00:00Z")] // Saturday
    public void LegacyCalendarDoesNotInferUnreviewedExchangeRules(string timestampText)
    {
        var timestamp = DateTime.Parse(timestampText).ToUniversalTime();
        var result = new LegacyUtcTradingSessionService().Assign("MES", timestamp);
        Assert.Equal(SessionAssignmentQuality.LegacyCompatibility, result.Session.Quality);
        Assert.Equal("UTC", result.Session.ExchangeTimeZone);
        Assert.False(result.Session.IsHoliday);
        Assert.False(result.Session.IsEarlyClose);
        Assert.NotEqual(TradingSessionSegment.Maintenance, result.Segment);
        Assert.Equal(TimeSpan.FromDays(1), result.Session.SessionCloseUtc - result.Session.SessionOpenUtc);
    }
}
