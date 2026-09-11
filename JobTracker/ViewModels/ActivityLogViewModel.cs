// Live activity log window: shows what ingestion/sync is doing in real
// time, with auto-scroll, copy, and clear.

using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public sealed partial class ActivityLogViewModel : DispatcherObservableObject
{
    private readonly ActivityLog _log = ActivityLog.Shared;

    [ObservableProperty]
    private bool _autoScroll = true;

    public ActivityLogViewModel()
    {
        _log.Changed += OnLogChanged;
    }

    public IReadOnlyList<ActivityLog.Entry> Entries => _log.Entries;

    private void OnLogChanged() => OnPropertyChanged(nameof(Entries));

    public void Copy() => System.Windows.Clipboard.SetText(_log.Text);

    public void Clear() => _log.Clear();

    public static string ColorKeyFor(ActivityLog.Level level) => level switch
    {
        ActivityLog.Level.Info => "Secondary",
        ActivityLog.Level.Success => "Green",
        ActivityLog.Level.Warning => "Orange",
        ActivityLog.Level.Error => "Red",
        _ => "Secondary",
    };
}
