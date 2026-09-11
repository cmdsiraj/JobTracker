// Groups classified emails into job applications using layered signals:
//
//   1. Gmail thread id                     → certain match  (1.0)
//   2. same company + similar role         → strong match   (0.9)
//   3. same company + partial role overlap → probable       (0.75, review)
//   4. same company + a missing role,
//      close in time                       → probable       (0.7, review)
//   5. known sender address                → weak            (0.6, review)
//
// Role comparison is token-based (season/year tokens stripped) so
// "Software Engineer Intern" matches "Software Engineering Intern —
// Summer 2026". Company keys match exactly or by whole-token containment
// ("amazon" ⊂ "amazon web services").
//
// Indexes are built once per sync session and updated incrementally —
// including thread ids attached to existing applications mid-run, so the
// second email of a thread always matches at 1.0.

using System.Text.RegularExpressions;
using JobTracker.Models;

namespace JobTracker.Services;

public sealed partial class ApplicationMatcher
{
    public sealed record Match(JobApplication Application, double Confidence)
    {
        public bool NeedsReview => Confidence < 0.9;
    }

    /// How close in time a role-less email must be to an application with
    /// the same company to be considered the same process.
    private static readonly TimeSpan ProximityWindow = TimeSpan.FromDays(120);

    private readonly Dictionary<string, JobApplication> _byThread = [];
    private readonly Dictionary<string, List<JobApplication>> _byCompany = [];
    private readonly Dictionary<string, JobApplication> _bySender = [];

    public ApplicationMatcher(IEnumerable<JobApplication> existing)
    {
        foreach (var application in existing) Register(application);
    }

    /// Adds a new application to the session indexes.
    public void Register(JobApplication application)
    {
        foreach (var threadId in application.ThreadIds.Where(t => !string.IsNullOrEmpty(t)))
        {
            _byThread[threadId] = application;
        }
        if (!string.IsNullOrEmpty(application.CompanyKey))
        {
            if (!_byCompany.TryGetValue(application.CompanyKey, out var list))
            {
                list = [];
                _byCompany[application.CompanyKey] = list;
            }
            list.Add(application);
        }
        var contact = application.ContactEmail is not null ? EmailAddress(application.ContactEmail) : null;
        if (contact is not null) _bySender[contact] = application;
    }

    /// Records that `threadId` now belongs to `application` — called
    /// whenever the pipeline appends a thread to an existing application so
    /// follow-ups in that thread match at 1.0 instead of re-running fuzzy
    /// matching (the old behavior caused duplicates).
    public void Associate(string threadId, JobApplication application)
    {
        if (string.IsNullOrEmpty(threadId)) return;
        _byThread[threadId] = application;
    }

    // MARK: - Matching

    public Match? MatchMessage(FetchedMessage message, ClassificationResult result)
    {
        // 1. Same Gmail thread — same application, always.
        if (!string.IsNullOrEmpty(message.ThreadId) && _byThread.TryGetValue(message.ThreadId, out var byThread))
        {
            return new Match(byThread, 1.0);
        }

        var companyKey = JobApplication.NormalizeCompany(result.Company);
        var (candidates, exactCompany) = CompanyCandidates(companyKey);

        if (candidates.Count > 0)
        {
            var incomingTokens = RoleTokens(result.Role);

            // 2./3. Role-similarity ranking within the company.
            (JobApplication App, double Similarity)? bestSimilar = null;
            List<JobApplication> windowCandidates = [];
            foreach (var candidate in candidates)
            {
                var candidateTokens = RoleTokens(candidate.RoleTitle);
                if (incomingTokens.Count == 0 || candidateTokens.Count == 0)
                {
                    // 4. A side lacks a role: candidate only if close in time.
                    var anchor = candidate.LastEmailDate ?? candidate.LastUpdated;
                    if ((anchor - message.Date).Duration() < ProximityWindow)
                    {
                        windowCandidates.Add(candidate);
                    }
                    continue;
                }
                var similarity = Similarity(incomingTokens, candidateTokens);
                if (similarity > (bestSimilar?.Similarity ?? 0))
                {
                    bestSimilar = (candidate, similarity);
                }
            }

            if (bestSimilar is { } best)
            {
                if (best.Similarity >= 0.6)
                {
                    // Same role in different words. Exact-company gets full
                    // strength; containment-matched company keeps review.
                    return new Match(best.App, exactCompany ? 0.9 : 0.75);
                }
                if (best.Similarity >= 0.35)
                {
                    return new Match(best.App, 0.75);
                }
            }

            // Role-less matching must respect multiple parallel applications
            // at the same company (e.g. several roles applied the same day):
            // unambiguous single candidate attaches; multiple candidates
            // attach to the nearest-in-time but flagged for review so the
            // user can detach if the guess is wrong.
            if (incomingTokens.Count == 0 || candidates.All(c => RoleTokens(c.RoleTitle).Count == 0))
            {
                if (windowCandidates.Count == 1)
                {
                    return new Match(windowCandidates[0], 0.7);
                }
                if (windowCandidates.Count > 1)
                {
                    var nearest = windowCandidates.MinBy(c =>
                    {
                        var anchor = c.LastEmailDate ?? c.LastUpdated;
                        return (anchor - message.Date).Duration();
                    });
                    if (nearest is not null) return new Match(nearest, 0.6);
                }
            }
            // Distinct roles at the same company → genuinely separate
            // applications; fall through to create a new one.
        }

        // 5. Known recruiter/sender address, when the model found no company.
        if (companyKey.Length == 0)
        {
            var sender = EmailAddress(message.Sender);
            if (sender is not null && _bySender.TryGetValue(sender, out var bySender))
            {
                return new Match(bySender, 0.6);
            }
        }

        return null;
    }

