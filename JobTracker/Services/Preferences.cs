// Non-secret user preferences, backed by a JSON file under %LOCALAPPDATA%.
// Secrets go through the DPAPI-protected store (Secrets.cs).
//
// Windows has no CloudKit/iCloud equivalent, so unlike the macOS app there
// is no "storage choice" — data always lives in the local SQLite store
// (see StoreManager).

using System.IO;
using System.Text.Json;

namespace JobTracker.Services;

/// LLM providers the classifier can talk to. All expose OpenAI-compatible
/// chat-completions endpoints; each keeps its own API key in Secrets, so
/// switching providers never loses a key.
public enum LlmProvider { Nvidia, OpenAi, OpenRouter, Groq, Custom }

public static class LlmProviderExtensions
{
    public static string DisplayName(this LlmProvider provider) => provider switch
    {
        LlmProvider.Nvidia => "NVIDIA NIM",
        LlmProvider.OpenAi => "OpenAI",
        LlmProvider.OpenRouter => "OpenRouter",
        LlmProvider.Groq => "Groq",
        LlmProvider.Custom => "Custom (OpenAI-compatible)",
        _ => provider.ToString(),
    };

    /// Fixed endpoint; null means the user supplies one (custom).
    public static Uri? BaseUrl(this LlmProvider provider) => provider switch
    {
        LlmProvider.Nvidia => new Uri("https://integrate.api.nvidia.com/v1/chat/completions"),
        LlmProvider.OpenAi => new Uri("https://api.openai.com/v1/chat/completions"),
        LlmProvider.OpenRouter => new Uri("https://openrouter.ai/api/v1/chat/completions"),
        LlmProvider.Groq => new Uri("https://api.groq.com/openai/v1/chat/completions"),
        LlmProvider.Custom => null,
        _ => null,
    };

    public static string SuggestedModel(this LlmProvider provider) => provider switch
    {
        LlmProvider.Nvidia => AppConfig.DefaultModel,
        LlmProvider.OpenAi => "gpt-5.2-mini",
        LlmProvider.OpenRouter => "meta-llama/llama-4-maverick",
        LlmProvider.Groq => "llama-4-maverick-17b",
        LlmProvider.Custom => "",
        _ => "",
    };

    public static SecretKey SecretKey(this LlmProvider provider) => provider switch
    {
        LlmProvider.Nvidia => Services.SecretKey.NvidiaApiKey,
        LlmProvider.OpenAi => Services.SecretKey.OpenAiKey,
        LlmProvider.OpenRouter => Services.SecretKey.OpenRouterKey,
        LlmProvider.Groq => Services.SecretKey.GroqKey,
        LlmProvider.Custom => Services.SecretKey.CustomLlmKey,
        _ => Services.SecretKey.CustomLlmKey,
    };

    public static string KeyPlaceholder(this LlmProvider provider) => provider switch
    {
        LlmProvider.Nvidia => "nvapi-…",
        LlmProvider.OpenAi => "sk-…",
        LlmProvider.OpenRouter => "sk-or-…",
        LlmProvider.Groq => "gsk_…",
        LlmProvider.Custom => "API key",
        _ => "API key",
    };
}

/// The JSON-serialized shape of the preferences file.
internal sealed class PreferencesData
{
    public string GoogleClientId { get; set; } = "";
    public string Model { get; set; } = AppConfig.DefaultModel;
    public LlmProvider Provider { get; set; } = LlmProvider.Nvidia;
    public string CustomBaseUrl { get; set; } = "";
    public bool TrayWatcherEnabled { get; set; }
    public double PollIntervalSeconds { get; set; } = AppConfig.DefaultPollInterval.TotalSeconds;
    public bool OnboardingDone { get; set; }
    public int ArchitectureVersion { get; set; }
}

public sealed class Preferences
{
    public static readonly Preferences Shared = new();

    private readonly PreferencesData _data;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JobTracker", "preferences.json");

    private Preferences()
    {
        _data = Load();
    }

    public string GoogleClientId
    {
        get => _data.GoogleClientId;
        set { _data.GoogleClientId = value; Save(); }
    }

    public string Model
    {
        get => _data.Model;
        set { _data.Model = value; Save(); }
    }

    public LlmProvider Provider
    {
        get => _data.Provider;
        set { _data.Provider = value; Save(); }
    }

    public string CustomBaseUrl
    {
        get => _data.CustomBaseUrl;
        set { _data.CustomBaseUrl = value; Save(); }
    }

    /// Effective chat-completions endpoint for the active provider.
    public Uri? EffectiveBaseUrl =>
        Provider.BaseUrl() ?? (Uri.TryCreate(CustomBaseUrl, UriKind.Absolute, out var uri) ? uri : null);

    /// Optional near-real-time watcher. Off by default: the app is
    /// launch-sync based to minimize CPU (user-toggleable).
    public bool TrayWatcherEnabled
    {
        get => _data.TrayWatcherEnabled;
        set { _data.TrayWatcherEnabled = value; Save(); }
    }

    public TimeSpan PollInterval
    {
        get => TimeSpan.FromSeconds(_data.PollIntervalSeconds > 0 ? _data.PollIntervalSeconds : AppConfig.DefaultPollInterval.TotalSeconds);
        set { _data.PollIntervalSeconds = value.TotalSeconds; Save(); }
    }

    public bool OnboardingDone
    {
        get => _data.OnboardingDone;
        set { _data.OnboardingDone = value; Save(); }
    }

    // MARK: - One-time architecture reset

    public static bool NeedsArchitectureReset() => Shared._data.ArchitectureVersion < AppConfig.ArchitectureVersion;

    public static void MarkArchitectureReset()
    {
        Shared._data.ArchitectureVersion = AppConfig.ArchitectureVersion;
        Shared.Save();
    }

    /// Full reset: back to first-launch state.
    public void WipeAll()
    {
        _data.GoogleClientId = "";
        _data.Model = AppConfig.DefaultModel;
        _data.Provider = LlmProvider.Nvidia;
        _data.CustomBaseUrl = "";
        _data.TrayWatcherEnabled = false;
        _data.PollIntervalSeconds = AppConfig.DefaultPollInterval.TotalSeconds;
        _data.OnboardingDone = false;
        Save();
    }

    // MARK: - Persistence

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static PreferencesData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<PreferencesData>(json, JsonOptions);
                if (data is not null) return data;
            }
        }
        catch { /* fall through to defaults */ }
        return new PreferencesData();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, JsonOptions));
        }
        catch { /* best effort, matches macOS UserDefaults fire-and-forget semantics */ }
    }
}
