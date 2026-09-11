// A single job application, aggregated from one or more Gmail threads.

using System.Text.RegularExpressions;

namespace JobTracker.Models;

public partial class JobApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Company { get; set; } = "";
    public string RoleTitle { get; set; } = "";

    /// Normalized company key ("Google LLC" → "google") maintained on every
    /// company edit; drives duplicate detection and matching.
    public string CompanyKey { get; set; } = "";

    public string StatusRaw { get; set; } = ApplicationStatus.Applied.ToRaw();

    public string? Location { get; set; }
    public string? Source { get; set; }

    /// Recruiting cycle, e.g. "Summer 2026", "Fall 2025", "New Grad 2026".
    public string Cycle { get; set; } = "";

    public DateTimeOffset? AppliedDate { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// Date of the newest *email* on this application. Status-advance
    /// decisions compare against this — not LastUpdated, which manual edits
    /// bump to "now" (that used to block all later email updates).
    public DateTimeOffset? LastEmailDate { get; set; }
    public string Notes { get; set; } = "";
    public string NextAction { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public string? ContactEmail { get; set; }

    /// Gmail thread ids that belong to this application (strongest match key).
    public List<string> ThreadIds { get; set; } = [];

    /// Set when the matcher attached an email with low confidence — surfaces
    /// the application in the "Needs Review" tray for manual merge/split.
    public bool NeedsReview { get; set; }

    public List<EmailEvent> Events { get; set; } = [];

    public JobApplication() { }

    public JobApplication(string company, string roleTitle, ApplicationStatus status = ApplicationStatus.Applied)
    {
        Company = company;
        RoleTitle = roleTitle;
        CompanyKey = NormalizeCompany(company);
        StatusRaw = status.ToRaw();
        LastUpdated = DateTimeOffset.UtcNow;
    }

    public ApplicationStatus Status
    {
        get => ApplicationStatusExtensions.FromRaw(StatusRaw);
        set => StatusRaw = value.ToRaw();
    }

    /// Events sorted newest-first, for the detail timeline.
    public IEnumerable<EmailEvent> SortedEvents => Events.OrderByDescending(e => e.ReceivedDate);

    // MARK: - Normalization

    private static readonly string[] CompanySuffixes =
    [
        ", inc.", ", inc", " inc.", " inc", ", llc", " llc",
        " corporation", " corp.", " corp", " company", " co.",
        " technologies", " technology", " labs", " ltd.", " ltd",
        " gmbh", " plc", " group",
    ];

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    /// Lowercases and strips legal/branding suffixes so "Google LLC",
    /// "Google, Inc." and "google" all collapse to the same key.
    public static string NormalizeCompany(string name)
    {
        var key = name.ToLowerInvariant().Replace("&", "and");
        foreach (var suffix in CompanySuffixes)
        {
            if (key.EndsWith(suffix, StringComparison.Ordinal))
            {
                key = key[..^suffix.Length];
            }
        }
        var tokens = NonAlphanumeric().Split(key).Where(t => t.Length > 0);
        return string.Join(" ", tokens);
    }
}
