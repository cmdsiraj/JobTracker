// Analytics overview: pick a time window + recruiting cycles, and every
// stat/chart follows. All statistics are computed in one pass over the
// data and cached in `Data`, recomputed only when the store or scope
// changes.

using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public sealed class DashboardData
{
    public int Total { get; set; }
    public int ThisWeek { get; set; }
    public int ThisMonth { get; set; }
    public int ThisYear { get; set; }
    public int Active { get; set; }
    public List<(ApplicationStatus Status, int Count)> StatusCounts { get; set; } = [];
    public List<(DateTimeOffset Date, int Count)> Daily { get; set; } = [];
    public List<(DateTimeOffset Date, int Count)> Weekly { get; set; } = [];
    public List<(ApplicationStatus Status, int Count)> Funnel { get; set; } = [];
    public List<(string Cycle, int Count)> Cycles { get; set; } = [];
    public List<HeatCell> Heat { get; set; } = [];
    public bool IsEmpty => Total == 0;

    public sealed record HeatCell(string Id, string WeekLabel, string DayLabel, int Count);
}

public sealed partial class DashboardViewModel : DispatcherObservableObject
{
    private static readonly string[] WeekdayLabels = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private readonly JobTrackerDbContext _context;

    [ObservableProperty]
    private List<JobApplication> _applications = [];

    [ObservableProperty]
    private DateRange? _timeline;

    [ObservableProperty]
    private DashboardData _data = new();

    public HashSet<string> SelectedCycles { get; } = [];

    public DashboardViewModel(JobTrackerDbContext context)
    {
        _context = context;
    }

    public DateRange ScrubberDomain => TimelineMath.Domain(Applications.Select(a => a.AppliedDate ?? a.LastUpdated));

    public List<(DateTimeOffset Month, int Count)> MonthHistogram() =>
        TimelineMath.MonthHistogram(Applications.Select(a => a.AppliedDate ?? a.LastUpdated), ScrubberDomain);

    public List<string> AvailableCycles =>
        [.. Applications.Select(a => a.Cycle).Where(c => c.Length > 0).Distinct()
            .OrderBy(c => c, Comparer<string>.Create(CycleDetector.CompareDescending))];

    public void Reload()
    {
        Applications = [.. _context.Applications.OrderByDescending(a => a.LastUpdated)];
        if (Timeline is null) Timeline = TimelineMath.DefaultWindow(ScrubberDomain);
        Recompute();
    }

    public void ToggleCycle(string cycle)
    {
        if (!SelectedCycles.Remove(cycle)) SelectedCycles.Add(cycle);
        Recompute();
    }

    public void ClearCycles()
    {
        SelectedCycles.Clear();
        Recompute();
    }

    partial void OnTimelineChanged(DateRange? value) => Recompute();

    // MARK: - Single-pass computation

    public void Recompute()
    {
        var now = DateTimeOffset.Now;
        var range = Timeline;

        var weekCutoff = now.AddDays(-7);
        var monthCutoff = now.AddDays(-30);
        var yearStart = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, now.Offset);

        var rangeEnd = range?.End ?? now;
        var dailyStart = StartOfDay(rangeEnd.AddDays(-29));
        var weeklyStart = StartOfWeekMonday(rangeEnd).AddDays(-11 * 7);
        var heatStart = StartOfWeekMonday(rangeEnd).AddDays(-15 * 7);

        var result = new DashboardData();
        var statusTally = new Dictionary<ApplicationStatus, int>();
        var dayBuckets = new Dictionary<DateTimeOffset, int>();
        var weekBuckets = new Dictionary<DateTimeOffset, int>();
        var heatCounts = new Dictionary<DateTimeOffset, int>();
        var cycleTally = new Dictionary<string, int>();
        var reachedRanks = new List<int>();

        foreach (var app in Applications)
        {
            var date = app.AppliedDate ?? app.LastUpdated;
            if (range is { } r && (date < r.Start || date > r.End)) continue;
            if (SelectedCycles.Count > 0 && !SelectedCycles.Contains(app.Cycle)) continue;

            result.Total++;
            if (app.Status.IsActive()) result.Active++;
            if (date >= weekCutoff) result.ThisWeek++;
            if (date >= monthCutoff) result.ThisMonth++;
            if (date >= yearStart) result.ThisYear++;
            statusTally[app.Status] = statusTally.GetValueOrDefault(app.Status) + 1;
            if (app.Cycle.Length > 0) cycleTally[app.Cycle] = cycleTally.GetValueOrDefault(app.Cycle) + 1;

            var day = StartOfDay(date);
            if (day >= dailyStart) dayBuckets[day] = dayBuckets.GetValueOrDefault(day) + 1;
            var week = StartOfWeekMonday(date);
            if (week >= weeklyStart) weekBuckets[week] = weekBuckets.GetValueOrDefault(week) + 1;
            if (day >= heatStart) heatCounts[day] = heatCounts.GetValueOrDefault(day) + 1;

            var best = app.Status == ApplicationStatus.Rejected ? 0 : app.Status.Rank();
            foreach (var evt in app.Events)
            {
                if (evt.DetectedStatus is { } detected && detected != ApplicationStatus.Rejected)
                {
                    best = Math.Max(best, detected.Rank());
                }
            }
            reachedRanks.Add(best);
        }

        result.StatusCounts = [.. ApplicationStatusExtensions.BoardColumns
            .Select(s => (Status: s, Count: statusTally.GetValueOrDefault(s)))
            .Where(t => t.Count > 0)];
        result.Funnel = [.. ApplicationStatusExtensions.FunnelStages
            .Select(stage => (stage, Count: reachedRanks.Count(r => r >= stage.Rank())))];
        // Ascending (oldest first) — this feeds a trend chart, unlike the
        // newest-first chip list in AvailableCycles.
        result.Cycles = [.. cycleTally.Select(kv => (Cycle: kv.Key, kv.Value))
            .OrderBy(t => CycleDetector.SortKey(t.Cycle))];

        result.Daily = [.. Enumerable.Range(0, 30).Select(offset =>
        {
            var day = dailyStart.AddDays(offset);
            return (day, dayBuckets.GetValueOrDefault(day));
        })];
        result.Weekly = [.. Enumerable.Range(0, 12).Select(offset =>
        {
            var week = weeklyStart.AddDays(offset * 7);
            return (week, weekBuckets.GetValueOrDefault(week));
        })];

        var cells = new List<DashboardData.HeatCell>();
        var lastMonth = "";
        for (var weekIndex = 0; weekIndex < 16; weekIndex++)
        {
            var weekStart = heatStart.AddDays(weekIndex * 7);
            var month = weekStart.ToString("MMM");
            var weekLabel = month == lastMonth ? new string(' ', weekIndex + 1) : month;
            lastMonth = month;
            for (var dayIndex = 0; dayIndex < 7; dayIndex++)
            {
                var day = weekStart.AddDays(dayIndex);
                cells.Add(new DashboardData.HeatCell($"{weekIndex}-{dayIndex}", weekLabel, WeekdayLabels[dayIndex],
                    heatCounts.GetValueOrDefault(StartOfDay(day))));
            }
        }
        result.Heat = cells;

        Data = result;
    }

    private static DateTimeOffset StartOfDay(DateTimeOffset date) => new(date.Year, date.Month, date.Day, 0, 0, 0, date.Offset);

    /// Monday-based start of the week containing `date`.
    private static DateTimeOffset StartOfWeekMonday(DateTimeOffset date)
    {
        var day = StartOfDay(date);
        var diff = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-diff);
    }
}
