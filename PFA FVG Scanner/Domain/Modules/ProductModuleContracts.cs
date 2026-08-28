namespace PFA_FVG_Scanner.Domain.Modules;

public enum ProductModuleKind { Core,Agent,PartnerStrategy,Coaching,Connector }
public enum ProductModuleIntegration { Native,ExternalApi }
public enum ModuleEntitlementStatus { Pending,Active,PastDue,Cancelled,Expired,Revoked }
public enum ModuleActivationState { Locked,Available,Active,Suspended,SafetyBlocked }

public sealed record ProductModuleDefinition(
    string ModuleId,string DisplayName,string Version,ProductModuleKind Kind,
    ProductModuleIntegration Integration,string SubscriptionSku,bool RequiresPaidEntitlement,
    bool RequiresSafetyGate,string? PartnerId,string? ManifestEndpoint,string Description,
    bool CanRouteToRealBroker=false);

public sealed record ModuleEntitlement(
    string EntitlementId,string UserId,string SubscriptionSku,ModuleEntitlementStatus Status,
    DateTime EffectiveFromUtc,DateTime? EffectiveToUtc,string BillingReference,string ContentHash);

public sealed record ModuleActivationRequest(
    string UserId,string ModuleId,string ModuleVersion,bool UserEnabled,bool SafetyGateSatisfied,
    bool ConnectorHealthy,IReadOnlyList<ModuleEntitlement> Entitlements,DateTime AsOfUtc);

public sealed record ModuleActivationDecision(
    string ModuleId,string ModuleVersion,ModuleActivationState State,bool Entitled,
    bool UserEnabled,bool SafetyGateSatisfied,bool ConnectorHealthy,string Reason,
    bool CanRouteToRealBroker=false);

public sealed class ProductModuleCatalog
{
    private static readonly IReadOnlyList<ProductModuleDefinition> Modules=
    [
        new("pfa-core","PFA Market Intelligence","1.0.0",ProductModuleKind.Core,ProductModuleIntegration.Native,"PFA-CORE",false,false,null,null,"Core charts, patterns, evidence, research, and sandbox access."),
        new("agent-research-lab","PFA Agent Research Lab","1.0.0",ProductModuleKind.Agent,ProductModuleIntegration.Native,"PFA-AGENT-RESEARCH",true,false,null,null,"Subscription-gated research agent trained only on immutable point-in-time datasets."),
        new("live-agent","PFA Live Agent","design-gated-1.0.0",ProductModuleKind.Agent,ProductModuleIntegration.Native,"PFA-LIVE-AGENT",true,true,null,null,"Future governed execution agent; subscription cannot bypass evidence or live-pilot authorization."),
        new("advanced-strategies","Advanced Strategies","partner-contract-1.0.0",ProductModuleKind.PartnerStrategy,ProductModuleIntegration.ExternalApi,"PFA-ADVANCED-STRATEGIES",true,false,"SAM",null,"Independent partner strategy module attachable through a versioned external API manifest."),
        new("prop-firm-coaching","Prop Firm Coaching","1.0.0",ProductModuleKind.Coaching,ProductModuleIntegration.Native,"PFA-COACHING",true,false,null,null,"Account-aware coaching, rule tracking, and challenge guidance."),
        new("custom-agent-access","Bring Your Own Agent","1.0.0",ProductModuleKind.Connector,ProductModuleIntegration.ExternalApi,"PFA-BYO-AGENT",true,true,null,null,"Paid connector access for a customer-supplied agent under PFA governance and data contracts.")
    ];
    public IReadOnlyList<ProductModuleDefinition> GetAll()=>Modules;
    public ProductModuleDefinition? Find(string id,string? version=null)=>Modules.FirstOrDefault(x=>x.ModuleId.Equals(id,StringComparison.OrdinalIgnoreCase)&&(version is null||x.Version.Equals(version,StringComparison.OrdinalIgnoreCase)));
}

public sealed class ModuleEntitlementEvaluator(ProductModuleCatalog catalog)
{
    public ModuleActivationDecision Evaluate(ModuleActivationRequest request)
    {
        var module=catalog.Find(request.ModuleId,request.ModuleVersion)??throw new KeyNotFoundException("Module version is not registered.");
        var entitlement=!module.RequiresPaidEntitlement||request.Entitlements.Any(x=>x.UserId==request.UserId&&x.SubscriptionSku==module.SubscriptionSku&&x.Status==ModuleEntitlementStatus.Active&&x.EffectiveFromUtc<=request.AsOfUtc&&(!x.EffectiveToUtc.HasValue||x.EffectiveToUtc>request.AsOfUtc));
        if(!entitlement)return Decision(ModuleActivationState.Locked,"An active paid entitlement is required.");
        if(module.RequiresSafetyGate&&!request.SafetyGateSatisfied)return Decision(ModuleActivationState.SafetyBlocked,"The independent safety/evidence gate is not satisfied.");
        if(module.Integration==ProductModuleIntegration.ExternalApi&&!request.ConnectorHealthy)return Decision(ModuleActivationState.Suspended,"The versioned external module connector is unavailable or unverified.");
        if(!request.UserEnabled)return Decision(ModuleActivationState.Available,"Entitled; the user has not activated this module.");
        return Decision(ModuleActivationState.Active,"Entitled and activated within current safety boundaries.");
        ModuleActivationDecision Decision(ModuleActivationState state,string reason)=>new(module.ModuleId,module.Version,state,entitlement,request.UserEnabled,request.SafetyGateSatisfied,request.ConnectorHealthy,reason,false);
    }
}
