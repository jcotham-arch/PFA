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
                    new("feed","Feed","select",false,true,massive.Feed)]),
            new("tradovate","Tradovate","Market data / brokerage","Existing demo-capable adapter. Execution authority remains independently disabled.","Username + application credentials",
                tradovateConfigured?ConnectionSetupState.Configured:ConnectionSetupState.NeedsCredentials,
                tradovateConfigured?"Credentials are present in protected application configuration.":"Username, password, CID, and secret are incomplete.",
                ["market-data","demo-environment"],[new("username","Username","text",false,true),new("password","Password","password",true,true),
                    new("cid","Client ID","password",true,true),new("sec","Client secret","password",true,true)]),
            Planned("wealthcharts","WealthCharts","Charts / market data","Customer-owned charting and market-data connection slot. Contract and supported authentication must be verified before activation."),
            Planned("robinhood","Robinhood","Broker / account","Customer-owned account connection slot. Supported data, account, and order capabilities must be contract-reviewed before activation."),
            new("advanced-strategies","Advanced Strategies","Partner module","Sam's independently deployed strategy-research service.","Service credential + versioned manifest",
                ConnectionSetupState.ConnectorNotConfigured,"PFA contract exists; service URL, credentials, and verified manifest are not configured.",
                ["market-context-analysis","strategy-candidate-generation","research-explanation"],
                [new("baseUrl","Service base URL","url",false,true),new("serviceCredential","Service credential","password",true,true),
                    new("manifestEndpoint","Manifest endpoint","text",false,true,"/.well-known/pfa-module")]),
            Planned("custom-market-data","Custom market-data API","Market data","Future customer-configured HTTPS adapter using a governed schema rather than arbitrary code execution."),
            Planned("custom-agent","Bring Your Own Agent","Agent connector","Future customer-owned agent connection with explicit data scopes, rate limits, and an independent safety gate.")
        ],false,false);
    }

    private static ConnectionSettingsItem Planned(string id,string name,string category,string description)=>
        new(id,name,category,description,"Contract pending",ConnectionSetupState.Planned,
            "Adapter definition, authentication, and secure credential storage are not implemented.",[],[]);
}
