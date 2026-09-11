// A cheap keyword/sender check that runs BEFORE the LLM, so obvious
// non-job-related mail never costs an API call (protecting the free-tier
// rate limit and reducing how much private mail is sent out).
//
// Tuned against a real 14k-message Takeout archive: blanket domain matches
// for LinkedIn/Indeed admit thousands of job-alert newsletters, so matching
// is limited to ATS senders, career mailboxes, and phrases that indicate the
// recipient's own application.

namespace JobTracker.Services;

public static class HeuristicPrefilter
{
    /// Senders that almost always mean application activity: ATS platforms
    /// and company career mailboxes.
    private static readonly string[] AtsSenderPatterns =
    [
        "greenhouse.io", "lever.co", "myworkday", "workday", "icims.com",
        "smartrecruiters.com", "ashbyhq.com", "jobvite.com", "taleo.net",
        "successfactors.com", "bamboohr.com", "workable", "hired.com",
        "amazon.jobs", "gem.com", "rippling", "wellfound.com", "otta.com",
        "careers@", "jobs@", "recruiting@", "talent@", "recruitment@",
        "noreply@careers", "no-reply@careers", "@careers.", "@jobs.",
        "hackerrank", "codesignal", "hirevue", "karat.com", "codility",
    ];

    /// Phrases in sender+subject that strongly indicate the recipient's own
    /// application/hiring process (not job-alert newsletters).
    private static readonly string[] StrongHeaderPhrases =
    [
        "your application", "thank you for applying", "thanks for applying",
        "we received your", "we've received your", "application received",
        "application to", "application for", "application status",
        "application update", "you applied", "your candidacy", "candidate",
        "interview", "phone screen", "assessment", "coding challenge",
        "online assessment", "next steps", "offer", "we regret",
        "unfortunately", "not moving forward", "moving forward",
        "requisition", "started your job application",
    ];

    /// Strong body phrases; two or more are required when the header alone
    /// isn't conclusive.
    private static readonly string[] StrongBodyPhrases =
    [
        "thank you for applying", "thanks for applying",
        "we received your application", "your application",
        "interview process", "schedule your interview", "phone screen",
        "online assessment", "coding challenge", "your candidacy",
        "we regret to inform", "not be moving forward", "not moving forward",
        "offer letter", "position you applied", "recruiting team",
        "talent acquisition", "hiring team", "hiring manager",
    ];

    /// Keywords that mark an outgoing (user-sent) email as plausibly
    /// job-related. Outgoing mail gets a deliberately lower bar: the user's
    /// own outreach rarely matches ATS/confirmation phrasing.
    private static readonly string[] OutgoingKeywords =
    [
        "position", "role", "opportunity", "application", "applying",
        "resume", "cv", "internship", "recruiter", "referral",
        "interested in", "opening",
    ];

    /// Job-alert / job-digest newsletters are never the user's own
    /// applications, no matter the sender.
    private static readonly string[] AlertMarkers =
    [
        "job alert", "jobs for you", "new jobs", "recommended jobs",
        "job recommendations", "jobs you may", "daily digest",
        "weekly digest", "trending jobs", "hiring now", "job matches",
    ];

    /// Direction-aware entry point. Outgoing messages pass if the subject or
    /// the first part of the body mentions any job-search keyword; incoming
    /// messages use the stricter ATS/phrase logic below.
    public static bool IsLikelyJobRelated(string sender, string subject, string body, bool isOutgoing)
    {
        if (!isOutgoing) return IsLikelyJobRelated(sender, subject, body);
        var text = $"{subject} {Truncate(body, 1500)}".ToLowerInvariant();
        return OutgoingKeywords.Any(text.Contains);
    }

    public static bool IsLikelyJobRelated(string sender, string subject, string body)
    {
        var senderLower = sender.ToLowerInvariant();
        var headerLower = $"{sender} {subject}".ToLowerInvariant();

        if (AlertMarkers.Any(headerLower.Contains)) return false;

        if (AtsSenderPatterns.Any(senderLower.Contains)) return true;

        if (StrongHeaderPhrases.Any(headerLower.Contains)) return true;

        var bodyHead = Truncate(body, 1500).ToLowerInvariant();
        var hits = StrongBodyPhrases.Count(bodyHead.Contains);
        return hits >= 2;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
