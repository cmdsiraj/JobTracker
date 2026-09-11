// Every dimension the user can slice applications by. Filters combine (AND
// across dimensions, OR within one).

using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Models;

namespace JobTracker.ViewModels;

public sealed partial class FilterState : ObservableObject
{
    [ObservableProperty]
    private string _searchText = "";

    public HashSet<ApplicationStatus> Statuses { get; } = [];
    public HashSet<string> Companies { get; } = [];
    public HashSet<string> Cycles { get; } = [];
    public HashSet<string> Sources { get; } = [];

    /// Pipeline timeline window, driven by the interactive scrubber.
    /// Null means "not yet initialized".
    [ObservableProperty]
    private DateRange? _timeline;

    public bool HasActiveFilters => Statuses.Count > 0 || Companies.Count > 0 || Cycles.Count > 0 || Sources.Count > 0;

    public int ActiveFilterCount => Statuses.Count + Companies.Count + Cycles.Count + Sources.Count;

    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    partial void OnSearchTextChanged(string value) => NotifyChanged();
    partial void OnTimelineChanged(DateRange? value) => NotifyChanged();

    public void Clear()
    {
        Statuses.Clear();
        Companies.Clear();
        Cycles.Clear();
        Sources.Clear();
        NotifyChanged();
    }

    public void ToggleStatus(ApplicationStatus status) { Toggle(Statuses, status); NotifyChanged(); }
    public void ToggleCompany(string company) { Toggle(Companies, company); NotifyChanged(); }
    public void ToggleCycle(string cycle) { Toggle(Cycles, cycle); NotifyChanged(); }
    public void ToggleSource(string source) { Toggle(Sources, source); NotifyChanged(); }

    private static void Toggle<T>(HashSet<T> set, T value) where T : notnull
    {
        if (!set.Remove(value)) set.Add(value);
    }

    public bool Matches(JobApplication app)
    {
        if (Statuses.Count > 0 && !Statuses.Contains(app.Status)) return false;
        if (Companies.Count > 0 && !Companies.Contains(app.Company)) return false;
        if (Cycles.Count > 0 && !Cycles.Contains(app.Cycle)) return false;
        if (Sources.Count > 0 && !Sources.Contains(app.Source ?? "")) return false;
        if (Timeline is { } timeline)
        {
            var date = app.AppliedDate ?? app.LastUpdated;
            if (date < timeline.Start || date > timeline.End) return false;
        }
        if (SearchText.Length > 0)
        {
            var matchesSearch =
                app.Company.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                app.RoleTitle.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                app.Notes.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                app.Tags.Any(t => t.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            if (!matchesSearch) return false;
        }
        return true;
    }
}

/// A closed date range (WPF has no built-in ClosedRange&lt;DateTime&gt;).
public readonly record struct DateRange(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset date) => date >= Start && date <= End;
}
