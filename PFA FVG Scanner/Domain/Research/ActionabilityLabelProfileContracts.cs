namespace PFA_FVG_Scanner.Domain.Research;

public sealed record ActionabilityLabelProfile(string Split,string ModuleId,int Samples,int ProfitableSamples,
    decimal ProfitableRate,decimal MeanNetR,decimal MeanPositiveNetR,decimal MeanNegativeNetR,
    decimal MeanMaximumFavorableExcursionR,decimal MeanMaximumAdverseExcursionR);

public sealed record ActionabilityLabelProfileReport(string Version,string DatasetId,string DatasetContentHash,
    int Examples,IReadOnlyList<string> DecomposedTargets,IReadOnlyList<ActionabilityLabelProfile> Profiles,
    DateTime GeneratedAtUtc,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);
