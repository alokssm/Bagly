namespace Bagly.Api.Services;

/// <summary>
/// Small helper for "today"/"this week"/"this month" boundaries in India Standard Time (UTC+5:30).
/// India has no daylight-saving time, so a fixed offset is used instead of <see cref="TimeZoneInfo"/>
/// lookups — this avoids relying on IANA ("Asia/Kolkata") vs Windows ("India Standard Time") time zone
/// database differences between local dev and the Linux container on Render.
/// </summary>
public static class IstTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(5.5);

    public static DateTime ToUtc(DateOnly istDate) => istDate.ToDateTime(TimeOnly.MinValue) - Offset;

    public static DateOnly TodayIst() => DateOnly.FromDateTime(DateTime.UtcNow + Offset);

    /// <summary>UTC [start, endExclusive) range covering the given IST calendar day.</summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) DayRangeUtc(DateOnly istDate) =>
        (ToUtc(istDate), ToUtc(istDate.AddDays(1)));

    /// <summary>UTC [start, endExclusive) range covering "today" in IST.</summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) TodayRangeUtc() => DayRangeUtc(TodayIst());

    /// <summary>UTC [start, endExclusive) range covering the current IST calendar week (Monday start) through today.</summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) ThisWeekRangeUtc()
    {
        var today = TodayIst();
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var monday = today.AddDays(-daysSinceMonday);
        return (ToUtc(monday), ToUtc(today.AddDays(1)));
    }

    /// <summary>UTC [start, endExclusive) range covering the current IST calendar month through today.</summary>
    public static (DateTime StartUtc, DateTime EndUtcExclusive) ThisMonthRangeUtc()
    {
        var today = TodayIst();
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        return (ToUtc(firstOfMonth), ToUtc(today.AddDays(1)));
    }
}
