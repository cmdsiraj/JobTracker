// Top-level coordinator holding the shared services, injected into every
// ViewModel that needs them.

using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Data;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public sealed partial class AppState : DispatcherObservableObject
{
    public GmailAuthService Auth { get; }
    public Preferences Prefs { get; }
    public SyncPipeline Pipeline { get; }
    public JobTrackerDbContext Context { get; }

    /// Tracks whether the ACTIVE provider has a key, so onboarding/settings
    /// can react. Refreshed on provider switches via RefreshKeyState().
    [ObservableProperty]
    private bool _hasApiKey;

    public AppState(JobTrackerDbContext context)
    {
        Context = context;
        Auth = new GmailAuthService();
        Prefs = Preferences.Shared;
        Pipeline = new SyncPipeline(context, Auth, Prefs);
        RefreshKeyState();

        Pipeline.SetTrayWatcher(Prefs.TrayWatcherEnabled);
    }

    public bool IsGoogleConfigured => AppConfig.IsGoogleConfigured;

    /// Onboarding shows until the wizard has been completed once.
    public bool NeedsOnboarding => !Prefs.OnboardingDone;

    /// Saves the key for the ACTIVE provider (Settings → Account picks it).
    /// Not used for Bedrock, which needs an access/secret key pair — see
    /// SaveBedrockCredentials.
    public void SaveApiKey(string key)
    {
        Secrets.Shared.Set(key.Trim(), Prefs.Provider.SecretKey());
        RefreshKeyState();
    }

    public void SaveBedrockCredentials(string accessKeyId, string secretAccessKey)
    {
        Secrets.Shared.Set(accessKeyId.Trim(), SecretKey.AwsAccessKeyId);
        Secrets.Shared.Set(secretAccessKey.Trim(), SecretKey.AwsSecretAccessKey);
        RefreshKeyState();
    }

    /// Re-reads credential presence after the provider changed (or a key
    /// was saved). Bedrock counts as configured if either an explicit key
    /// pair or a local AWS profile/env var is available.
    public void RefreshKeyState()
    {
        HasApiKey = Prefs.Provider == LlmProvider.Bedrock
            ? BedrockClient.HasAnyCredentials()
            : Secrets.Shared.Get(Prefs.Provider.SecretKey()) is { Length: > 0 };
    }

    /// Sends a minimal completion request through the active provider to
    /// verify the stored key (and, for custom providers, the endpoint).
    public async Task<bool> ValidateApiKeyAsync()
    {
        var client = new NimClient(Prefs.Model);
        try
        {
            await client.CompleteAsync([new NimClient.Message("user", "Reply with OK")], maxTokens: 10);
            return true;
        }
        catch (Exception ex)
        {
            ActivityLog.Shared.Warning($"{Prefs.Provider.DisplayName()} key validation failed: {ex.Message}");
            return false;
        }
    }
}
