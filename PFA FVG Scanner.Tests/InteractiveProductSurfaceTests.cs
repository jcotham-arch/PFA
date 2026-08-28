namespace PFA_FVG_Scanner.Tests;

public sealed class InteractiveProductSurfaceTests
{
    private static readonly string WebRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..","..","..","..","PFA FVG Scanner","wwwroot"));

    [Fact]
    public void MainResearchSurfaceLoadsCoachAndDrilldownExperience()
    {
        var html=File.ReadAllText(Path.Combine(WebRoot,"index.html"));
        var script=File.ReadAllText(Path.Combine(WebRoot,"experience.js"));
        Assert.Contains("/experience.js?v=1",html);Assert.Contains("PFA Setup Coach",script);
        Assert.Contains("/api/sequences/notifications",script);Assert.Contains("/api/research/pattern-trades/notifications",script);
        Assert.Contains("pattern-example",script);Assert.Contains("data-jump-research",script);
    }

    [Fact]
    public void AgentAndSandboxLoadTheirInteractiveControllers()
    {
        var agent=File.ReadAllText(Path.Combine(WebRoot,"agent.html"));
        var sandbox=File.ReadAllText(Path.Combine(WebRoot,"sandbox.html"));
        var sandboxScript=File.ReadAllText(Path.Combine(WebRoot,"sandbox-experience.js"));
        Assert.Contains("/agent-experience.js?v=1",agent);Assert.Contains("/experience.css",agent);
        Assert.Contains("/sandbox-experience.js?v=1",sandbox);Assert.Contains("/experience.css",sandbox);
        Assert.Contains("Refresh sandbox",sandboxScript);Assert.Contains("Simulation only",sandboxScript);
    }
}
