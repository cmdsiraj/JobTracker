using JobTracker.Models;
using JobTracker.Services;

namespace JobTracker.Tests.Services;

public class ApplicationMatcherTests
{
    private static FetchedMessage Message(string threadId, string sender, DateTimeOffset date) =>
        new("id-" + Guid.NewGuid(), threadId, sender, "Subject", "Snippet", "Body", date);

    private static ClassificationResult Result(string company, string role) => new()
    {
        IsJobRelated = true,
        Company = company,
        Role = role,
        Status = "applied",
        Confidence = 0.9,
    };

    [Fact]
    public void Same_thread_matches_at_full_confidence()
    {
        var existing = new JobApplication("Stripe", "SWE Intern") { ThreadIds = ["thread-1"] };
        var matcher = new ApplicationMatcher([existing]);

        var match = matcher.MatchMessage(Message("thread-1", "hr@stripe.com", DateTimeOffset.UtcNow),
            Result("Stripe", "SWE Intern"));

        Assert.NotNull(match);
        Assert.Equal(1.0, match!.Confidence);
        Assert.False(match.NeedsReview);
        Assert.Same(existing, match.Application);
    }

    [Fact]
    public void Same_company_similar_role_matches_at_high_confidence_without_review()
    {
        var existing = new JobApplication("Google", "Software Engineer Intern");
        var matcher = new ApplicationMatcher([existing]);

        var match = matcher.MatchMessage(Message("thread-2", "recruiting@google.com", DateTimeOffset.UtcNow),
            Result("Google", "Software Engineering Intern — Summer 2026"));

        Assert.NotNull(match);
        Assert.Equal(0.9, match!.Confidence);
        Assert.False(match.NeedsReview);
    }

    [Fact]
    public void Containment_matched_company_keeps_review_even_with_similar_role()
    {
        var existing = new JobApplication("Amazon", "Software Engineer Intern");
        var matcher = new ApplicationMatcher([existing]);

        var match = matcher.MatchMessage(Message("thread-3", "hr@amazon.jobs", DateTimeOffset.UtcNow),
            Result("Amazon Web Services", "Software Engineer Intern"));

        Assert.NotNull(match);
        Assert.Equal(0.75, match!.Confidence);
        Assert.True(match.NeedsReview);
    }

    [Fact]
    public void Distinct_roles_at_same_company_do_not_match()
    {
        var existing = new JobApplication("Meta", "Data Scientist Intern");
        var matcher = new ApplicationMatcher([existing]);

        var match = matcher.MatchMessage(Message("thread-4", "hr@meta.com", DateTimeOffset.UtcNow),
            Result("Meta", "Software Engineer Intern"));

        Assert.Null(match);
    }

    [Fact]
    public void Roleless_email_matches_unambiguous_same_company_candidate()
    {
        var existing = new JobApplication("Netflix", "") { LastEmailDate = DateTimeOffset.UtcNow.AddDays(-5) };
        var matcher = new ApplicationMatcher([existing]);

        var match = matcher.MatchMessage(Message("thread-5", "talent@netflix.com", DateTimeOffset.UtcNow),
            Result("Netflix", ""));

        Assert.NotNull(match);
        Assert.Equal(0.7, match!.Confidence);
        Assert.True(match.NeedsReview);
    }

    [Fact]
    public void Roleless_email_with_multiple_candidates_picks_nearest_in_time_and_flags_review()
    {
        // The matcher's time anchor is LastEmailDate ?? LastUpdated (not
        // AppliedDate) — see ApplicationMatcher.MatchMessage.
        var near = new JobApplication("Netflix", "") { LastEmailDate = DateTimeOffset.UtcNow.AddDays(-1) };
        var far = new JobApplication("Netflix", "") { LastEmailDate = DateTimeOffset.UtcNow.AddDays(-100) };
        var matcher = new ApplicationMatcher([near, far]);

        var match = matcher.MatchMessage(Message("thread-6", "talent@netflix.com", DateTimeOffset.UtcNow),
            Result("Netflix", ""));

        Assert.NotNull(match);
        Assert.Same(near, match!.Application);
        Assert.Equal(0.6, match.Confidence);
        Assert.True(match.NeedsReview);
    }

    [Fact]
    public void Known_sender_matches_when_model_found_no_company()
    {
        var existing = new JobApplication("Acme", "SWE") { ContactEmail = "recruiter@acme.com" };
        var matcher = new ApplicationMatcher([existing]);

        var match = matcher.MatchMessage(Message("thread-7", "Recruiter <recruiter@acme.com>", DateTimeOffset.UtcNow),
            Result("", "SWE"));

        Assert.NotNull(match);
        Assert.Equal(0.6, match!.Confidence);
    }

    [Fact]
    public void Associate_makes_followup_in_same_thread_match_at_full_confidence()
    {
        var existing = new JobApplication("Stripe", "SWE Intern");
        var matcher = new ApplicationMatcher([existing]);
        matcher.Associate("thread-8", existing);

        var match = matcher.MatchMessage(Message("thread-8", "hr@stripe.com", DateTimeOffset.UtcNow),
            Result("Stripe", "SWE Intern"));

        Assert.NotNull(match);
        Assert.Equal(1.0, match!.Confidence);
    }

    [Theory]
    [InlineData("careers@stripe.com", "Stripe")]
    [InlineData("noreply@linkedin.com", null)]
    [InlineData("jobs@boards.greenhouse.io", null)]
    [InlineData("hr@tiktok.com", "Tiktok")]
    public void CompanyFromSender_extracts_or_rejects_generic_domains(string sender, string? expected)
    {
        Assert.Equal(expected, ApplicationMatcher.CompanyFromSender(sender));
    }

    [Fact]
    public void RoleTokens_strips_noise_and_light_stems_gerunds()
    {
        var tokens = ApplicationMatcher.RoleTokens("Software Engineering Intern — Summer 2026");
        Assert.Contains("software", tokens);
        Assert.Contains("engineer", tokens); // "engineering" stemmed
        Assert.Contains("intern", tokens);   // deliberately kept
        Assert.DoesNotContain("summer", tokens);
        Assert.DoesNotContain("2026", tokens);
    }
}
