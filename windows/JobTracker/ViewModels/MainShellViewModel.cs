// Drives MainWindow's sidebar + pipeline/dashboard/leads/review routing,
// filtering, and the sync overlay. Window/dialog opening (Application
// Detail, New Application, Import Scope, Activity Log) is handled by
// MainWindow's code-behind, which observes SelectedApplication and the
// pipeline's PendingImport/Stage.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public enum SidebarSection { Dashboard, AllApplications, Status, Leads, Review }

public sealed record SidebarItem(SidebarSection Section, ApplicationStatus? Status = null)
{
    public string Title => Section switch
    {
        SidebarSection.Dashboard => "Dashboard",
        SidebarSection.AllApplications => "All Applications",
        SidebarSection.Status => Status?.DisplayName() ?? "",
        SidebarSection.Leads => "Leads",
        SidebarSection.Review => "Needs Review",
        _ => "",
    };

    public static readonly SidebarItem Dashboard = new(SidebarSection.Dashboard);
    public static readonly SidebarItem AllApplications = new(SidebarSection.AllApplications);
    public static readonly SidebarItem Leads = new(SidebarSection.Leads);
    public static readonly SidebarItem Review = new(SidebarSection.Review);
    public static SidebarItem ForStatus(ApplicationStatus status) => new(SidebarSection.Status, status);
}

public sealed partial class MainShellViewModel : DispatcherObservableObject
{
    public AppState AppState { get; }
    private readonly JobTrackerDbContext _context;

    public FilterState Filters { get; } = new();

    [ObservableProperty]
    private SidebarItem _selection = SidebarItem.Dashboard;

    [ObservableProperty]
    private List<JobApplication> _applications = [];

    [ObservableProperty]
    private JobApplication? _selectedApplication;

    [ObservableProperty]
    private bool _overlayVisible;

    public MainShellViewModel(AppState appState)
    {
        AppState = appState;
        _context = appState.Context;
        Filters.Changed += RaiseFilteredChanged;
        AppState.Pipeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SyncPipeline.Stage)) UpdateOverlay();
        };
        UpdateOverlay();
    }

    public void Reload()
    {
        Applications = [.. _context.Applications.OrderByDescending(a => a.LastUpdated)];
        RaiseFilteredChanged();
    }

    private void RaiseFilteredChanged()
    {
        OnPropertyChanged(nameof(FilteredApplications));
        OnPropertyChanged(nameof(StatusTally));
        OnPropertyChanged(nameof(ReviewCount));
        OnPropertyChanged(nameof(AvailableCompanies));
        OnPropertyChanged(nameof(AvailableCycles));
        OnPropertyChanged(nameof(AvailableSources));
        OnPropertyChanged(nameof(TimelineDomain));
    }

    partial void OnApplicationsChanged(List<JobApplication> value) => RaiseFilteredChanged();

    public List<JobApplication> FilteredApplications => [.. Applications.Where(Filters.Matches)];

    public Dictionary<ApplicationStatus, int> StatusTally =>
        FilteredApplications.GroupBy(a => a.Status).ToDictionary(g => g.Key, g => g.Count());

    public int ReviewCount => Applications.Count(a => a.NeedsReview);

    public List<string> AvailableCompanies =>
        [.. Applications.Select(a => a.Company).Where(c => c.Length > 0).Distinct()
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)];

    public List<string> AvailableCycles =>
        [.. Applications.Select(a => a.Cycle).Where(c => c.Length > 0).Distinct()
            .OrderBy(c => c, Comparer<string>.Create(CycleDetector.CompareDescending))];

    public List<string> AvailableSources =>
        [.. Applications.Select(a => a.Source).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().Distinct()
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)];

    public DateRange TimelineDomain => TimelineMath.Domain(Applications.Select(a => a.AppliedDate ?? a.LastUpdated));

    public List<(DateTimeOffset Month, int Count)> MonthHistogram() =>
        TimelineMath.MonthHistogram(Applications.Select(a => a.AppliedDate ?? a.LastUpdated), TimelineDomain);

    // MARK: Commands

    [RelayCommand]
    private async Task SyncNow() => await AppState.Pipeline.SyncNowAsync();

    [RelayCommand]
    private void ClearFilters() => Filters.Clear();

    // MARK: Board move (drag-drop)

    public void MoveToStatus(JobApplication app, ApplicationStatus status)
    {
        if (app.Status == status) return;
        var previous = app.Status;
        app.Status = status;
        app.LastUpdated = DateTimeOffset.UtcNow;

        var evt = EmailEvent.NoteOrStatusChange(EventKind.StatusChange, $"{previous.DisplayName()} → {status.DisplayName()}");
        evt.Application = app;
        _context.Events.Add(evt);
        _context.SaveChanges();
        RaiseFilteredChanged();
    }

    // MARK: Overlay visibility

    private CancellationTokenSource? _overlayHideCts;

    private void UpdateOverlay()
    {
        _overlayHideCts?.Cancel();
        switch (AppState.Pipeline.Stage)
        {
            case SyncStage.Idle:
                OverlayVisible = false;
                break;
            case SyncStage.Finished:
                OverlayVisible = true;
                ScheduleHide(TimeSpan.FromSeconds(2.2), () => AppState.Pipeline.Stage is SyncStage.Finished);
                break;
            case SyncStage.Failed:
                OverlayVisible = true;
                ScheduleHide(TimeSpan.FromSeconds(6), () => AppState.Pipeline.Stage is SyncStage.Failed);
                break;
            default:
                OverlayVisible = true;
                break;
        }
    }

    private void ScheduleHide(TimeSpan delay, Func<bool> stillApplies)
    {
        var cts = new CancellationTokenSource();
        _overlayHideCts = cts;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(delay, cts.Token); }
            catch (TaskCanceledException) { return; }
            if (!cts.IsCancellationRequested && stillApplies()) OverlayVisible = false;
        }, cts.Token);
    }
}
