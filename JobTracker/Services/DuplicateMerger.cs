// Post-sync cleanup: collapses applications that are really the same
// process — same company with token-similar roles (or one side missing a
// role, close in time). Runs automatically at the end of every sync/import
// and repairs duplicates that earlier, stricter matching created.

using JobTracker.Data;
using JobTracker.Models;

namespace JobTracker.Services;

public static class DuplicateMerger
{
    private static readonly TimeSpan ProximityWindow = TimeSpan.FromDays(120);

    /// Merges duplicates in place. Returns the number of applications merged away.
    public static int Run(JobTrackerDbContext context)
    {
        var applications = context.Applications
            .Select(a => a) // materialize below; Events loaded via Include where needed
            .ToList();
        var groups = applications
            .Where(a => !string.IsNullOrEmpty(a.CompanyKey))
            .GroupBy(a => a.CompanyKey);
        var merged = 0;

        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            var ordered = group
                .OrderBy(a => a.AppliedDate ?? a.LastUpdated)
                .ToList();

            // Pass 1 — records WITH roles: merge only on real role similarity.
            // Multiple distinct roles at one company (even applied the same
            // day) are separate applications and must never collapse.
            List<JobApplication> survivors = [];
            List<JobApplication> roleless = [];
            foreach (var app in ordered)
            {
                if (ApplicationMatcher.RoleTokens(app.RoleTitle).Count == 0)
                {
                    roleless.Add(app);
                    continue;
                }
                var target = survivors.FirstOrDefault(s => RolesMatch(s, app));
                if (target is not null)
                {
                    Merge(app, target, context);
                    merged++;
                }
                else
                {
                    survivors.Add(app);
                }
            }

            // Pass 2 — role-less records: merge only when the target is
            // UNAMBIGUOUS (exactly one candidate in the time window).
            // Ambiguous ones are kept and flagged for manual review instead
            // of being guessed into the wrong application.
            foreach (var app in roleless)
            {
                var anchor = app.AppliedDate ?? app.LastUpdated;
                var inWindow = survivors
                    .Where(s => ((s.AppliedDate ?? s.LastUpdated) - anchor).Duration() < ProximityWindow)
                    .ToList();
                if (inWindow.Count == 1)
                {
                    Merge(app, inWindow[0], context);
                    merged++;
                }
                else if (inWindow.Count > 1)
                {
                    app.NeedsReview = true;
                    survivors.Add(app);
                }
                else
                {
                    survivors.Add(app);
                }
            }
        }

        if (merged > 0)
        {
            context.SaveChanges();
            ActivityLog.Shared.Success($"Merged {merged} duplicate application(s)");
        }
        return merged;
    }

    /// Same company (guaranteed by grouping) + genuinely similar roles.
    private static bool RolesMatch(JobApplication a, JobApplication b) =>
        ApplicationMatcher.Similarity(
            ApplicationMatcher.RoleTokens(a.RoleTitle),
            ApplicationMatcher.RoleTokens(b.RoleTitle)) >= 0.55;

    private static void Merge(JobApplication source, JobApplication target, JobTrackerDbContext context)
    {
        // Move the communication log.
        foreach (var evt in context.Events.Where(e => e.ApplicationId == source.Id))
        {
            evt.Application = target;
        }

        // Union thread ids so future thread matches land on the survivor.
        foreach (var threadId in source.ThreadIds.Where(t => !target.ThreadIds.Contains(t)))
        {
            target.ThreadIds.Add(threadId);
        }

        // Prefer filled-in fields; keep the earliest applied date and the
        // status of whichever record heard from the company most recently.
        if (target.RoleTitle.Length == 0) target.RoleTitle = source.RoleTitle;
        if (target.Cycle.Length == 0) target.Cycle = source.Cycle;
        target.Location ??= source.Location;
        target.Source ??= source.Source;
        target.ContactEmail ??= source.ContactEmail;
        if (target.NextAction.Length == 0) target.NextAction = source.NextAction;
        foreach (var tag in source.Tags.Where(t => !target.Tags.Contains(t)))
        {
            target.Tags.Add(tag);
        }
        if (source.Notes.Length > 0)
        {
            target.Notes = target.Notes.Length == 0 ? source.Notes : target.Notes + "\n" + source.Notes;
        }

        target.AppliedDate = (target.AppliedDate, source.AppliedDate) switch
        {
            (null, null) => null,
            (var t, null) => t,
            (null, var s) => s,
            (var t, var s) => t <= s ? t : s,
        };

        if ((source.LastEmailDate ?? DateTimeOffset.MinValue) > (target.LastEmailDate ?? DateTimeOffset.MinValue))
        {
            target.LastEmailDate = source.LastEmailDate;
            if (source.Status != target.Status)
            {
                var change = EmailEvent.NoteOrStatusChange(EventKind.StatusChange,
                    $"{target.Status.DisplayName()} → {source.Status.DisplayName()}",
                    source.LastEmailDate ?? DateTimeOffset.UtcNow);
                change.Application = target;
                context.Events.Add(change);
                target.Status = source.Status;
            }
        }
        target.LastUpdated = target.LastUpdated > source.LastUpdated ? target.LastUpdated : source.LastUpdated;
        target.NeedsReview = target.NeedsReview || source.NeedsReview;

        var note = EmailEvent.NoteOrStatusChange(EventKind.Note,
            $"Merged duplicate entry ({source.Company} — {(source.RoleTitle.Length == 0 ? "no role" : source.RoleTitle)})");
        note.Application = target;
        context.Events.Add(note);

        context.Applications.Remove(source);
    }
}
