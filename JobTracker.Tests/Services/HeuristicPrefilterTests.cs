using JobTracker.Services;

namespace JobTracker.Tests.Services;

public class HeuristicPrefilterTests
{
    [Fact]
    public void Ats_sender_is_always_job_related()
    {
        Assert.True(HeuristicPrefilter.IsLikelyJobRelated(
            sender: "notifications@greenhouse.io", subject: "Random subject", body: "no keywords here"));
    }

    [Fact]
    public void Career_mailbox_sender_is_job_related()
    {
        Assert.True(HeuristicPrefilter.IsLikelyJobRelated(
            sender: "careers@stripe.com", subject: "Update", body: "..."));
    }

    [Fact]
    public void Strong_header_phrase_is_job_related()
    {
        Assert.True(HeuristicPrefilter.IsLikelyJobRelated(
            sender: "hr@acme.com", subject: "Thank you for applying to Acme", body: "..."));
    }

    [Fact]
    public void Job_alert_newsletter_is_never_job_related_even_from_ats_domain()
    {
        Assert.False(HeuristicPrefilter.IsLikelyJobRelated(
            sender: "jobs@linkedin.com", subject: "5 new jobs for you", body: "..."));
    }

    [Fact]
    public void Single_strong_body_phrase_is_not_enough()
    {
        Assert.False(HeuristicPrefilter.IsLikelyJobRelated(
            sender: "someone@example.com", subject: "Hello", body: "your application is important to us"));
    }

    [Fact]
    public void Two_strong_body_phrases_are_job_related()
    {
        Assert.True(HeuristicPrefilter.IsLikelyJobRelated(
            sender: "someone@example.com", subject: "Hello",
            body: "Thank you for applying. Our recruiting team will review your candidacy."));
    }

    [Fact]
    public void Generic_email_is_not_job_related()
    {
        Assert.False(HeuristicPrefilter.IsLikelyJobRelated(
            sender: "friend@example.com", subject: "Lunch?", body: "Want to grab lunch today?"));
    }

    [Fact]
    public void Outgoing_email_uses_lower_keyword_bar()
    {
        Assert.True(HeuristicPrefilter.IsLikelyJobRelated(
            sender: "me@example.com", subject: "Interested in the SWE role",
            body: "I saw the opening and wanted to reach out.", isOutgoing: true));
    }

    [Fact]
    public void Outgoing_email_without_keywords_is_not_job_related()
    {
        Assert.False(HeuristicPrefilter.IsLikelyJobRelated(
            sender: "me@example.com", subject: "Dinner plans",
            body: "See you at 7.", isOutgoing: true));
    }
}
