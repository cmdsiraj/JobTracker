// Thin async wrapper over the Gmail REST API v1. Handles the profile call,
// timestamp-watermark message listing (inbox + sent), and fetching +
// decoding individual messages.

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobTracker.Services;

public enum GmailApiErrorKind { Http, Decoding }

public sealed class GmailApiException(GmailApiErrorKind kind, string detail)
    : Exception(kind == GmailApiErrorKind.Http ? $"Gmail API error: {detail}" : $"Gmail decode error: {detail}");

public sealed class GmailProfile
{
    [JsonPropertyName("emailAddress")] public string EmailAddress { get; set; } = "";
    [JsonPropertyName("historyId")] public string HistoryId { get; set; } = "";
}

public sealed partial class GmailApiClient(GmailAuthService auth)
{
    private static readonly HttpClient Http = new();

    private async Task<HttpRequestMessage> AuthorizedRequestAsync(string path, Dictionary<string, string>? query = null)
    {
        var token = await auth.ValidAccessTokenAsync();
        var url = new Uri(AppConfig.GmailBaseUrl, path).ToString();
        if (query is { Count: > 0 })
        {
            url += "?" + string.Join('&', query.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        }
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<string> PerformAsync(HttpRequestMessage request)
    {
        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new GmailApiException(GmailApiErrorKind.Http, $"{(int)response.StatusCode}: {body}");
        }
        return body;
    }

    // MARK: - Profile

    public async Task<GmailProfile> GetProfileAsync()
    {
        var request = await AuthorizedRequestAsync("users/me/profile");
        var body = await PerformAsync(request);
        return JsonSerializer.Deserialize<GmailProfile>(body)
            ?? throw new GmailApiException(GmailApiErrorKind.Decoding, "profile");
    }

    // MARK: - Timestamp-based listing

    /// Returns ids of inbox + sent messages after `date` (the sync
    /// watermark). Sent mail is included so the user's own outreach emails
    /// are captured. Deliberately independent of read/unread state — the
    /// user may read mail on other devices.
    public Task<List<string>> MessageIdsAfterAsync(DateTimeOffset date, int maxResults = 500) =>
        ListMessageIdsAsync($"(in:inbox OR in:sent) after:{date.ToUnixTimeSeconds()}", maxResults);

    /// Returns recent inbox + sent message ids from the last `days` days
    /// (first sync when no archive was imported).
    public Task<List<string>> RecentMessageIdsAsync(int days, int maxResults = 200) =>
        ListMessageIdsAsync($"(in:inbox OR in:sent) newer_than:{days}d", maxResults);

    private async Task<List<string>> ListMessageIdsAsync(string query, int maxResults)
    {
        var ids = new List<string>();
        string? pageToken = null;

        do
        {
            var q = new Dictionary<string, string> { ["q"] = query, ["maxResults"] = "100" };
            if (pageToken is not null) q["pageToken"] = pageToken;
            var request = await AuthorizedRequestAsync("users/me/messages", q);
            var body = await PerformAsync(request);
            var page = JsonSerializer.Deserialize<MessageListResponse>(body) ?? new MessageListResponse();
            ids.AddRange((page.Messages ?? []).Select(m => m.Id));
            pageToken = page.NextPageToken;
        } while (pageToken is not null && ids.Count < maxResults);

        return ids.Take(maxResults).ToList();
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")] public List<MessageRef>? Messages { get; set; }
        [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    }

    private sealed class MessageRef
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
    }

    // MARK: - Fetch a single message

    public async Task<FetchedMessage> GetMessageAsync(string id)
    {
        var request = await AuthorizedRequestAsync($"users/me/messages/{id}", new Dictionary<string, string> { ["format"] = "full" });
        var body = await PerformAsync(request);
        var raw = JsonSerializer.Deserialize<RawMessage>(body)
            ?? throw new GmailApiException(GmailApiErrorKind.Decoding, "message");

        var headers = raw.Payload?.Headers ?? [];
        string Header(string name) =>
            headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

        var dateMillis = long.TryParse(raw.InternalDate, out var ms) ? ms : 0;
        return new FetchedMessage(
            Id: raw.Id,
            ThreadId: raw.ThreadId,
            Sender: Header("From"),
            Subject: Header("Subject"),
            Snippet: raw.Snippet ?? "",
            Body: ExtractBody(raw.Payload),
            Date: DateTimeOffset.FromUnixTimeMilliseconds(dateMillis),
            IsOutgoing: (raw.LabelIds ?? []).Contains("SENT"));
    }

    // MARK: - Body decoding

    private sealed class RawMessage
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("threadId")] public string ThreadId { get; set; } = "";
        [JsonPropertyName("snippet")] public string? Snippet { get; set; }
        [JsonPropertyName("internalDate")] public string? InternalDate { get; set; }
        [JsonPropertyName("labelIds")] public List<string>? LabelIds { get; set; }
        [JsonPropertyName("payload")] public MessagePart? Payload { get; set; }
    }

    private sealed class MessagePart
    {
        [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
        [JsonPropertyName("headers")] public List<MessageHeader>? Headers { get; set; }
        [JsonPropertyName("body")] public MessageBody? Body { get; set; }
        [JsonPropertyName("parts")] public List<MessagePart>? Parts { get; set; }
    }

    private sealed class MessageHeader
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("value")] public string Value { get; set; } = "";
    }

    private sealed class MessageBody
    {
        [JsonPropertyName("data")] public string? Data { get; set; }
    }

    /// Depth-first search for the first text/plain part; falls back to text/html
    /// (stripped) or any decodable data.
    private static string ExtractBody(MessagePart? part)
    {
        if (part is null) return "";

        if (part.MimeType == "text/plain" && Decode(part.Body?.Data) is { } text) return text;

        if (part.Parts is not null)
        {
            foreach (var child in part.Parts)
            {
                var childText = ExtractBody(child);
                if (childText.Length > 0) return childText;
            }
        }
        if (part.MimeType == "text/html" && Decode(part.Body?.Data) is { } html) return StripHtml(html);
        return Decode(part.Body?.Data) ?? "";
    }

    private static string? Decode(string? base64Url)
    {
        if (string.IsNullOrEmpty(base64Url)) return null;
        var value = base64Url.Replace('-', '+').Replace('_', '/');
        value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
        catch (FormatException) { return null; }
    }

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static string StripHtml(string html)
    {
        var result = TagRegex().Replace(html, " ");
        result = result.Replace("&nbsp;", " ");
        result = WhitespaceRegex().Replace(result, " ");
        return result.Trim();
    }
}
