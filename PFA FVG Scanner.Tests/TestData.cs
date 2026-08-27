namespace PFA_FVG_Scanner.Tests;

internal static class TestData
{
    internal static readonly DateTime BaseTime =
        new(2025, 1, 6, 14, 0, 0, DateTimeKind.Utc);

    internal static Candle Candle(
        int minute, decimal open, decimal high, decimal low, decimal close,
        string timeframe = "1m", string symbol = "MES", bool closed = true,
        decimal volume = 1m) => new()
        {
            Symbol = symbol,
            Timeframe = timeframe,
            OpenTimeUtc = BaseTime.AddMinutes(minute),
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = volume,
            IsClosed = closed
        };

    internal static FairValueGap BullishFvg(DateTime? formation = null) => new()
    {
        Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Symbol = "MES",
        Timeframe = "5m",
        Direction = FvgDirection.Bullish,
        FormationTimeUtc = formation ?? BaseTime,
        LowerBoundary = 100m,
        UpperBoundary = 102m,
        GapSize = 2m
    };

    internal static FvgOutcome Outcome(FairValueGap fvg) => new()
    {
        OutcomeId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
        FvgId = fvg.Id,
        Symbol = fvg.Symbol,
        Timeframe = fvg.Timeframe,
        Direction = fvg.Direction,
        FormationTimeUtc = fvg.FormationTimeUtc,
        ConfirmationTimeUtc = fvg.FormationTimeUtc.AddMinutes(5)
    };

    internal static FvgFeatureRecord Feature(
        int day, int ordinal, decimal realizedR,
        MesEntryModel entry = MesEntryModel.BoundaryTouch,
        decimal targetR = 1m, bool included = true) => new()
        {
            FvgId = Guid.Parse($"{day + 1:D8}-0000-0000-0000-{ordinal + 1:D12}"),
            EntryModel = entry,
            TargetR = targetR,
            Direction = FvgDirection.Bullish,
            SessionBucket = FvgSessionBucket.RegularMorning,
            GapSizePoints = 1.5m,
            MinutesFromConfirmationToEntry = 10,
            RiskTicks = 6m,
            EntryTimeUtc = BaseTime.Date.AddDays(day).AddHours(14).AddMinutes(ordinal),
            RealizedR = realizedR,
            GrossProfitLoss = realizedR * 10m,
            NetProfitLoss = realizedR * 9m,
            Outcome = realizedR > 0 ? FvgFeatureOutcome.Win : FvgFeatureOutcome.Loss,
            IncludedInLearningPopulation = included,
            IntrabarSequenceWasKnown = true,
            ExecutionPricesValid = true
        };
}
