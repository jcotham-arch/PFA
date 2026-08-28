namespace PFA_FVG_Scanner.Domain.Instruments;

public sealed class InstrumentDefinitionRegistry : IInstrumentDefinitionRegistry
{
    public const string DefinitionVersion = "1.0.0";
    public const string UniverseExpansionVersion = "1.1.0";
    private static readonly DateOnly InitialEffectiveDate = new(2000, 1, 1);
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
            .00005m, 125000m, 5, "https://www.cmegroup.com/markets/fx/g10/euro-fx.contractSpecs.html"),
        Create("SI", "Silver", "COMEX", AssetClass.Metal,
            .005m, 5000m, 3, "https://www.cmegroup.com/markets/metals/precious/silver.contractSpecs.html", UniverseExpansionVersion),
        Create("6B", "British Pound", "CME", AssetClass.ForeignExchange,
            .0001m, 62500m, 4, "https://www.cmegroup.com/markets/fx/g10/british-pound.contractSpecs.html", UniverseExpansionVersion),
        Create("6J", "Japanese Yen", "CME", AssetClass.ForeignExchange,
            .0000005m, 12500000m, 7, "https://www.cmegroup.com/markets/fx/g10/japanese-yen.contractSpecs.html", UniverseExpansionVersion),
        Create("6A", "Australian Dollar", "CME", AssetClass.ForeignExchange,
            .0001m, 100000m, 4, "https://www.cmegroup.com/markets/fx/g10/australian-dollar.contractSpecs.html", UniverseExpansionVersion),
        Create("MYM", "Micro E-mini Dow", "CBOT", AssetClass.EquityIndex,
            1m, .5m, 0, "https://www.cmegroup.com/markets/equities/dow-jones/micro-e-mini-dow.contractSpecs.html", UniverseExpansionVersion),
        Create("M2K", "Micro E-mini Russell 2000", "CME", AssetClass.EquityIndex,
            .10m, 5m, 1, "https://www.cmegroup.com/markets/equities/russell/micro-e-mini-russell-2000.contractSpecs.html", UniverseExpansionVersion),
        Create("HG", "Copper", "COMEX", AssetClass.Metal,
            .0005m, 25000m, 4, "https://www.cmegroup.com/markets/metals/base/copper.contractSpecs.html", UniverseExpansionVersion),
        Create("NG", "Henry Hub Natural Gas", "NYMEX", AssetClass.Energy,
            .001m, 10000m, 3, "https://www.cmegroup.com/markets/energy/natural-gas/natural-gas.contractSpecs.html", UniverseExpansionVersion),
        Create("ZC", "Corn", "CBOT", AssetClass.Agriculture,
            .25m, 50m, 2, "https://www.cmegroup.com/markets/agriculture/grains/corn.contractSpecs.html", UniverseExpansionVersion),
        Create("ZW", "Chicago SRW Wheat", "CBOT", AssetClass.Agriculture,
            .25m, 50m, 2, "https://www.cmegroup.com/markets/agriculture/grains/wheat.contractSpecs.html", UniverseExpansionVersion),
        Create("ZS", "Soybeans", "CBOT", AssetClass.Agriculture,
            .25m, 50m, 2, "https://www.cmegroup.com/markets/agriculture/oilseeds/soybean.contractSpecs.html", UniverseExpansionVersion)
    ];

    public IReadOnlyList<InstrumentDefinition> GetAll() => Definitions;

    public InstrumentDefinition? Find(string instrumentIdOrRootSymbol, DateOnly asOfDate)
    {
        if (string.IsNullOrWhiteSpace(instrumentIdOrRootSymbol)) return null;
        var symbol=instrumentIdOrRootSymbol.Trim().ToUpperInvariant();
        var eligible=Definitions
            .Where(x => asOfDate >= x.EffectiveFrom)
            .OrderByDescending(x => x.RootSymbol.Length).ThenByDescending(x => x.EffectiveFrom).ToArray();
        return eligible.FirstOrDefault(x=>string.Equals(x.InstrumentId,symbol,StringComparison.OrdinalIgnoreCase)
            ||string.Equals(x.RootSymbol,symbol,StringComparison.OrdinalIgnoreCase))
            ??eligible.FirstOrDefault(x=>symbol.StartsWith(x.RootSymbol,StringComparison.OrdinalIgnoreCase));
    }

    private static InstrumentDefinition Create(string root, string name, string exchange,
        AssetClass assetClass, decimal tickSize, decimal pointValue, int precision, string source,
        string version = DefinitionVersion) =>
        new(root, root, name, exchange, assetClass, "USD", tickSize, pointValue,
            precision, CandleResolutions, "CME_LEGACY_COMPATIBILITY", version,
            InitialEffectiveDate, source);
}
