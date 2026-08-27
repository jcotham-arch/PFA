namespace PFA_FVG_Scanner.Domain.Contracts;

public enum ContractResolutionConfidence
{
    Unresolved,
    Exact
}

public sealed record FuturesContract(
    string ContractId,
    string InstrumentId,
    string ContractSymbol,
    int ContractYear,
    int ContractMonth,
    string DefinitionVersion);

public sealed record ContractResolution(
    string Provider,
    string ProviderSymbol,
    string? InstrumentId,
    string? ContractId,
    ContractResolutionConfidence Confidence,
    string ResolverVersion,
    string? Reason)
{
    public bool IsResolved => Confidence == ContractResolutionConfidence.Exact;
}

public sealed record ProviderContractMapping(
    string Provider,
    string ProviderSymbol,
    FuturesContract Contract);

public interface IContractResolver
{
    ContractResolution Resolve(string provider, string providerSymbol);
}

public sealed class ContractResolver : IContractResolver
{
    public const string ResolverVersion = "1.0.0";
    private readonly IReadOnlyDictionary<string, ProviderContractMapping> _mappings;

    public ContractResolver(IEnumerable<ProviderContractMapping>? mappings = null)
    {
        _mappings = (mappings ?? Array.Empty<ProviderContractMapping>())
            .ToDictionary(x => Key(x.Provider, x.ProviderSymbol), StringComparer.OrdinalIgnoreCase);
    }

    public ContractResolution Resolve(string provider, string providerSymbol)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerSymbol))
            return Unresolved(provider, providerSymbol, "Provider and provider symbol are required.");

        if (!_mappings.TryGetValue(Key(provider, providerSymbol), out var mapping))
            return Unresolved(provider, providerSymbol, "No reviewed provider-contract mapping exists.");

        return new(provider.Trim(), providerSymbol.Trim(), mapping.Contract.InstrumentId,
            mapping.Contract.ContractId, ContractResolutionConfidence.Exact, ResolverVersion, null);
    }

    private static ContractResolution Unresolved(string? provider, string? symbol, string reason) =>
        new(provider?.Trim() ?? string.Empty, symbol?.Trim() ?? string.Empty, null, null,
            ContractResolutionConfidence.Unresolved, ResolverVersion, reason);

    private static string Key(string provider, string symbol) =>
        $"{provider.Trim().ToUpperInvariant()}|{symbol.Trim().ToUpperInvariant()}";
}

public sealed record ContinuousSeriesDefinition(
    string SeriesId,
    string InstrumentId,
    string DefinitionVersion,
    string RolloverPolicyId,
    bool PreservesRawContractPrices);
