// The full pipeline of a job application, including proactive outreach.
// Stored as a string on JobApplication.StatusRaw (matches the Swift model's
// CloudKit-friendly string backing, kept here purely for parity/porting
// convenience — SQLite has no such constraint).

namespace JobTracker.Models;

public enum ApplicationStatus
{
    Outreach,      // user reached out (cold email, referral ask)
    Applied,
    Assessment,    // online assessment / take-home
    RecruiterCall, // recruiter screen / intro call
    Interview,     // technical interview rounds
    FinalRound,    // onsite / final loop
    Offer,
    Rejected,
}

public static class ApplicationStatusExtensions
{
    /// Columns shown on the Kanban board, in order.
    public static readonly ApplicationStatus[] BoardColumns =
    [
        ApplicationStatus.Outreach,
        ApplicationStatus.Applied,
        ApplicationStatus.Assessment,
        ApplicationStatus.RecruiterCall,
        ApplicationStatus.Interview,
        ApplicationStatus.FinalRound,
        ApplicationStatus.Offer,
        ApplicationStatus.Rejected,
    ];

    /// Stages used in the analytics funnel, in pipeline order.
    public static readonly ApplicationStatus[] FunnelStages =
    [
        ApplicationStatus.Applied,
        ApplicationStatus.Assessment,
        ApplicationStatus.RecruiterCall,
        ApplicationStatus.Interview,
        ApplicationStatus.FinalRound,
        ApplicationStatus.Offer,
    ];

    public static string DisplayName(this ApplicationStatus status) => status switch
    {
        ApplicationStatus.Outreach => "Outreach",
        ApplicationStatus.Applied => "Applied",
        ApplicationStatus.Assessment => "Assessment",
        ApplicationStatus.RecruiterCall => "Recruiter Call",
        ApplicationStatus.Interview => "Interview",
        ApplicationStatus.FinalRound => "Final Round",
        ApplicationStatus.Offer => "Offer",
        ApplicationStatus.Rejected => "Rejected",
        _ => status.ToString(),
    };

    /// Higher means further along the pipeline. Used to decide whether a
    /// newer email should advance an application's status.
    public static int Rank(this ApplicationStatus status) => status switch
    {
        ApplicationStatus.Outreach => 0,
        ApplicationStatus.Applied => 1,
        ApplicationStatus.Assessment => 2,
        ApplicationStatus.RecruiterCall => 3,
        ApplicationStatus.Interview => 4,
        ApplicationStatus.FinalRound => 5,
        ApplicationStatus.Offer => 6,
        ApplicationStatus.Rejected => 7,
        _ => 0,
    };

    /// True while the application is still in play.
    public static bool IsActive(this ApplicationStatus status) =>
        status is not (ApplicationStatus.Offer or ApplicationStatus.Rejected);

    /// Named color key consumed by StatusColorConverter in XAML.
    public static string ColorKey(this ApplicationStatus status) => status switch
    {
        ApplicationStatus.Outreach => "Orange",
        ApplicationStatus.Applied => "Blue",
        ApplicationStatus.Assessment => "Cyan",
        ApplicationStatus.RecruiterCall => "Teal",
        ApplicationStatus.Interview => "Purple",
        ApplicationStatus.FinalRound => "Indigo",
        ApplicationStatus.Offer => "Green",
        ApplicationStatus.Rejected => "Red",
        _ => "Gray",
    };

    /// Plain-Unicode glyph (renders with the default Segoe UI / Segoe UI
    /// Emoji fallback on any Windows 10+ box, unlike private-use icon-font
    /// codepoints which need a specific icon font installed).
    public static string IconGlyph(this ApplicationStatus status) => status switch
    {
        ApplicationStatus.Outreach => "↗",       // ↗ north east arrow
        ApplicationStatus.Applied => "✉",        // ✉ envelope
        ApplicationStatus.Assessment => "⌨",     // ⌨ keyboard
        ApplicationStatus.RecruiterCall => "☎",  // ☎ telephone
        ApplicationStatus.Interview => "▤",      // ▤ bars (people stand-in)
        ApplicationStatus.FinalRound => "\U0001F3C1", // 🏁 checkered flag
        ApplicationStatus.Offer => "✓",          // ✓ check mark
        ApplicationStatus.Rejected => "✕",       // ✕ multiplication x
        _ => "●",                                // ●
    };

    public static string ToRaw(this ApplicationStatus status) => status.ToString();

    public static ApplicationStatus FromRaw(string? raw) =>
        Enum.TryParse<ApplicationStatus>(raw, out var value) ? value : ApplicationStatus.Applied;

    /// Maps the strings the LLM returns (and v1 statuses) onto the pipeline.
    public static ApplicationStatus? FromLlmString(string raw)
    {
        return raw.Trim().ToLowerInvariant() switch
        {
            "outreach" or "cold email" or "reached out" or "follow-up" or "followup" => ApplicationStatus.Outreach,
            "applied" or "application" or "submitted" => ApplicationStatus.Applied,
            "assessment" or "oa" or "online assessment" or "take-home" or "takehome" or "coding challenge"
                => ApplicationStatus.Assessment,
            "screening" or "recruiter" or "recruiter call" or "phone screen" or "recruitercall"
                => ApplicationStatus.RecruiterCall,
            "interview" or "technical" or "technical interview" => ApplicationStatus.Interview,
            "final" or "final round" or "finalround" or "onsite" => ApplicationStatus.FinalRound,
            "offer" => ApplicationStatus.Offer,
            "rejected" or "rejection" or "declined" => ApplicationStatus.Rejected,
            _ => null,
        };
    }
}
