// Preferences window: General (model, tray watcher, poll interval),
// Account (Gmail, LLM provider/key), and Data (import, full erase).

using CommunityToolkit.Mvvm.ComponentModel;
using JobTracker.Data;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public sealed partial class SettingsViewModel : DispatcherObservableObject
{
    public AppState AppState { get; }
    private readonly JobTrackerDbContext _context;

    [ObservableProperty]
    private string _apiKeyInput = "";

    [ObservableProperty]
    private string _awsAccessKeyInput = "";

    [ObservableProperty]
    private string _awsSecretKeyInput = "";

    [ObservableProperty]
    private KeyValidationState _keyState = KeyValidationState.Idle;

    [ObservableProperty]
    private string? _clearedMessage;

    public SettingsViewModel(AppState appState)
    {
        AppState = appState;
        _context = appState.Context;
    }

    public IReadOnlyList<LlmProvider> Providers { get; } = Enum.GetValues<LlmProvider>();

    public void SetTrayWatcherEnabled(bool enabled)
    {
        AppState.Prefs.TrayWatcherEnabled = enabled;
        AppState.Pipeline.SetTrayWatcher(enabled);
    }

    public void ProviderChanged(LlmProvider provider)
    {
        AppState.Prefs.Provider = provider;
        if (provider.SuggestedModel().Length > 0) AppState.Prefs.Model = provider.SuggestedModel();
        KeyState = KeyValidationState.Idle;
        ApiKeyInput = "";
        AwsAccessKeyInput = "";
        AwsSecretKeyInput = "";
        AppState.RefreshKeyState();
    }

    public async Task ValidateAndSaveAsync()
    {
        KeyState = KeyValidationState.Validating;
        AppState.SaveApiKey(ApiKeyInput);
        var ok = await AppState.ValidateApiKeyAsync();
        KeyState = ok ? KeyValidationState.Valid : KeyValidationState.Invalid;
        if (ok) ApiKeyInput = "";
    }

    /// Bedrock's credential pair, saved together (an access key alone or a
    /// secret alone is useless). Leaving both blank clears any explicit
    /// keys and falls back to the local AWS profile/env-var chain.
    public async Task ValidateAndSaveBedrockAsync()
    {
        KeyState = KeyValidationState.Validating;
        AppState.SaveBedrockCredentials(AwsAccessKeyInput, AwsSecretKeyInput);
        var ok = await AppState.ValidateApiKeyAsync();
        KeyState = ok ? KeyValidationState.Valid : KeyValidationState.Invalid;
        if (ok)
        {
            AwsAccessKeyInput = "";
            AwsSecretKeyInput = "";
        }
    }

    public async Task ReconnectAsync() => await AppState.Auth.SignInAsync();

    public void SignOut() => AppState.Auth.SignOut();

    /// In-app data clear: batch-deletes every record but leaves auth,
    /// secrets, and preferences untouched. No restart needed.
    public void ClearData()
    {
        try
        {
            _context.Events.RemoveRange(_context.Events);
            _context.Applications.RemoveRange(_context.Applications);
            _context.Leads.RemoveRange(_context.Leads);
            _context.SyncStates.RemoveRange(_context.SyncStates);
            _context.SaveChanges();
            ActivityLog.Shared.Success("All data cleared — tracker is empty, ready for fresh ingestion");
            ClearedMessage = "Data cleared.";
        }
        catch (Exception ex)
        {
            ActivityLog.Shared.Error($"Clear data failed: {ex.Message}");
            ClearedMessage = null;
        }
    }

    /// Erasing the store requires disposing this DbContext's live SQLite
    /// connection first — unlike Unix, Windows won't delete a file that's
    /// still open — then relaunching, since every ViewModel in the app
    /// holds references into the now-replaced store. Handled at the App
    /// level; this event just requests it.
    public event Action? RequestEraseAndRestart;

    public void EraseAll() => RequestEraseAndRestart?.Invoke();
}
