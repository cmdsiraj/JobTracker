// A manually-added prospect: a job posting to apply to, or a person to
// reach out to. Lives in its own "Leads" section until acted on.

namespace JobTracker.Models;

public enum LeadType { JobPosting, Person }

public enum LeadStage { Todo, Contacted, Applied, Done }

public static class LeadExtensions
{
    public static string DisplayName(this LeadType type) => type switch
    {
        LeadType.JobPosting => "Job Posting",
        LeadType.Person => "Person / Contact",
        _ => type.ToString(),
    };

    public static string DisplayName(this LeadStage stage) => stage switch
    {
        LeadStage.Todo => "To Do",
        LeadStage.Contacted => "Contacted",
        LeadStage.Applied => "Applied",
        LeadStage.Done => "Done",
        _ => stage.ToString(),
    };

    public static string ColorKey(this LeadStage stage) => stage switch
    {
        LeadStage.Todo => "Orange",
        LeadStage.Contacted => "Teal",
        LeadStage.Applied => "Blue",
        LeadStage.Done => "Green",
        _ => "Gray",
    };
}

public class Lead
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Url { get; set; } = "";
    public string PersonName { get; set; } = "";
    public string TypeRaw { get; set; } = LeadType.JobPosting.ToString();
    public string StageRaw { get; set; } = LeadStage.Todo.ToString();
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    public Lead() { }

    public Lead(LeadType type = LeadType.JobPosting)
    {
        TypeRaw = type.ToString();
    }

    public LeadType Type
    {
        get => Enum.TryParse<LeadType>(TypeRaw, out var v) ? v : LeadType.JobPosting;
        set => TypeRaw = value.ToString();
    }

    public LeadStage Stage
    {
        get => Enum.TryParse<LeadStage>(StageRaw, out var v) ? v : LeadStage.Todo;
        set => StageRaw = value.ToString();
    }

    public Uri? LinkUrl
    {
        get
        {
            if (string.IsNullOrEmpty(Url)) return null;
            var value = Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Url : $"https://{Url}";
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
        }
    }
}
