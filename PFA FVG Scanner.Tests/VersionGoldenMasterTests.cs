using System.Text.Json;

namespace PFA_FVG_Scanner.Tests;

public sealed class VersionGoldenMasterTests
{
    [Fact]
    public void LegacyOnePointZeroAndOnePointOneDiscrepancyRemainsExplicit()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenMasters", "legacy-engine-versions.json");
        using var golden = JsonDocument.Parse(File.ReadAllText(path));
        var root = golden.RootElement;
        Assert.Equal("1.0.0", root.GetProperty("observationRepository").GetString());
        Assert.Equal("1.0.0", root.GetProperty("candidateDiscovery").GetString());
        Assert.Equal("1.0.0", root.GetProperty("crossDayEvidence").GetString());
        Assert.Equal("1.0.0", root.GetProperty("outOfSampleValidation").GetString());
        Assert.Equal("1.1.0", root.GetProperty("historicalReplay").GetString());
        Assert.Equal("1.1.0", root.GetProperty("mesScenario").GetString());
    }
}
