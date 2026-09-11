using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Tests.Services;

public class SyncPipelineTests
{
    private static SyncPipeline NewPipeline(out JobTrackerDbContext context, out SqliteConnection connection)
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<JobTrackerDbContext>().UseSqlite(connection).Options;
        context = new JobTrackerDbContext(options);
        context.Database.EnsureCreated();
        return new SyncPipeline(context, new GmailAuthService(), Preferences.Shared);
    }

    private static FetchedMessage Message(string id, string threadId, string sender, string subject,
        string snippet, DateTimeOffset date, bool isOutgoing = false) =>
        new(id, threadId, sender, subject, snippet, "body", date, isOutgoing);

    private static ClassificationResult Result(string company, string role, string status,
        double confidence = 0.9, string? cycle = null) => new()
    {
        IsJobRelated = true,
        Company = company,
        Role = role,
        Status = status,
        Confidence = confidence,
        Cycle = cycle,
    };

    [Fact]
    public void Upsert_creates_new_application_for_unmatched_message()
    {
        var pipeline = NewPipeline(out var context, out var connection);
        using (connection)
        {
            var matcher = new ApplicationMatcher(context.Applications.ToList());
            var date = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
            var message = Message("m1", "t1", "careers@stripe.com", "Thank you for applying", "snippet", date);

            pipeline.Upsert(message, Result("Stripe", "SWE Intern", "applied"), matcher);
            context.SaveChanges();

            var app = Assert.Single(context.Applications);
            Assert.Equal("Stripe", app.Company);
            Assert.Equal("stripe", app.CompanyKey);
            Assert.Equal(ApplicationStatus.Applied, app.Status);
            Assert.Contains("t1", app.ThreadIds);
            Assert.Single(context.Events);
        }
    }

    [Fact]
    public void Upsert_derives_company_from_sender_when_llm_gives_none()
    {
        var pipeline = NewPipeline(out var context, out var connection);
        using (connection)
        {
            var matcher = new ApplicationMatcher([]);
            var message = Message("m1", "t1", "careers@stripe.com", "Thanks", "snippet", DateTimeOffset.UtcNow);

            pipeline.Upsert(message, Result("", "SWE Intern", "applied"), matcher);
            context.SaveChanges();

            var app = Assert.Single(context.Applications);
            Assert.Equal("Stripe", app.Company);
        }
    }

    [Fact]
    public void Upsert_skips_message_with_no_identifiable_company_or_role()
    {
        var pipeline = NewPipeline(out var context, out var connection);
        using (connection)
        {
            var matcher = new ApplicationMatcher([]);
            var message = Message("m1", "t1", "noreply@linkedin.com", "Digest", "snippet", DateTimeOffset.UtcNow);

            pipeline.Upsert(message, Result("", "", "applied"), matcher);
            context.SaveChanges();

            Assert.Empty(context.Applications);
        }
    }

    [Fact]
    public void Upsert_advances_status_on_newer_email_and_logs_status_change_event()
    {
        var pipeline = NewPipeline(out var context, out var connection);
        using (connection)
        {
            var app = new JobApplication("Stripe", "SWE Intern") { LastEmailDate = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero) };
            context.Applications.Add(app);
            context.SaveChanges();

            var matcher = new ApplicationMatcher([app]);
            var message = Message("m2", "", "recruiter@stripe.com", "Interview invite", "snippet",
                new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero));

            pipeline.Upsert(message, Result("Stripe", "SWE Intern", "interview"), matcher);
            context.SaveChanges();

            Assert.Equal(ApplicationStatus.Interview, app.Status);
            Assert.Contains(context.Events, e => e.Kind == EventKind.StatusChange);
        }
    }

    [Fact]
    public void Upsert_does_not_regress_status_from_an_older_email()
    {
        var pipeline = NewPipeline(out var context, out var connection);
        using (connection)
        {
            var app = new JobApplication("Stripe", "SWE Intern", ApplicationStatus.Interview)
            {
                LastEmailDate = new DateTimeOffset(2026, 5, 5, 0, 0, 0, TimeSpan.Zero),
            };
            context.Applications.Add(app);
            context.SaveChanges();

            var matcher = new ApplicationMatcher([app]);
            // An older email (e.g. fetched out of order) claiming "applied".
            var message = Message("m2", "", "careers@stripe.com", "Application received", "snippet",
                new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));

            pipeline.Upsert(message, Result("Stripe", "SWE Intern", "applied"), matcher);

            Assert.Equal(ApplicationStatus.Interview, app.Status);
        }
    }

    [Fact]
    public void Upsert_falls_back_to_cycle_detector_when_llm_gives_no_cycle()
    {
        var pipeline = NewPipeline(out var context, out var connection);
        using (connection)
        {
            var matcher = new ApplicationMatcher([]);
            var message = Message("m1", "t1", "careers@stripe.com", "Summer 2026 Internship — Thank you for applying",
                "snippet", DateTimeOffset.UtcNow);

            pipeline.Upsert(message, Result("Stripe", "SWE Intern", "applied", cycle: null), matcher);
            context.SaveChanges();

            var app = Assert.Single(context.Applications);
            Assert.Equal("Summer 2026", app.Cycle);
        }
    }

    [Fact]
    public void Upsert_flags_needs_review_when_matcher_confidence_is_below_threshold()
    {
        var pipeline = NewPipeline(out var context, out var connection);
        using (connection)
        {
            var existing = new JobApplication("Netflix", "") { LastEmailDate = DateTimeOffset.UtcNow.AddDays(-1) };
            context.Applications.Add(existing);
            context.SaveChanges();

            var matcher = new ApplicationMatcher([existing]);
            var message = Message("m1", "", "talent@netflix.com", "Update", "snippet", DateTimeOffset.UtcNow);

            pipeline.Upsert(message, Result("Netflix", "", "applied", confidence: 0.9), matcher);

            Assert.True(existing.NeedsReview);
        }
    }

    [Fact]
    public void CurrentCycleStart_is_august_first_of_the_active_cycle_year()
    {
        var start = PendingImport.CurrentCycleStart;
        Assert.Equal(8, start.Month);
        Assert.Equal(1, start.Day);
    }

    [Fact]
    public void ImportScope_CurrentCycle_filters_to_messages_on_or_after_cycle_start()
    {
        var before = PendingImport.CurrentCycleStart.AddDays(-1);
        var after = PendingImport.CurrentCycleStart.AddDays(1);
        var messages = new List<FetchedMessage>
        {
            Message("a", "", "x@y.com", "s", "sn", new DateTimeOffset(before)),
            Message("b", "", "x@y.com", "s", "sn", new DateTimeOffset(after)),
        };

        var filtered = ImportScope.CurrentCycle.Filter(messages);

        Assert.Single(filtered);
        Assert.Equal("b", filtered[0].Id);
    }
}
