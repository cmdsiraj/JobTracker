using JobTracker.Data;
using JobTracker.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Tests.Data;

public class JobTrackerDbContextTests
{
    private static JobTrackerDbContext NewInMemoryContext(out SqliteConnection connection)
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<JobTrackerDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new JobTrackerDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public void RoundTrips_application_with_tags_and_thread_ids()
    {
        using var context = NewInMemoryContext(out var connection);
        using (connection)
        {
            var app = new JobApplication("Stripe", "SWE Intern", ApplicationStatus.Applied)
            {
                Tags = ["priority", "remote"],
                ThreadIds = ["abc123", "def456"],
            };
            context.Applications.Add(app);
            context.SaveChanges();

            using var reload = new JobTrackerDbContext(
                new DbContextOptionsBuilder<JobTrackerDbContext>().UseSqlite(connection).Options);
            var loaded = reload.Applications.Single(a => a.Id == app.Id);

            Assert.Equal(["priority", "remote"], loaded.Tags);
            Assert.Equal(["abc123", "def456"], loaded.ThreadIds);
            Assert.Equal(ApplicationStatus.Applied, loaded.Status);
        }
    }

    [Fact]
    public void Deleting_application_cascades_to_events()
    {
        using var context = NewInMemoryContext(out var connection);
        using (connection)
        {
            var app = new JobApplication("Google", "SWE");
            var evt = new EmailEvent("msg1", "thread1", DateTimeOffset.UtcNow, "hr@google.com", "Subject", "Snippet")
            {
                Application = app,
            };
            context.Applications.Add(app);
            context.Events.Add(evt);
            context.SaveChanges();

            context.Applications.Remove(app);
            context.SaveChanges();

            Assert.Empty(context.Events);
        }
    }
}
