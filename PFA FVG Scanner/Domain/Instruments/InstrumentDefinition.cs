namespace PFA_FVG_Scanner.Domain.Instruments;

public enum AssetClass
{
    EquityIndex,
    Metal,
    Energy,
    InterestRate,
    ForeignExchange,
    Agriculture
}

public sealed record InstrumentDefinition(
    string InstrumentId,
    string RootSymbol,
    string DisplayName,
    string Exchange,
    AssetClass AssetClass,
    string Currency,
    decimal TickSize,
    decimal PointValue,
    int PricePrecision,
    IReadOnlySet<string> ApprovedResolutions,
    string CalendarId,
    string DefinitionVersion,
    DateOnly EffectiveFrom,
    string SpecificationSource)
{
    public decimal TickValue => TickSize * PointValue;
}

public interface IInstrumentDefinitionRegistry
{
    IReadOnlyList<InstrumentDefinition> GetAll();
    InstrumentDefinition? Find(string instrumentIdOrRootSymbol, DateOnly asOfDate);
}
