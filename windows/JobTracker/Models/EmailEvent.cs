// One entry in an application's communication log. Three kinds:
//   - Email:        a Gmail message (incoming or outgoing/outreach)
//   - Note:         a manual note the user added ("process paused", …)
//   - StatusChange: auto-logged whenever the status moves

namespace JobTracker.Models;

public enum EventKind { Email, Note, StatusChange }

public enum EventDirection { Incoming, Outgoing }

public class EmailEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string KindRaw { get; set; } = EventKind.Email.ToString();
    public string DirectionRaw { get; set; } = EventDirection.Incoming.ToString();

    public string GmailMessageId { get; set; } = "";
    public string ThreadId { get; set; } = "";
    public DateTimeOffset ReceivedDate { get; set; } = DateTimeOffset.UtcNow;
    public string Sender { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Snippet { get; set; } = "";

    /// Status the classifier detected for this specific message.
    public string? DetectedStatusRaw { get; set; }
    public double Confidence { get; set; }

    /// How confident the matcher was when attaching this email to its
    /// application (1.0 = same thread, lower = fuzzy match).
    public double MatchConfidence { get; set; } = 1.0;

    /// Raw JSON returned by the LLM, kept for debugging/auditing.
    public string? RawJson { get; set; }

    public Guid? ApplicationId { get; set; }
    public JobApplication? Application { get; set; }

    public EmailEvent() { }

    public EmailEvent(string gmailMessageId, string threadId, DateTimeOffset receivedDate,
        string sender, string subject, string snippet, EventDirection direction = EventDirection.Incoming)
    {
        KindRaw = EventKind.Email.ToString();
        DirectionRaw = direction.ToString();
        GmailMessageId = gmailMessageId;
        ThreadId = threadId;
        ReceivedDate = receivedDate;
        Sender = sender;
        Subject = subject;
        Snippet = snippet;
    }

    /// Manual note or auto-logged status change.
    public static EmailEvent NoteOrStatusChange(EventKind kind, string text, DateTimeOffset? date = null)
    {
        return new EmailEvent
        {
            KindRaw = kind.ToString(),
            DirectionRaw = EventDirection.Incoming.ToString(),
            ReceivedDate = date ?? DateTimeOffset.UtcNow,
            Snippet = text,
            Subject = kind == EventKind.StatusChange ? "Status changed" : "Note",
        };
    }

    public EventKind Kind => Enum.TryParse<EventKind>(KindRaw, out var v) ? v : EventKind.Email;
    public EventDirection Direction => Enum.TryParse<EventDirection>(DirectionRaw, out var v) ? v : EventDirection.Incoming;

    public ApplicationStatus? DetectedStatus =>
        DetectedStatusRaw is not null && Enum.TryParse<ApplicationStatus>(DetectedStatusRaw, out var v) ? v : null;

    /// Deep link that opens this message in the Gmail web client. Messages
    /// synced via the API use the hex message id; messages imported from a
    /// Takeout archive carry an RFC 822 Message-ID and use Gmail search.
    public Uri? GmailUrl
    {
        get
        {
            if (Kind != EventKind.Email || string.IsNullOrEmpty(GmailMessageId)) return null;
            if (GmailMessageId.Contains('@'))
            {
                var encoded = Uri.EscapeDataString(GmailMessageId);
                return new Uri($"https://mail.google.com/mail/u/0/#search/rfc822msgid:{encoded}");
            }
            return new Uri($"https://mail.google.com/mail/u/0/#all/{GmailMessageId}");
        }
    }
}
