// Triage tray for applications where the matcher attached an email with low
// confidence. The user confirms the match, opens the detail to fix it, or
// deletes the application.

using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public sealed partial class ReviewViewModel : DispatcherObservableObject
{
    private readonly JobTrackerDbContext _context;

    [ObservableProperty]
    private List<JobApplication> _applications = [];

    [ObservableProperty]
    private int? _mergeResult;

    /// Raised when the user opens an application from this tray — the View
    /// opens the detail window.
    public event Action<JobApplication>? RequestOpen;

    public ReviewViewModel(JobTrackerDbContext context)
    {
        _context = context;
        Reload();
    }

    public void Reload()
    {
        Applications = [.. _context.Applications.Where(a => a.NeedsReview).OrderByDescending(a => a.LastUpdated)];
    }

    public static IEnumerable<EmailEvent> LowConfidenceEvents(JobApplication app) =>
        app.SortedEvents.Where(e => e.Kind == EventKind.Email && e.MatchConfidence < 0.9).Take(3);

    public void Confirm(JobApplication app)
    {
        app.NeedsReview = false;
        app.LastUpdated = DateTimeOffset.UtcNow;
        _context.SaveChanges();
        Reload();
    }

    public void Open(JobApplication app) => RequestOpen?.Invoke(app);

    public void Delete(JobApplication app)
    {
        _context.Applications.Remove(app);
        _context.SaveChanges();
        Reload();
    }

    public void MergeDuplicates()
    {
        var count = DuplicateMerger.Run(_context);
        MergeResult = count;
        Reload();
    }
}
