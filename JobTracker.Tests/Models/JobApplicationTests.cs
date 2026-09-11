using JobTracker.Models;

namespace JobTracker.Tests.Models;

public class JobApplicationTests
{
    [Theory]
    [InlineData("Google LLC", "google")]
    [InlineData("Google, Inc.", "google")]
    [InlineData("google", "google")]
    [InlineData("Stripe, Inc", "stripe")]
    [InlineData("Amazon Web Services", "amazon web services")]
    [InlineData("Meta & Co.", "meta and")]
    [InlineData("Acme Technologies", "acme")]
    [InlineData("Acme Corp.", "acme")]
    public void NormalizeCompany_collapses_legal_suffixes_and_casing(string input, string expected)
    {
        Assert.Equal(expected, JobApplication.NormalizeCompany(input));
    }

    [Fact]
    public void Status_round_trips_through_StatusRaw()
    {
        var app = new JobApplication("Stripe", "SWE") { Status = ApplicationStatus.Interview };
        Assert.Equal("Interview", app.StatusRaw);
        Assert.Equal(ApplicationStatus.Interview, app.Status);
    }

    [Fact]
    public void SortedEvents_orders_newest_first()
    {
        var app = new JobApplication("Stripe", "SWE");
        var older = new EmailEvent("m1", "t1", DateTimeOffset.UtcNow.AddDays(-2), "a@b.com", "s", "sn");
        var newer = new EmailEvent("m2", "t1", DateTimeOffset.UtcNow, "a@b.com", "s", "sn");
        app.Events.Add(older);
        app.Events.Add(newer);

        Assert.Equal([newer, older], app.SortedEvents);
    }
}
