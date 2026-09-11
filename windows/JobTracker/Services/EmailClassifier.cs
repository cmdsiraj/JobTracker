// Turns Gmail messages into structured classifications using the active LLM
// provider. Emails are classified in batches (AppConfig.ClassificationBatchSize
// per request) which divides request count — and free-tier credit burn —
// by the batch size. The heuristic prefilter runs first so obvious noise
// never reaches the network.

using System.Text.Json;

namespace JobTracker.Services;

public sealed class EmailClassifier(NimClient nim)
{
    private const string BatchSystemPrompt = """
        You are an assistant that reads emails and decides, for each one, whether it relates to the recipient's own job application(s), and if so extracts structured data.

        You will receive several emails, each preceded by "=== EMAIL <index> ===".

        Respond with ONLY a JSON array (no prose, no markdown fences) containing one object per email:
        {
          "index": number,            // the email's index, exactly as given
          "isJobRelated": bool,       // true only if about the recipient's own application/hiring process
          "company": string,          // hiring company name, "" if unknown
          "role": string,             // job title, "" if unknown
          "status": string,           // one of: outreach, applied, assessment, recruiter call, interview, final round, offer, rejected, unknown
          "location": string|null,
          "source": string|null,      // e.g. LinkedIn, Greenhouse, company site
          "nextAction": string|null,  // any action the recipient must take
          "cycle": string|null,       // recruiting cycle, e.g. "Summer 2026", "Fall 2025", "New Grad 2026"
          "confidence": number        // 0.0 - 1.0
        }

        Status guidance:
        - outreach: an email the recipient themselves sent to a company/recruiter (cold email, referral request, follow-up)
        - applied: confirmation an application was received/submitted
        - assessment: online assessment, take-home, coding challenge invitation
        - recruiter call: recruiter reaching out, scheduling an intro/phone screen
        - interview: technical/behavioral interview rounds
        - final round: onsite or final interview loop
        - offer: an offer is extended
        - rejected: declined / not moving forward
        Newsletters, job alerts, marketing, and generic "jobs you may like" emails are NOT job-related.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// Classifies up to `AppConfig.ClassificationBatchSize` messages in one
    /// LLM request. Returns results keyed by position in `messages`.
    /// Messages rejected by the prefilter never reach the network.
    public async Task<Dictionary<int, ClassificationResult>> ClassifyAsync(List<FetchedMessage> messages)
    {
        var results = new Dictionary<int, ClassificationResult>();
        var toSend = new List<(int Position, FetchedMessage Message)>();

        for (var position = 0; position < messages.Count; position++)
        {
            var message = messages[position];
            if (HeuristicPrefilter.IsLikelyJobRelated(message.Sender, message.Subject, message.Body, message.IsOutgoing))
            {
                toSend.Add((position, message));
            }
            else
            {
                results[position] = ClassificationResult.NotJobRelated(position);
            }
        }
        if (toSend.Count == 0) return results;

        var userPrompt = string.Join("\n\n", toSend.Select(t =>
            $"=== EMAIL {t.Position} ===\n" +
            $"Direction: {(t.Message.IsOutgoing ? "SENT BY RECIPIENT (outreach)" : "RECEIVED")}\n" +
            $"From: {t.Message.Sender}\n" +
            $"Subject: {t.Message.Subject}\n" +
            $"Date: {t.Message.Date.UtcDateTime:yyyy-MM-ddTHH:mm:ss}Z\n" +
            "Body:\n" +
            Truncate(t.Message.Body, 2500)));

        var content = await nim.CompleteAsync(
        [
            new NimClient.Message("system", BatchSystemPrompt),
            new NimClient.Message("user", userPrompt),
        ], maxTokens: 350 * toSend.Count);

        foreach (var parsed in ParseArray(content))
        {
            if (parsed.Index is not int index || toSend.All(t => t.Position != index)) continue;
            results[index] = parsed;
        }

        // Anything the model failed to answer counts as unclassified (skipped).
        foreach (var (position, _) in toSend)
        {
            results.TryAdd(position, ClassificationResult.NotJobRelated(position));
        }
        return results;
    }

    // MARK: - Parsing

    /// Extracts the JSON array from model output (tolerant of stray text or
    /// fences) and decodes it.
    public static List<ClassificationResult> ParseArray(string content)
    {
        var text = content.Trim();
        var arrayStart = text.IndexOf('[');
        var arrayEnd = text.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            text = text[arrayStart..(arrayEnd + 1)];
        }
        else
        {
            var objStart = text.IndexOf('{');
            var objEnd = text.LastIndexOf('}');
            if (objStart >= 0 && objEnd > objStart)
            {
                // Model answered with a single object; accept it.
                text = $"[{text[objStart..(objEnd + 1)]}]";
            }
        }
        return JsonSerializer.Deserialize<List<ClassificationResult>>(text, JsonOptions)
            ?? throw new NimException(NimErrorKind.EmptyResponse);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
