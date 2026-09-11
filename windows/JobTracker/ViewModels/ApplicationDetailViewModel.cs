// Detail for a single application. Every edit persists instantly — there is
// no Save button. Includes the communication log (emails, notes, status
// changes), note capture, merge/split tools, and review clearing.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public sealed partial class ApplicationDetailViewModel : DispatcherObservableObject
{
    private readonly JobTrackerDbContext _context;
    public JobApplication Application { get; }

    [ObservableProperty]
    private string _newNote = "";

    /// Raised when the application (or a merge target) has been deleted /
    /// merged away — the View should close.
    public event Action? RequestClose;

    public ApplicationDetailViewModel(JobTrackerDbContext context, JobApplication application)
    {
        _context = context;
        Application = application;
    }

    public IEnumerable<EmailEvent> SortedEvents => Application.SortedEvents;

    /// All applications in the store, for the merge-target picker.
    public List<JobApplication> ContextApplications() =>
        [.. _context.Applications.OrderByDescending(a => a.LastUpdated)];

    public string TagsText
    {
        get => string.Join(", ", Application.Tags);
        set
        {
            Application.Tags = [.. value.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0)];
            Persist();
        }
    }

    /// Call after any bound field (Company, RoleTitle, Location, Source,
    /// Cycle, NextAction, Notes, Tags) changes.
    public void OnFieldChanged()
    {
        Application.CompanyKey = JobApplication.NormalizeCompany(Application.Company);
        Persist();
    }

    public void ChangeStatus(ApplicationStatus newStatus)
    {
        var old = Application.Status;
        if (old == newStatus) return;
        Application.Status = newStatus;
        var evt = EmailEvent.NoteOrStatusChange(EventKind.StatusChange, $"{old.DisplayName()} → {newStatus.DisplayName()}");
        evt.Application = Application;
        _context.Events.Add(evt);
        Persist();
    }

    [RelayCommand]
    private void MarkReviewed()
    {
        Application.NeedsReview = false;
        Persist();
    }

    [RelayCommand]
    private void AddNote()
    {
        var text = NewNote.Trim();
        if (text.Length == 0) return;
        var note = EmailEvent.NoteOrStatusChange(EventKind.Note, text);
        note.Application = Application;
        _context.Events.Add(note);
        NewNote = "";
        Persist();
        OnPropertyChanged(nameof(SortedEvents));
    }

    public void DeleteEvent(EmailEvent evt)
    {
        _context.Events.Remove(evt);
        _context.SaveChanges();
        OnPropertyChanged(nameof(SortedEvents));
    }

    /// Splits one event out into a brand-new application.
    public void Detach(EmailEvent evt)
    {
        var copy = new JobApplication(Application.Company, Application.RoleTitle, evt.DetectedStatus ?? Application.Status)
        {
            Cycle = Application.Cycle,
            NeedsReview = true,
        };
        _context.Applications.Add(copy);
        evt.Application = copy;
        if (evt.ThreadId.Length > 0)
        {
            Application.ThreadIds.Remove(evt.ThreadId);
            if (!copy.ThreadIds.Contains(evt.ThreadId)) copy.ThreadIds.Add(evt.ThreadId);
        }
        Persist();
        OnPropertyChanged(nameof(SortedEvents));
    }

    [RelayCommand]
    private void Delete()
    {
        _context.Applications.Remove(Application);
        _context.SaveChanges();
        RequestClose?.Invoke();
    }

    public void MergeInto(JobApplication target)
    {
        foreach (var evt in _context.Events.Where(e => e.ApplicationId == Application.Id))
        {
            evt.Application = target;
        }
        foreach (var threadId in Application.ThreadIds.Where(t => !target.ThreadIds.Contains(t)))
        {
            target.ThreadIds.Add(threadId);
        }
        if (Application.Notes.Length > 0)
        {
            target.Notes = target.Notes.Length == 0 ? Application.Notes : target.Notes + "\n\n" + Application.Notes;
        }
        target.LastUpdated = DateTimeOffset.UtcNow;
        _context.Applications.Remove(Application);
        _context.SaveChanges();
        RequestClose?.Invoke();
    }

    private void Persist()
    {
        Application.LastUpdated = DateTimeOffset.UtcNow;
        _context.SaveChanges();
    }
}
