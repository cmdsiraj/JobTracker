// Shared date-domain/histogram math for the timeline scrubber control, used
// by both the Dashboard and the pipeline board. Pure functions — no UI
// framework dependency.

namespace JobTracker.ViewModels;

public static class TimelineMath
{
    /// Month-floored extent covering `dates` through now.
    public static DateRange Domain(IEnumerable<DateTimeOffset> dates)
    {
        var now = DateTimeOffset.Now;
        var list = dates.ToList();
        var earliest = list.Count > 0 ? list.Min() : now.AddMonths(-5);
        var floor = StartOfMonth(earliest < now ? earliest : now);
        var upper = now > floor.AddDays(1) ? now : floor.AddDays(1);
        return new DateRange(floor, upper);
    }

    /// Counts per month across `domain`.
    public static List<(DateTimeOffset Month, int Count)> MonthHistogram(
        IEnumerable<DateTimeOffset> dates, DateRange domain)
    {
        var counts = new Dictionary<DateTimeOffset, int>();
        foreach (var date in dates)
        {
            var month = StartOfMonth(date);
            counts[month] = counts.GetValueOrDefault(month) + 1;
        }
        var result = new List<(DateTimeOffset, int)>();
        var cursor = StartOfMonth(domain.Start);
        while (cursor <= domain.End)
        {
            result.Add((cursor, counts.GetValueOrDefault(cursor)));
            cursor = cursor.AddMonths(1);
        }
        return result;
    }

    /// Last 5 whole months through the domain's end, clamped to the domain.
    public static DateRange DefaultWindow(DateRange domain)
    {
        var upper = domain.End;
        var lower = upper.AddMonths(-5);
        var clampedLower = lower > domain.Start ? lower : domain.Start;
        var finalUpper = upper > clampedLower ? upper : clampedLower;
        return new DateRange(clampedLower, finalUpper);
    }

    public static DateTimeOffset StartOfMonth(DateTimeOffset date) =>
        new(date.Year, date.Month, 1, 0, 0, 0, date.Offset);
}
