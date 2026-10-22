using System;

namespace Broiler.Documents.Rtf;

/// <summary>
/// The packed date and time RTF states a tracked change's time in: a DTTM,
/// as <c>\revdttm</c> and <c>\revdttmdel</c> carry it.
/// </summary>
/// <remarks>
/// The bits, lowest first: the minute (6), the hour (5), the day of the month
/// (5), the month (4), the year since 1900 (9) and the day of the week (3),
/// which is implied by the rest and not read. A DTTM states no time zone, and
/// is taken as universal time.
/// </remarks>
internal static class RtfDateTime
{
    /// <summary>The time <paramref name="dttm"/> packs, or null where it is not a date.</summary>
    public static DateTimeOffset? FromDttm(int dttm)
    {
        uint bits = unchecked((uint)dttm);
        int minute = (int)(bits & 0x3F);
        int hour = (int)((bits >> 6) & 0x1F);
        int day = (int)((bits >> 11) & 0x1F);
        int month = (int)((bits >> 16) & 0xF);
        int year = 1900 + (int)((bits >> 20) & 0x1FF);
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59)
            return null;

        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// <paramref name="time"/> packed as a DTTM, to the minute, in universal
    /// time; zero for a time before 1900 or after 2411, which a DTTM cannot hold.
    /// </summary>
    public static int ToDttm(DateTimeOffset time)
    {
        DateTime utc = time.UtcDateTime;
        if (utc.Year < 1900 || utc.Year > 1900 + 0x1FF)
            return 0;

        uint bits = (uint)utc.Minute |
            ((uint)utc.Hour << 6) |
            ((uint)utc.Day << 11) |
            ((uint)utc.Month << 16) |
            ((uint)(utc.Year - 1900) << 20) |
            ((uint)utc.DayOfWeek << 29);
        return unchecked((int)bits);
    }
}
