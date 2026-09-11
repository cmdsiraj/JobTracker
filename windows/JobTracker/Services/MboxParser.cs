// Streaming parser for Google Takeout mbox archives. Reads the file in
// chunks (never loads the whole archive into memory), splits on mbox
// "From " separators, decodes MIME (RFC 2047 headers, quoted-printable /
// base64 bodies, multipart), and yields FetchedMessage values compatible
// with the rest of the pipeline.
//
// Takeout specifics used here:
// - Each message starts with "From <decimal-gm-thrid>@xxx <date>"
// - "X-GM-THRID" (decimal) converts to the hex threadId the Gmail API uses
// - "X-Gmail-Labels" lets us skip Spam / Trash outright

using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace JobTracker.Services;

public sealed class MboxParserException(string message) : Exception(message);

public sealed class MboxParseSummary
{
    public int TotalMessages { get; set; }
    public int SkippedSpamTrash { get; set; }
    public int Candidates { get; set; }
}

public static partial class MboxParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// Streams the mbox at `path`, returning only messages that pass the
    /// job-related prefilter (and are not Spam/Trash). `progress` receives
    /// a 0...1 fraction of file bytes consumed.
    public static (List<FetchedMessage> Messages, MboxParseSummary Summary) CollectJobCandidates(
        string path, Action<double>? progress = null)
    {
        if (!File.Exists(path)) throw new MboxParserException($"Cannot open mbox file: {path}");

        using var stream = File.OpenRead(path);
        var fileSize = stream.Length;
        const int chunkSize = 4 * 1024 * 1024;
        var separator = "\nFrom "u8.ToArray();

        var buffer = new List<byte>(chunkSize * 2);
        long bytesConsumed = 0;
        var summary = new MboxParseSummary();
        var results = new List<FetchedMessage>();

        void HandleMessage(byte[] raw)
        {
            summary.TotalMessages++;
            var parsed = ParseMessage(raw);
            if (parsed is null) return;
            if (parsed.Labels.Contains("Spam") || parsed.Labels.Contains("Trash"))
            {
                summary.SkippedSpamTrash++;
                return;
            }
            if (!HeuristicPrefilter.IsLikelyJobRelated(parsed.Fetched.Sender, parsed.Fetched.Subject,
                    parsed.Fetched.Body, parsed.Fetched.IsOutgoing))
            {
                return;
            }
            summary.Candidates++;
            results.Add(parsed.Fetched);
        }

        var readBuffer = new byte[chunkSize];
        int bytesRead;
        while ((bytesRead = stream.Read(readBuffer, 0, chunkSize)) > 0)
        {
            buffer.AddRange(bytesRead == chunkSize ? readBuffer : readBuffer[..bytesRead]);
            bytesConsumed += bytesRead;
            if (fileSize > 0) progress?.Invoke((double)bytesConsumed / fileSize);

            // Extract complete messages; keep the trailing partial one buffered.
            var searchStart = 0;
            while (true)
            {
                var bufSpan = CollectionsMarshal.AsSpan(buffer);
                if (searchStart >= bufSpan.Length) break;
                var relIdx = bufSpan[searchStart..].IndexOf(separator);
                if (relIdx < 0) break;
                var idx = searchStart + relIdx;

                // Validate this is a real mbox boundary: "From <digits>@xxx"
                if (!IsTakeoutBoundary(bufSpan, idx + separator.Length))
                {
                    searchStart = idx + separator.Length;
                    continue;
                }

                var messageBytes = buffer.GetRange(0, idx).ToArray();
                if (messageBytes.Length > 0) HandleMessage(messageBytes);
                // +1 keeps the "From " (drops only the "\n").
                buffer.RemoveRange(0, idx + 1);
                searchStart = 0;
            }
        }

        // Final message.
        if (buffer.Count > 0) HandleMessage(buffer.ToArray());

        return (results, summary);
    }

    /// Checks that the bytes after "\nFrom " look like "<digits>@xxx ".
    private static bool IsTakeoutBoundary(ReadOnlySpan<byte> data, int index)
    {
        var sawDigit = false;
        while (index < data.Length && data[index] is >= (byte)'0' and <= (byte)'9')
        {
            sawDigit = true;
            index++;
        }
        return sawDigit && index < data.Length && data[index] == (byte)'@';
    }

    // MARK: - Single message

    private sealed record ParsedMessage(FetchedMessage Fetched, HashSet<string> Labels);

    private static ParsedMessage? ParseMessage(byte[] raw)
    {
        var (headerBytes, bodyBytes) = SplitHeaderBody(raw);

        var headerText = DecodeUtf8OrLatin1(headerBytes);
        var headers = UnfoldHeaders(headerText);
        string Header(string name) => headers.GetValueOrDefault(name.ToLowerInvariant(), "");

        var labels = Header("X-Gmail-Labels").Split(',').Select(l => l.Trim())
            .Where(l => l.Length > 0).ToHashSet();

        var threadIdHex = "";
        if (ulong.TryParse(Header("X-GM-THRID"), NumberStyles.None, CultureInfo.InvariantCulture, out var decimalId))
        {
            threadIdHex = decimalId.ToString("x", CultureInfo.InvariantCulture);
        }

        var messageId = Header("Message-ID").Trim('<', '>', ' ', '\t');

        var subject = DecodeRfc2047(Header("Subject"));
        var sender = DecodeRfc2047(Header("From"));
        var date = ParseDate(Header("Date")) ?? DateTimeOffset.MinValue;

        var body = ExtractBody(bodyBytes, Header("Content-Type"), Header("Content-Transfer-Encoding"), 0);
        var cleanBody = Truncate(body, 8000);

        var fetched = new FetchedMessage(
            Id: messageId.Length == 0 ? $"mbox-{threadIdHex}-{raw.Length}" : messageId,
            ThreadId: threadIdHex,
            Sender: sender,
            Subject: subject,
            Snippet: Truncate(cleanBody.Replace('\n', ' '), 160),
            Body: cleanBody,
            Date: date,
            IsOutgoing: labels.Contains("Sent"));

        return new ParsedMessage(fetched, labels);
    }

    // MARK: - Headers

    private static (byte[] Header, byte[] Body) SplitHeaderBody(ReadOnlySpan<byte> raw)
    {
        var crlf = "\r\n\r\n"u8;
        var lf = "\n\n"u8;
        var idx = raw.IndexOf(crlf);
        var sepLen = 4;
        if (idx < 0) { idx = raw.IndexOf(lf); sepLen = 2; }
        if (idx < 0) return (raw.ToArray(), []);
        return (raw[..idx].ToArray(), raw[(idx + sepLen)..].ToArray());
    }

    /// Unfolds continuation lines and returns a [lowercased-name: value] map
    /// (first occurrence wins).
    private static Dictionary<string, string> UnfoldHeaders(string text)
    {
        var result = new Dictionary<string, string>();
        string? currentName = null;
        var currentValue = new StringBuilder();

        void Commit()
        {
            if (currentName is not null && !result.ContainsKey(currentName))
            {
                result[currentName] = currentValue.ToString().Trim();
            }
        }

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith(' ') || line.StartsWith('\t'))
            {
                currentValue.Append(' ').Append(line.Trim());
            }
            else
            {
                var colon = line.IndexOf(':');
                if (colon >= 0)
                {
                    Commit();
                    currentName = line[..colon].ToLowerInvariant();
                    currentValue.Clear();
                    currentValue.Append(line[(colon + 1)..]);
                }
            }
        }
        Commit();
        return result;
    }

    [GeneratedRegex(@"=\?([^?]+)\?([BbQq])\?([^?]*)\?=")]
    private static partial Regex Rfc2047Pattern();

    /// Decodes RFC 2047 encoded words: =?charset?B|Q?data?=
    public static string DecodeRfc2047(string value)
    {
        if (!value.Contains("=?")) return value;

        var result = value;
        var iterations = 0;
        while (iterations < 20)
        {
            var match = Rfc2047Pattern().Match(result);
            if (!match.Success) break;
            iterations++;

            var charset = match.Groups[1].Value.ToLowerInvariant();
            var encoding = match.Groups[2].Value.ToUpperInvariant();
            var payload = match.Groups[3].Value;

            byte[]? decodedData;
            if (encoding == "B")
            {
                try { decodedData = Convert.FromBase64String(payload); }
                catch (FormatException) { decodedData = null; }
            }
            else
            {
                decodedData = DecodeQuotedPrintable(payload.Replace('_', ' '));
            }

            var text = decodedData is null ? ""
                : charset.Contains("8859") ? Encoding.Latin1.GetString(decodedData)
                : DecodeUtf8OrLatin1(decodedData);

            result = string.Concat(result.AsSpan(0, match.Index), text, result.AsSpan(match.Index + match.Length));
        }
        return result;
    }

    // MARK: - Date

    private static readonly string[] DateFormats =
    [
        "ddd, d MMM yyyy HH:mm:ss zzz",
        "d MMM yyyy HH:mm:ss zzz",
        "ddd, d MMM yyyy HH:mm zzz",
    ];

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex TrailingOffsetRegex();

    public static DateTimeOffset? ParseDate(string value)
    {
        // Strip trailing comments like "(PDT)".
        var text = value;
        var paren = text.IndexOf('(');
        if (paren >= 0) text = text[..paren];
        text = text.Trim();

        // Normalize "-0700" → "-07:00" so the "zzz" custom specifier parses it.
        var offsetMatch = TrailingOffsetRegex().Match(text);
        if (offsetMatch.Success)
        {
            text = string.Concat(text.AsSpan(0, offsetMatch.Index),
                offsetMatch.Groups[1].Value, ":", offsetMatch.Groups[2].Value);
        }

        foreach (var format in DateFormats)
        {
            if (DateTimeOffset.TryParseExact(text, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var date))
            {
                return date;
            }
        }
        return null;
    }

    // MARK: - Body / MIME

    private static string ExtractBody(byte[] data, string contentType, string transferEncoding, int depth)
    {
        if (depth >= 5) return "";
        var typeLower = contentType.ToLowerInvariant();

        if (typeLower.Contains("multipart/"))
        {
            var boundary = Boundary(contentType);
            if (boundary is not null)
            {
                var parts = SplitMultipart(data, boundary);
                var htmlFallback = "";
                foreach (var part in parts)
                {
                    var (headers, partBody) = SplitPart(part);
                    var partType = headers.GetValueOrDefault("content-type", "text/plain");
                    var partEnc = headers.GetValueOrDefault("content-transfer-encoding", "");
                    var text = ExtractBody(partBody, partType, partEnc, depth + 1);
                    if (partType.ToLowerInvariant().Contains("text/plain") && text.Length > 0) return text;
                    if (htmlFallback.Length == 0 && text.Length > 0) htmlFallback = text;
                }
                return htmlFallback;
            }
        }

        // Leaf part: decode transfer encoding, then charset.
        byte[] decoded;
        switch (transferEncoding.Trim().ToLowerInvariant())
        {
            case "base64":
                var compact = new string(Encoding.ASCII.GetString(data)
                    .Where(c => !char.IsWhiteSpace(c)).ToArray());
                try { decoded = Convert.FromBase64String(compact); }
                catch (FormatException) { decoded = []; }
                break;
            case "quoted-printable":
                decoded = DecodeQuotedPrintable(Encoding.ASCII.GetString(data)) ?? [];
                break;
            default:
                decoded = data;
                break;
        }

        var text2 = DecodeUtf8OrLatin1(decoded);
        if (typeLower.Contains("text/html")) text2 = StripHtml(text2);
        return text2.Trim();
    }

    [GeneratedRegex("boundary=\"?([^\";]+)\"?", RegexOptions.IgnoreCase)]
    private static partial Regex BoundaryRegex();

    private static string? Boundary(string contentType)
    {
        var match = BoundaryRegex().Match(contentType);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<byte[]> SplitMultipart(byte[] data, string boundary)
    {
        var marker = Encoding.UTF8.GetBytes($"--{boundary}");
        var parts = new List<byte[]>();
        var cursor = 0;
        int? partStart = null;

        while (true)
        {
            var relIdx = ((ReadOnlySpan<byte>)data)[cursor..].IndexOf(marker);
            if (relIdx < 0) break;
            var idx = cursor + relIdx;
            if (partStart is int start)
            {
                parts.Add(data[start..idx]);
            }
            partStart = idx + marker.Length;
            cursor = idx + marker.Length;
            if (cursor >= data.Length) break;
        }
        return parts;
    }

    private static (Dictionary<string, string> Headers, byte[] Body) SplitPart(byte[] data)
    {
        var (headerBytes, body) = SplitHeaderBody(data);
        if (body.Length == 0 && headerBytes.Length == data.Length)
        {
            // No blank-line separator found: SplitHeaderBody returned the
            // whole thing as "header" with an empty body — treat as body-only.
            return ([], data);
        }
        return (UnfoldHeaders(DecodeUtf8OrLatin1(headerBytes)), body);
    }

    public static byte[]? DecodeQuotedPrintable(string text)
    {
        var output = new List<byte>(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var c = text[index];
            if (c == '=')
            {
                var next = index + 1;
                // Soft line break: "=\n" or "=\r\n"
                if (next < text.Length && text[next] == '\n') { index = next + 1; continue; }
                if (next < text.Length && text[next] == '\r')
                {
                    var afterCr = next + 1;
                    if (afterCr < text.Length && text[afterCr] == '\n') { index = afterCr + 1; continue; }
                }
                // Hex byte: "=XX"
                if (index + 3 <= text.Length &&
                    byte.TryParse(text.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out var b))
                {
                    output.Add(b);
                    index += 3;
                    continue;
                }
                output.Add((byte)'=');
                index++;
            }
            else
            {
                output.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
                index++;
            }
        }
        return [.. output];
    }

    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleTagRegex();
    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTagRegex();
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private static string StripHtml(string html)
    {
        var result = StyleTagRegex().Replace(html, " ");
        result = ScriptTagRegex().Replace(result, " ");
        result = TagRegex().Replace(result, " ");
        result = result.Replace("&nbsp;", " ").Replace("&amp;", "&");
        result = WhitespaceRegex().Replace(result, " ");
        return result.Trim();
    }

    // MARK: - Shared helpers

    private static string DecodeUtf8OrLatin1(byte[] bytes)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