    /// Applications at the same company: exact key match, else whole-token
    /// containment ("amazon" ⊂ "amazon web services", either direction).
    private (List<JobApplication> Apps, bool ExactCompany) CompanyCandidates(string key)
    {
        if (key.Length == 0) return ([], false);
        if (_byCompany.TryGetValue(key, out var exact)) return (exact, true);

        var tokens = key.Split(' ').ToHashSet();
        List<JobApplication> contained = [];
        foreach (var (otherKey, apps) in _byCompany)
        {
            var otherTokens = otherKey.Split(' ').ToHashSet();
            if (tokens.IsSubsetOf(otherTokens) || otherTokens.IsSubsetOf(tokens))
            {
                contained.AddRange(apps);
            }
        }
        return (contained, false);
    }

    // MARK: - Role similarity

    /// Tokens that describe the cycle, not the role, and connective noise.
    /// NOTE: "intern"/"grad" are deliberately KEPT — an internship and a
    /// full-time role at the same company are different applications.
    private static readonly HashSet<string> NoiseTokens =
    [
        "summer", "fall", "autumn", "winter", "spring",
        "the", "a", "an", "of", "and", "for", "at", "in", "position", "role",
    ];

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    /// Lowercased word tokens with cycle years/seasons and noise removed.
    public static HashSet<string> RoleTokens(string role)
    {
        var lower = role.ToLowerInvariant();
        var words = NonAlphanumeric().Split(lower).Where(w => w.Length > 0);
        return words
            .Where(w => !NoiseTokens.Contains(w))
            .Where(w => !(w.Length == 4 && w.StartsWith("20") && int.TryParse(w, out _)))
            .Select(w => w.EndsWith("ing") && w.Length > 6 ? w[..^3] : w)
            .ToHashSet();
    }

    /// Jaccard similarity of two token sets.
    public static double Similarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return (double)intersection / union;
    }

    // MARK: - Sender helpers

    /// Extracts "user@host" from a From header like "Name <user@host>".
    public static string? EmailAddress(string from)
    {
        var open = from.LastIndexOf('<');
        var close = from.LastIndexOf('>');
        if (open >= 0 && close >= 0 && open < close)
        {
            return from[(open + 1)..close].ToLowerInvariant();
        }
        var trimmed = from.Trim().ToLowerInvariant();
        return trimmed.Contains('@') ? trimmed : null;
    }

    /// Mail providers and ATS platforms whose domain never names the
    /// hiring company.
    private static readonly HashSet<string> GenericDomains =
    [
        "gmail", "googlemail", "outlook", "hotmail", "yahoo", "icloud", "aol",
        "greenhouse", "lever", "myworkday", "workday", "icims",
        "smartrecruiters", "ashbyhq", "jobvite", "taleo", "successfactors",
        "bamboohr", "workable", "linkedin", "indeed", "ziprecruiter",
        "hackerrank", "codesignal", "hirevue", "karat", "codility",
        "workablemail", "candidates", "notifications", "mail", "email", "no-reply",
    ];

    /// Best-effort company name from a sender address, e.g.
    /// "careers@stripe.com" → "Stripe". Null for generic/ATS domains.
    public static string? CompanyFromSender(string from)
    {
        var address = EmailAddress(from);
        if (address is null) return null;
        var atIndex = address.IndexOf('@');
        if (atIndex < 0) return null;
        var host = address[(atIndex + 1)..];
        var labels = host.Split('.').Where(l => l.Length > 0).ToList();
        if (labels.Count < 2) return null;
        var label = labels[^2];
        if (label.Length <= 1 || GenericDomains.Contains(label) || labels.Any(GenericDomains.Contains))
        {
            return null;
        }
        return char.ToUpperInvariant(label[0]) + label[1..];
    }
}
