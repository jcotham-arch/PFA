namespace PFA_FVG_Scanner.Domain.Instruments;

public sealed class InstrumentDefinitionRegistry : IInstrumentDefinitionRegistry
{
    public const string DefinitionVersion = "1.0.0";
    private static readonly DateOnly InitialEffectiveDate = new(2026, 8, 27);
    private static readonly IReadOnlySet<string> CandleResolutions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "1m", "5m", "15m", "1h", "4h", "1d" };

    // The initial research universe is deliberately explicit. Changes to contract
    // economics require a new version/effective date, never mutation in place.
    private static readonly IReadOnlyList<InstrumentDefinition> Definitions =
    [
        Create("MES", "Micro E-mini S&P 500", "CME", AssetClass.EquityIndex,
            .25m, 5m, 2, "https://www.cmegroup.com/markets/equities/sp/micro-e-mini-sandp-500.contractSpecs.html"),
        Create("MNQ", "Micro E-mini Nasdaq-100", "CME", AssetClass.EquityIndex,
            .25m, 2m, 2, "https://www.cmegroup.com/markets/equities/nasdaq/micro-e-mini-nasdaq-100.contractSpecs.html"),
        Create("GC", "Gold", "COMEX", AssetClass.Metal,
            .10m, 100m, 1, "https://www.cmegroup.com/markets/metals/precious/gold.contractSpecs.html"),
        Create("CL", "WTI Crude Oil", "NYMEX", AssetClass.Energy,
            .01m, 1000m, 2, "https://www.cmegroup.com/markets/energy/crude-oil/light-sweet-crude.contractSpecs.html"),
        Create("ZN", "10-Year U.S. Treasury Note", "CBOT", AssetClass.InterestRate,
            1m / 64m, 1000m, 6, "https://www.cmegroup.com/markets/interest-rates/us-treasury/10-year-us-treasury-note.contractSpecs.html"),
        Create("6E", "Euro FX", "CME", AssetClass.ForeignExchange,
            .00005m, 125000m, 5, "https://www.cmegroup.com/markets/fx/g10/euro-fx.contractSpecs.html")
    ];

    public IReadOnlyList<InstrumentDefinition> GetAll() => Definitions;

    public InstrumentDefinition? Find(string instrumentIdOrRootSymbol, DateOnly asOfDate)
    {
        if (string.IsNullOrWhiteSpace(instrumentIdOrRootSymbol)) return null;
        return Definitions
            .Where(x => asOfDate >= x.EffectiveFrom)
            .Where(x => string.Equals(x.InstrumentId, instrumentIdOrRootSymbol.Trim(), StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x.RootSymbol, instrumentIdOrRootSymbol.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefault();
    }

    private static InstrumentDefinition Create(string root, string name, string exchange,
        AssetClass assetClass, decimal tickSize, decimal pointValue, int precision, string source) =>
        new(root, root, name, exchange, assetClass, "USD", tickSize, pointValue,
            precision, CandleResolutions, "CME_LEGACY_COMPATIBILITY", DefinitionVersion,
            InitialEffectiveDate, source);
}
