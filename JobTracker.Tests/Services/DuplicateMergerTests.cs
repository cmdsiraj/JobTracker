using JobTracker.Data;
using JobTracker.Models;
using JobTracker.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Tests.Services;

public class DuplicateMergerTests
{
    private static JobTrackerDbContext NewInMemoryContext(out SqliteConnection connection)
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<JobTrackerDbContext>().UseSqlite(connection).Options;
        var context = new JobTrackerDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void Merges_same_company_similar_role_duplicates()
    {
        using var context = NewInMemoryContext(out var connection);
        using (connection)
        {
            var older = new JobApplication("Stripe", "Software Engineer Intern")
            {
                AppliedDate = DateTimeOffset.UtcNow.AddDays(-10),
                Notes = "applied via referral",
            };
            var newer = new JobApplication("Stripe", "Software Engineering Intern")
            {
                AppliedDate = DateTimeOffset.UtcNow.AddDays(-2),
                Status = ApplicationStatus.RecruiterCall,
                LastEmailDate = DateTimeOffset.UtcNow.AddDays(-2),
            };
            context.Applications.AddRange(older, newer);
            context.SaveChanges();

            var mergedCount = DuplicateMerger.Run(context);

            Assert.Equal(1, mergedCount);
            var survivor = Assert.Single(context.Applications);
            Assert.Equal("applied via referral", survivor.Notes);
            Assert.Equal(ApplicationStatus.RecruiterCall, survivor.Status);
        }
    }

    [Fact]
    public void Does_not_merge_distinct_roles_at_same_company()
    {
        using var context = NewInMemoryContext(out var connection);
        using (connection)
        {
            context.Applications.AddRange(
                new JobApplication("Meta", "Data Scientist Intern"),
                new JobApplication("Meta", "Software Engineer Intern"));
            context.SaveChanges();

            var mergedCount = DuplicateMerger.Run(context);

            Assert.Equal(0, mergedCount);
            Assert.Equal(2, context.Applications.Count());
        }
    }

    [Fact]
    public void Ambiguous_roleless_duplicate_is_flagged_for_review_not_merged()
    {
        using var context = NewInMemoryContext(out var connection);
        using (connection)
        {
            var a = new JobApplication("Netflix", "Backend Engineer") { AppliedDate = DateTimeOffset.UtcNow.AddDays(-5) };
            var b = new JobApplication("Netflix", "Frontend Engineer") { AppliedDate = DateTimeOffset.UtcNow.AddDays(-4) };
            var roleless = new JobApplication("Netflix", "") { AppliedDate = DateTimeOffset.UtcNow.AddDays(-3) };
            context.Applications.AddRange(a, b, roleless);
            context.SaveChanges();

            var mergedCount = DuplicateMerger.Run(context);

            Assert.Equal(0, mergedCount);
            Assert.Equal(3, context.Applications.Count());
            Assert.True(context.Applications.Single(x => x.RoleTitle == "").NeedsReview);
        }
    }

    [Fact]
    public void Unambiguous_roleless_duplicate_merges_into_sole_candidate()
    {
        using var context = NewInMemoryContext(out var connection);
        using (connection)
        {
            var withRole = new JobApplication("Netflix", "Backend Engineer") { AppliedDate = DateTimeOffset.UtcNow.AddDays(-5) };
            var roleless = new JobApplication("Netflix", "") { AppliedDate = DateTimeOffset.UtcNow.AddDays(-3) };
            context.Applications.AddRange(withRole, roleless);
            context.SaveChanges();

            var mergedCount = DuplicateMerger.Run(context);

            Assert.Equal(1, mergedCount);
            Assert.Single(context.Applications);
        }
    }

    [Fact]
    public void Merge_moves_events_to_survivor()
    {
        using var context = NewInMemoryContext(out var connection);
        using (connection)
        {
            var older = new JobApplication("Stripe", "Software Engineer Intern") { AppliedDate = DateTimeOffset.UtcNow.AddDays(-10) };
            var newer = new JobApplication("Stripe", "Software Engineering Intern") { AppliedDate = DateTimeOffset.UtcNow.AddDays(-2) };
            context.Applications.AddRange(older, newer);
            // The merge algorithm sorts ascending by AppliedDate and keeps
            // the FIRST-seen record as the survivor, so "older" is the
            // target and "newer" is the one merged away — attach the event
            // to "newer" so this actually exercises the move-on-merge path.
            var evt = new EmailEvent("m1", "t1", DateTimeOffset.UtcNow, "hr@stripe.com", "s", "sn")
            {
                Application = newer,
            };
            context.Events.Add(evt);
            context.SaveChanges();

            DuplicateMerger.Run(context);

            var survivor = Assert.Single(context.Applications);
            var movedEvent = Assert.Single(context.Events.Where(e => e.GmailMessageId == "m1"));
            Assert.Equal(survivor.Id, movedEvent.ApplicationId);
        }
    }
}
