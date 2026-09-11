// Client for the active LLM provider's OpenAI-compatible chat completions
// endpoint. The API key is read from Secrets at call time.

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JobTracker.Services;

public enum NimErrorKind { MissingApiKey, Http, RateLimited, EmptyResponse }

public sealed class NimException(NimErrorKind kind, string? detail = null) : Exception(Describe(kind, detail))
{
    public NimErrorKind Kind { get; } = kind;

    private static string Describe(NimErrorKind kind, string? detail) => kind switch
    {
        NimErrorKind.MissingApiKey => "No API key set. Add it in Settings.",
        NimErrorKind.Http => $"LLM API error: {detail}",
        NimErrorKind.RateLimited => "LLM API rate limit hit; will retry.",
        NimErrorKind.EmptyResponse => "LLM API returned no content.",
        _ => "LLM API error",
    };
}

/// Paces all LLM requests to stay under the provider's rate limit (NVIDIA
/// free tier: 40 requests/minute → one request per 1.5s; a small margin
/// keeps bursts safely below the cap). Every request in the app funnels
/// through NimClient, so this is the single enforcement point.
///
/// Relies on callers awaiting one request at a time (true throughout this
/// app — SyncPipeline classifies batches sequentially), matching the
/// macOS version's @MainActor-serialized equivalent.
public sealed class RequestThrottle
{
    public static readonly RequestThrottle Shared = new();
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1.6);
    private DateTimeOffset _lastRequestStart = DateTimeOffset.MinValue;

    private RequestThrottle() { }

    /// Waits until it's safe to start the next request.
    public async Task WaitTurnAsync()
    {
        var elapsed = DateTimeOffset.UtcNow - _lastRequestStart;
        if (elapsed < MinInterval)
        {
            await Task.Delay(MinInterval - elapsed);
        }
        _lastRequestStart = DateTimeOffset.UtcNow;
    }
}

public sealed class NimClient(string model)
{
    private static readonly HttpClient Http = new();

    public sealed record Message(string Role, string Content);

    /// Sends a chat completion to the active provider (Settings → Account)
    /// and returns the assistant's message content.
    public async Task<string> CompleteAsync(List<Message> messages, int maxTokens = 800, double temperature = 0.1)
    {
        var provider = Preferences.Shared.Provider;

        // Bedrock isn't an OpenAI-compatible HTTP endpoint (AWS SigV4 auth,
        // Converse API request/response shape) — hand off entirely.
        if (provider == LlmProvider.Bedrock)
        {
            return await BedrockClient.CompleteAsync(model, messages, maxTokens, temperature);
        }

        var apiKey = Secrets.Shared.Get(provider.SecretKey());
        if (string.IsNullOrEmpty(apiKey)) throw new NimException(NimErrorKind.MissingApiKey);
        var endpoint = Preferences.Shared.EffectiveBaseUrl
            ?? throw new NimException(NimErrorKind.Http, "No endpoint URL configured for the custom provider");

        await RequestThrottle.Shared.WaitTurnAsync();

        var payload = new RequestBody
        {
            Model = model,
            Messages = messages.Select(m => new WireMessage { Role = m.Role, Content = m.Content }).ToList(),
            Temperature = temperature,
            MaxTokens = maxTokens,
            // NVIDIA-only knob (disables reasoning traces for clean JSON);
            // other providers can reject unknown fields, so omit it there.
            ChatTemplateKwargs = provider == LlmProvider.Nvidia ? new ChatTemplateKwargs { EnableThinking = false } : null,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new NimException(NimErrorKind.RateLimited);
        if (!response.IsSuccessStatusCode) throw new NimException(NimErrorKind.Http, $"{(int)response.StatusCode}: {body}");

        var decoded = JsonSerializer.Deserialize<ResponseBody>(body);
        var content = decoded?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrEmpty(content)) throw new NimException(NimErrorKind.EmptyResponse);
        return content;
    }

    // MARK: - Wire types

    private sealed class WireMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatTemplateKwargs
    {
        [JsonPropertyName("enable_thinking")] public bool EnableThinking { get; set; }
    }

    private sealed class RequestBody
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<WireMessage> Messages { get; set; } = [];
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("chat_template_kwargs")] public ChatTemplateKwargs? ChatTemplateKwargs { get; set; }
    }

    private sealed class ResponseBody
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChoiceMessage? Message { get; set; }
    }

    private sealed class ChoiceMessage
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
