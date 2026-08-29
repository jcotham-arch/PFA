using PFA_FVG_Scanner.Domain.Modules;
using PFA_FVG_Scanner.MarketData;

namespace PFA_FVG_Scanner.Services;

public sealed class ConnectionSettingsService(MassiveOptions massive,TradovateOptions tradovate)
{
    public ConnectionSettingsDashboard Get()
    {
        var massiveConfigured=!string.IsNullOrWhiteSpace(massive.ApiKey);
        var tradovateConfigured=!string.IsNullOrWhiteSpace(tradovate.Username)&&!string.IsNullOrWhiteSpace(tradovate.Password)&&
            !string.IsNullOrWhiteSpace(tradovate.Cid)&&!string.IsNullOrWhiteSpace(tradovate.Sec);
        return new([
            new("massive","Massive Futures Market Data","Market data","Historical and streaming futures market-data adapter.","API key",
                massiveConfigured?ConnectionSetupState.Configured:ConnectionSetupState.NeedsCredentials,
                massiveConfigured?"A credential is present in protected application configuration.":"API key has not been configured.",
                ["historical-bars","streaming-bars"],[new("apiKey","API key","password",true,true,massiveConfigured?"Configured · hidden":null),
                    new("feed","Feed","select",false,true,massive.Feed)],"Verified official REST and futures WebSocket APIs.","https://massive.com/docs/rest/futures/overview"),
            new("tradovate","Tradovate","Market data / brokerage","Existing demo-capable adapter. Execution authority remains independently disabled.","Username + application credentials",
                tradovateConfigured?ConnectionSetupState.Configured:ConnectionSetupState.NeedsCredentials,
                tradovateConfigured?"Credentials are present in protected application configuration.":"Username, password, CID, and secret are incomplete.",
                ["market-data","demo-environment"],[new("username","Username","text",false,true),new("password","Password","password",true,true),
                    new("cid","Client ID","password",true,true),new("sec","Client secret","password",true,true)],
                "Verified official REST, WebSocket, bearer-token, and OAuth APIs.","https://api.tradovate.com/"),
            new("advanced-strategies","Advanced Strategies","Partner module","Sam's independently deployed strategy-research service.","Service credential + versioned manifest",
                ConnectionSetupState.ConnectorNotConfigured,"PFA contract exists; service URL, credentials, and verified manifest are not configured.",
                ["market-context-analysis","strategy-candidate-generation","research-explanation"],
                [new("baseUrl","Service base URL","url",false,true),new("serviceCredential","Service credential","password",true,true),
                    new("manifestEndpoint","Manifest endpoint","text",false,true,"/.well-known/pfa-module")],
                "Verified PFA-owned partner-contract-1.0.0; external service deployment remains pending.",
                "/api/product/modules/advanced-strategies/integration-packet")
        ],false,false);
    }
}
