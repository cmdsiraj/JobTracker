// The system tray popup: stage-aware sync status, recent updates, and
// quick actions. Windows equivalent of the macOS menu-bar popover.

using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public sealed partial class TrayViewModel : DispatcherObservableObject
{
    public AppState AppState { get; }
    private readonly JobTrackerDbContext _context;

    [ObservableProperty]
    private List<JobApplication> _recentApplications = [];

    public TrayViewModel(AppState appState)
    {
        AppState = appState;
        _context = appState.Context;
        Reload();
    }

    public void Reload()
    {
        // AsEnumerable(): EF Core's SQLite provider can't translate ORDER BY
        // over a DateTimeOffset column, so this sorts client-side.
        RecentApplications = [.. _context.Applications.AsEnumerable().OrderByDescending(a => a.LastUpdated).Take(6)];
    }

    public string StatusText
    {
        get
        {
            if (AppState.Pipeline.IsRunning) return AppState.Pipeline.Stage.ShortDescription();
            if (AppState.Pipeline.Stage is SyncStage.Failed failed) return $"Failed: {failed.Message}";
            return AppState.Auth.IsSignedIn ? "Up to date" : "Gmail not connected";
        }
    }

    public async Task SyncNow() => await AppState.Pipeline.SyncNowAsync();
}
