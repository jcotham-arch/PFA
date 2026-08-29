using PFA_FVG_Scanner.Domain.Modules;

namespace PFA_FVG_Scanner.Services;

public sealed class AdvancedStrategiesCompatibilityService(ProductModuleCatalog catalog)
{
    public const string ContractVersion="partner-contract-1.0.0";
    private static readonly string[] Capabilities=["market-context-analysis","strategy-candidate-generation","research-explanation"];
    private static readonly string[] Scopes=["canonical-bars:read","market-observations:read","market-sequences:read"];
    private static readonly string[] Timeframes=["1m","5m","15m","1h"];

    public AdvancedStrategiesCompatibilityResult Validate(AdvancedStrategiesManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);var issues=new List<PartnerCompatibilityIssue>();
        Check(manifest.ModuleId=="advanced-strategies","MODULE_ID","ModuleId must be advanced-strategies.");
        Check(manifest.DisplayName=="Advanced Strategies","DISPLAY_NAME","DisplayName must be Advanced Strategies.");
        Check(manifest.ContractVersion==ContractVersion,"CONTRACT_VERSION",$"ContractVersion must be {ContractVersion}.");
        Check(manifest.Integration=="ExternalApi","INTEGRATION","Integration must be ExternalApi.");
        Check(!string.IsNullOrWhiteSpace(manifest.ModuleVersion),"MODULE_VERSION","A semantic module version is required.");
        Check(manifest.SupportedInstruments.Contains("MES",StringComparer.OrdinalIgnoreCase),"MES_REQUIRED","Initial compatibility requires MES support.");
        foreach(var timeframe in Timeframes)Check(manifest.SupportedTimeframes.Contains(timeframe,StringComparer.OrdinalIgnoreCase),
            "TIMEFRAME_REQUIRED",$"Initial compatibility requires {timeframe} support.");
        foreach(var capability in Capabilities)Check(manifest.Capabilities.Contains(capability,StringComparer.Ordinal),
            "CAPABILITY_REQUIRED",$"Missing required capability: {capability}.");
        foreach(var scope in manifest.RequiredDataScopes)Check(Scopes.Contains(scope,StringComparer.Ordinal),
            "SCOPE_NOT_ALLOWED",$"Data scope is not allowed by contract 1.0: {scope}.");
        Check(!manifest.CanActivateStrategy,"ACTIVATION_FORBIDDEN","Partner modules cannot activate strategies.");
        Check(!manifest.CanRouteToRealBroker,"ROUTING_FORBIDDEN","Partner modules cannot route orders to a real broker.");
        Check(!string.IsNullOrWhiteSpace(manifest.ContentHash),"CONTENT_HASH","A deterministic manifest content hash is required.");
        return new(issues.Count==0,ContractVersion,issues);
        void Check(bool condition,string code,string message){if(!condition)issues.Add(new(code,message));}
    }

    public AdvancedStrategiesIntegrationPacket Packet()
    {
        var module=catalog.Find("advanced-strategies",ContractVersion)!;
        return new(module.ModuleId,module.DisplayName,ContractVersion,"RegisteredConnectorNotConfigured",
            "Research and paper/simulation only; MES first; no direct database, strategy activation, or broker access.",
            ["MES"],Timeframes,Capabilities,Scopes,
            ["Capability manifest","OpenAPI specification","Versioned request/response DTOs","Health and compatibility endpoint",
             "Deterministic replay tests","Point-in-time leakage tests","Idempotency tests","Deployment and rollback instructions"],
            "Docs/PFA_ADVANCED_STRATEGIES_CURRENT_INTEGRATION_PACKET.md",
            "/api/product/modules/advanced-strategies/compatibility");
    }
}
