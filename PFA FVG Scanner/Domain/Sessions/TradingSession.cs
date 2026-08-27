namespace PFA_FVG_Scanner.Domain.Sessions;

public enum SessionAssignmentQuality
{
    LegacyCompatibility,
    CalendarResolved,
    Unresolved
}

public enum TradingSessionSegment
{
    Overnight,
    Premarket,
    RegularMorning,
    RegularMidday,
    RegularAfternoon,
    PostMarket,
    Maintenance,
    Unknown
}

public sealed record TradingSession(
    string TradingSessionId,
    DateOnly TradingDate,
    DateTime SessionOpenUtc,
    DateTime SessionCloseUtc,
    string ExchangeTimeZone,
    string CalendarVersion,
    bool IsHoliday,
    bool IsEarlyClose,
    SessionAssignmentQuality Quality);

public sealed record SessionAssignment(
    TradingSession Session,
    TradingSessionSegment Segment,
    DateTime AsOfUtc,
    string AssignmentVersion);

public interface ITradingSessionService
{
    SessionAssignment Assign(string instrumentId, DateTime timestampUtc);
}

/// <summary>
/// Compatibility-only session assignment. It makes the current UTC calendar-day
/// behavior explicit without claiming to implement an authoritative CME calendar.
/// </summary>
public sealed class LegacyUtcTradingSessionService : ITradingSessionService
{
    public const string AssignmentVersion = "legacy-utc-1.0.0";

    public SessionAssignment Assign(string instrumentId, DateTime timestampUtc)
    {
        var utc = EnsureUtc(timestampUtc);
        var date = DateOnly.FromDateTime(utc);
        var open = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var session = new TradingSession(
            $"{instrumentId.Trim().ToUpperInvariant()}|{date:yyyy-MM-dd}|LEGACY-UTC",
            date, open, open.AddDays(1), "UTC", AssignmentVersion,
            false, false, SessionAssignmentQuality.LegacyCompatibility);
        return new(session, GetLegacySegment(utc.Hour), utc, AssignmentVersion);
    }

    private static TradingSessionSegment GetLegacySegment(int hour) => hour switch
    {
        < 8 => TradingSessionSegment.Overnight,
        < 13 => TradingSessionSegment.Premarket,
        < 16 => TradingSessionSegment.RegularMorning,
        < 18 => TradingSessionSegment.RegularMidday,
        < 20 => TradingSessionSegment.RegularAfternoon,
        _ => TradingSessionSegment.PostMarket
    };

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };
}
