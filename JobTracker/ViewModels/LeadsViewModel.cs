// Manually-tracked prospects: job postings to apply to and people to reach
// out to. Leads advance through a lightweight stage flow and can be
// converted into full applications.

using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public sealed partial class LeadsViewModel : DispatcherObservableObject
{
    private readonly JobTrackerDbContext _context;

    [ObservableProperty]
    private List<Lead> _leads = [];

    [ObservableProperty]
    private LeadStage? _stageFilter;

    public LeadsViewModel(JobTrackerDbContext context)
    {
        _context = context;
        Reload();
    }

    public void Reload()
    {
        // AsEnumerable(): EF Core's SQLite provider can't translate ORDER BY
        // over a DateTimeOffset column, so this sorts client-side.
        Leads = [.. _context.Leads.AsEnumerable().OrderByDescending(l => l.LastUpdated)];
        OnPropertyChanged(nameof(FilteredLeads));
    }

    partial void OnStageFilterChanged(LeadStage? value) => OnPropertyChanged(nameof(FilteredLeads));

    public List<Lead> FilteredLeads => StageFilter is { } stage ? [.. Leads.Where(l => l.Stage == stage)] : Leads;

    public void SetStage(Lead lead, LeadStage stage)
    {
        lead.Stage = stage;
        lead.LastUpdated = DateTimeOffset.UtcNow;
        _context.SaveChanges();
        Reload();
    }

    public void Convert(Lead lead)
    {
        var app = new JobApplication(
            lead.Company,
            lead.Type == LeadType.Person && lead.Title.Length == 0 ? $"Outreach — {lead.PersonName}" : lead.Title,
            lead.Type == LeadType.Person ? ApplicationStatus.Outreach : ApplicationStatus.Applied);
        if (lead.Url.Length > 0) app.Source = lead.Url;
        if (lead.Notes.Length > 0) app.Notes = lead.Notes;
        _context.Applications.Add(app);
        _context.Leads.Remove(lead);
        _context.SaveChanges();
        Reload();
    }

    public void Delete(Lead lead)
    {
        _context.Leads.Remove(lead);
        _context.SaveChanges();
        Reload();
    }

    public void Add(LeadType type, string title, string company, string url, string personName, string notes)
    {
        var lead = new Lead(type)
        {
            Title = title.Trim(),
            Company = company.Trim(),
            Url = url.Trim(),
            PersonName = personName.Trim(),
            Notes = notes.Trim(),
        };
        _context.Leads.Add(lead);
        _context.SaveChanges();
        Reload();
    }
}
