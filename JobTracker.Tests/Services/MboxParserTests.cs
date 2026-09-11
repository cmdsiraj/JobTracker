using System.Text;
using JobTracker.Services;

namespace JobTracker.Tests.Services;

public class MboxParserTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"jobtracker-test-{Guid.NewGuid()}.mbox");

    public void Dispose()
    {
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
        GC.SuppressFinalize(this);
    }

    private static string RawMessage(string gmThrId, string labels, string from, string subject,
        string date, string contentType, string transferEncoding, string body, string messageId = "")
    {
        var msgIdHeader = messageId.Length > 0 ? $"Message-ID: <{messageId}>\r\n" : "";
        return
            $"From {gmThrId}@xxx {date}\r\n" +
            $"X-GM-THRID: {gmThrId}\r\n" +
            $"X-Gmail-Labels: {labels}\r\n" +
            msgIdHeader +
            $"From: {from}\r\n" +
            $"Subject: {subject}\r\n" +
            $"Date: {date}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Transfer-Encoding: {transferEncoding}\r\n" +
            "\r\n" +
            body + "\r\n";
    }

    [Fact]
    public void Parses_plain_text_job_related_message_as_candidate()
    {
        var msg = RawMessage("305419896", "Inbox", "careers@stripe.com", "Thank you for applying to Stripe",
            "Tue, 3 Jun 2025 10:15:00 -0700", "text/plain", "7bit",
            "Thank you for applying. We received your application and our recruiting team will review your candidacy.",
            messageId: "abc123@mail.gmail.com");
        File.WriteAllText(_tempPath, msg, Encoding.UTF8);

        var (messages, summary) = MboxParser.CollectJobCandidates(_tempPath);

        Assert.Equal(1, summary.TotalMessages);
        Assert.Equal(1, summary.Candidates);
        Assert.Equal(0, summary.SkippedSpamTrash);
        var only = Assert.Single(messages);
        Assert.Equal("careers@stripe.com", only.Sender);
        Assert.Equal("Thank you for applying to Stripe", only.Subject);
        Assert.Equal("abc123@mail.gmail.com", only.Id);
        Assert.Equal("12345678", only.ThreadId); // 305419896 decimal == 0x12345678
        Assert.False(only.IsOutgoing);
        Assert.Contains("recruiting team", only.Body);
    }

    [Fact]
    public void Skips_spam_and_trash_labeled_messages()
    {
        var msg = RawMessage("1", "Spam", "jobs@example.com", "Thank you for applying",
            "Tue, 3 Jun 2025 10:15:00 -0700", "text/plain", "7bit",
            "Thank you for applying. We received your application. Your candidacy is under review.");
        File.WriteAllText(_tempPath, msg, Encoding.UTF8);

        var (messages, summary) = MboxParser.CollectJobCandidates(_tempPath);

        Assert.Equal(1, summary.TotalMessages);
        Assert.Equal(1, summary.SkippedSpamTrash);
        Assert.Empty(messages);
    }

    [Fact]
    public void Filters_out_non_job_related_message()
    {
        var msg = RawMessage("1", "Inbox", "friend@example.com", "Lunch tomorrow?",
            "Tue, 3 Jun 2025 10:15:00 -0700", "text/plain", "7bit", "Want to grab lunch tomorrow?");
        File.WriteAllText(_tempPath, msg, Encoding.UTF8);

        var (messages, summary) = MboxParser.CollectJobCandidates(_tempPath);

        Assert.Equal(1, summary.TotalMessages);
        Assert.Equal(0, summary.Candidates);
        Assert.Empty(messages);
    }

    [Fact]
    public void Parses_multiple_messages_split_on_boundary()
    {
        var msg1 = RawMessage("1", "Inbox", "careers@stripe.com", "Thank you for applying to Stripe",
            "Tue, 3 Jun 2025 10:15:00 -0700", "text/plain", "7bit",
            "Thank you for applying. We received your application and your candidacy will be reviewed.");
        var msg2 = RawMessage("2", "Sent,Inbox", "me@example.com", "Interested in the SWE Internship role",
            "Wed, 4 Jun 2025 09:00:00 -0700", "text/plain", "7bit",
            "I'm interested in the opportunity and wanted to send my resume.");
        File.WriteAllText(_tempPath, msg1 + msg2, Encoding.UTF8);

        var (messages, summary) = MboxParser.CollectJobCandidates(_tempPath);

        Assert.Equal(2, summary.TotalMessages);
        Assert.Equal(2, messages.Count);
        Assert.True(messages[1].IsOutgoing);
    }

    [Fact]
    public void Decodes_quoted_printable_body()
    {
        // "café" quoted-printable-encoded, plus enough job-signal phrases to
        // clear the prefilter bar.
        var body = "Thank you for applying to our caf=C3=A9 team. We received your application. Your candidacy is strong.";
        var msg = RawMessage("1", "Inbox", "careers@example.com", "Thank you for applying",
            "Tue, 3 Jun 2025 10:15:00 -0700", "text/plain", "quoted-printable", body);
        File.WriteAllText(_tempPath, msg, Encoding.UTF8);

        var (messages, _) = MboxParser.CollectJobCandidates(_tempPath);

        var only = Assert.Single(messages);
        Assert.Contains("café", only.Body);
    }

    [Fact]
    public void Decodes_rfc2047_encoded_subject()
    {
        var msg = RawMessage("1", "Inbox", "careers@example.com", "=?UTF-8?B?VGjDoW5rIHlvdQ==?=",
            "Tue, 3 Jun 2025 10:15:00 -0700", "text/plain", "7bit",
            "Thank you for applying. We received your application. Your candidacy is under review.");
        File.WriteAllText(_tempPath, msg, Encoding.UTF8);

        var (messages, _) = MboxParser.CollectJobCandidates(_tempPath);

        var only = Assert.Single(messages);
        Assert.Equal("Thánk you", only.Subject);
    }

    [Fact]
    public void Prefers_text_plain_part_in_multipart_message()
    {
        const string boundary = "BOUNDARY123";
        var body =
            $"--{boundary}\r\n" +
            "Content-Type: text/html\r\n\r\n" +
            "<p>Thank you for <b>applying</b></p>\r\n" +
            $"--{boundary}\r\n" +
            "Content-Type: text/plain\r\n\r\n" +
            "Thank you for applying. We received your application. Your candidacy is under review.\r\n" +
            $"--{boundary}--\r\n";
        var msg =
            "From 1@xxx Tue, 3 Jun 2025 10:15:00 -0700\r\n" +
            "X-GM-THRID: 1\r\n" +
            "X-Gmail-Labels: Inbox\r\n" +
            "From: careers@example.com\r\n" +
            "Subject: Thank you\r\n" +
            "Date: Tue, 3 Jun 2025 10:15:00 -0700\r\n" +
            $"Content-Type: multipart/alternative; boundary=\"{boundary}\"\r\n" +
            "\r\n" + body;
        File.WriteAllText(_tempPath, msg, Encoding.UTF8);

        var (messages, _) = MboxParser.CollectJobCandidates(_tempPath);

        var only = Assert.Single(messages);
        Assert.DoesNotContain("<b>", only.Body);
        Assert.Contains("We received your application", only.Body);
    }

    [Theory]
    [InlineData("=?UTF-8?Q?Caf=C3=A9?=", "Café")]
    [InlineData("Plain subject", "Plain subject")]
    public void DecodeRfc2047_handles_quoted_printable_and_plain(string input, string expected)
    {
        Assert.Equal(expected, MboxParser.DecodeRfc2047(input));
    }

    [Fact]
    public void ParseDate_parses_rfc822_style_offset_without_colon()
    {
        var date = MboxParser.ParseDate("Tue, 3 Jun 2025 10:15:00 -0700 (PDT)");
        Assert.NotNull(date);
        Assert.Equal(2025, date!.Value.Year);
        Assert.Equal(6, date.Value.Month);
        Assert.Equal(3, date.Value.Day);
        Assert.Equal(TimeSpan.FromHours(-7), date.Value.Offset);
    }
}
