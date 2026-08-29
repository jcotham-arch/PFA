using PFA_FVG_Scanner.Domain.Modules;
using PFA_FVG_Scanner.MarketData;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Tests;

public sealed class ConnectionSettingsServiceTests
{
    [Fact]
    public void CatalogReportsExistingAndPlannedAdaptersWithoutExposingSecrets()
    {
        var dashboard=new ConnectionSettingsService(new MassiveOptions{ApiKey="SECRET"},new TradovateOptions()).Get();
        Assert.Equal(3,dashboard.Connections.Count);
        Assert.False(dashboard.UserAuthenticationConfigured);Assert.False(dashboard.EncryptedCredentialVaultConfigured);
        Assert.False(dashboard.LiveBrokerRoutingEnabled);
        var massive=Assert.Single(dashboard.Connections,x=>x.ConnectionId=="massive");
        Assert.Equal(ConnectionSetupState.Configured,massive.State);Assert.DoesNotContain("SECRET",System.Text.Json.JsonSerializer.Serialize(dashboard));
        Assert.True(Assert.Single(massive.Fields,x=>x.FieldId=="apiKey").Secret);
        var partner=Assert.Single(dashboard.Connections,x=>x.ConnectionId=="advanced-strategies");
        Assert.Equal(ConnectionSetupState.ConnectorNotConfigured,partner.State);
        Assert.DoesNotContain(dashboard.Connections,x=>x.ConnectionId is "wealthcharts" or "robinhood" or "custom-market-data" or "custom-agent");
        Assert.All(dashboard.Connections,x=>Assert.False(x.CredentialMutationAvailable));
    }
}
