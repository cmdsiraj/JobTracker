// Manual entry: create a brand-new application, or log an update (note +
// optional status change) on an existing one — for anything that didn't
// arrive by email: phone calls, LinkedIn messages, career-fair chats,
// "process paused", and so on.

using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public enum NewApplicationMode { NewApplication, Update }

public sealed partial class NewApplicationViewModel : DispatcherObservableObject
{
    private readonly JobTrackerDbContext _context;

    [ObservableProperty]
    private NewApplicationMode _mode = NewApplicationMode.NewApplication;

    // New application fields
    [ObservableProperty] private string _company = "";
    [ObservableProperty] private string _role = "";
    [ObservableProperty] private ApplicationStatus _status = ApplicationStatus.Applied;
    [ObservableProperty] private string _cycle = "";
    [ObservableProperty] private string _location = "";
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private DateTimeOffset _appliedDate = DateTimeOffset.Now;
    [ObservableProperty] private string _notes = "";

    // Update fields
    [ObservableProperty] private JobApplication? _targetApp;
    [ObservableProperty] private string _updateText = "";
    [ObservableProperty] private ApplicationStatus? _newStatus;

    public List<JobApplication> Applications { get; }

    public event Action? Saved;

    public NewApplicationViewModel(JobTrackerDbContext context)
    {
        _context = context;
        Applications = [.. context.Applications.OrderByDescending(a => a.LastUpdated)];
    }

    public bool CanSave => Mode == NewApplicationMode.NewApplication
        ? Company.Trim().Length > 0
        : TargetApp is not null && (UpdateText.Trim().Length > 0 || NewStatus is not null);

    public void Save()
    {
        if (Mode == NewApplicationMode.NewApplication)
        {
            var app = new JobApplication(Company.Trim(), Role.Trim(), Status)
            {
                Cycle = Cycle.Trim(),
                Location = Location.Length == 0 ? null : Location,
                Source = Source.Length == 0 ? null : Source,
                AppliedDate = AppliedDate,
                LastUpdated = AppliedDate,
                Notes = Notes,
            };
            _context.Applications.Add(app);

            var created = EmailEvent.NoteOrStatusChange(EventKind.Note, "Added manually", AppliedDate);
            created.Application = app;
            _context.Events.Add(created);
        }
        else if (TargetApp is { } app)
        {
            if (UpdateText.Trim().Length > 0)
            {
                var note = EmailEvent.NoteOrStatusChange(EventKind.Note, UpdateText.Trim());
                note.Application = app;
                _context.Events.Add(note);
            }
            if (NewStatus is { } newStatus && newStatus != app.Status)
            {
                var change = EmailEvent.NoteOrStatusChange(EventKind.StatusChange,
                    $"{app.Status.DisplayName()} → {newStatus.DisplayName()}");
                change.Application = app;
                _context.Events.Add(change);
                app.Status = newStatus;
            }
            app.LastUpdated = DateTimeOffset.UtcNow;
        }
        _context.SaveChanges();
        Saved?.Invoke();
    }
}
