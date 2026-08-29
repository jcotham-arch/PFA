using PFA_FVG_Scanner.Domain.Modules;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class AdvancedStrategiesCompatibilityTests
{
    [Fact]
    public void ContractAcceptsResearchOnlyMesManifest()
    {
        var service=new AdvancedStrategiesCompatibilityService(new ProductModuleCatalog());
        var result=service.Validate(Valid());Assert.True(result.Compatible);Assert.Empty(result.Issues);
        Assert.False(result.CanActivateStrategy);Assert.False(result.CanRouteToRealBroker);
        var packet=service.Packet();Assert.Equal("RegisteredConnectorNotConfigured",packet.IntegrationState);
        Assert.Equal(["MES"],packet.AcceptedInstruments);
    }

    [Fact]
    public void ContractFailsClosedOnExpandedAuthorityScopeOrMissingTimeframe()
    {
        var service=new AdvancedStrategiesCompatibilityService(new ProductModuleCatalog());
        var invalid=Valid() with{SupportedTimeframes=["5m"],RequiredDataScopes=["pfa-database:write"],
            CanActivateStrategy=true,CanRouteToRealBroker=true};
        var result=service.Validate(invalid);Assert.False(result.Compatible);
        Assert.Contains(result.Issues,x=>x.Code=="TIMEFRAME_REQUIRED");
        Assert.Contains(result.Issues,x=>x.Code=="SCOPE_NOT_ALLOWED");
        Assert.Contains(result.Issues,x=>x.Code=="ACTIVATION_FORBIDDEN");
        Assert.Contains(result.Issues,x=>x.Code=="ROUTING_FORBIDDEN");
    }

    private static AdvancedStrategiesManifest Valid()=>new("advanced-strategies","Advanced Strategies","1.0.0",
        "partner-contract-1.0.0","ExternalApi",
        ["market-context-analysis","strategy-candidate-generation","research-explanation"],["MES"],
        ["1m","5m","15m","1h"],["canonical-bars:read","market-observations:read","market-sequences:read"],
        false,false,"MANIFEST-HASH");
}
