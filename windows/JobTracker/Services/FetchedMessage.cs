// A parsed email message reduced to what the classifier needs. Shared
// between GmailApiClient (live sync) and MboxParser (Takeout import).

namespace JobTracker.Services;

public sealed record FetchedMessage(
    string Id,
    string ThreadId,
    string Sender,
    string Subject,
    string Snippet,
    string Body,
    DateTimeOffset Date,
    bool IsOutgoing = false);
