// Multi-step first-run wizard: welcome, Gmail connection, LLM API key, and
// the initial-import guide. Finishing marks OnboardingDone, which flips
// MainWindow over to the main shell.
//
// Unlike macOS there's no storage-choice step: Windows has no CloudKit
// equivalent, so storage is always local.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Services;

namespace JobTracker.ViewModels;

public enum OnboardingStep { Welcome, Gmail, ApiKey, ImportGuide }

public enum KeyValidationState { Idle, Validating, Valid, Invalid }

public sealed partial class OnboardingViewModel : DispatcherObservableObject
{
    public AppState AppState { get; }

    [ObservableProperty]
    private OnboardingStep _step = OnboardingStep.Welcome;

    [ObservableProperty]
    private bool _signingIn;

    [ObservableProperty]
    private string? _signInError;

    [ObservableProperty]
    private string _apiKeyInput = "";

    [ObservableProperty]
    private KeyValidationState _keyState = KeyValidationState.Idle;

    /// Raised when the user picks "Import Archive…" — the View opens the
    /// file dialog and, on a path, calls FinishAndImport(path).
    public event Action? RequestArchiveImport;

    /// Raised once onboarding completes (either path) so MainWindow can
    /// switch to the main shell.
    public event Action? Finished;

    public OnboardingViewModel(AppState appState)
    {
        AppState = appState;
    }

    public IReadOnlyList<OnboardingStep> AllSteps { get; } =
    [
        OnboardingStep.Welcome, OnboardingStep.Gmail, OnboardingStep.ApiKey, OnboardingStep.ImportGuide,
    ];

    public string ContinueTitle => Step switch
    {
        OnboardingStep.Gmail when !AppState.Auth.IsSignedIn => "Skip for Now",
        OnboardingStep.ApiKey when KeyState != KeyValidationState.Valid && !AppState.HasApiKey => "Skip for Now",
        _ => "Continue",
    };

    [RelayCommand]
    private void Next()
    {
        var next = (int)Step + 1;
        Step = next <= (int)OnboardingStep.ImportGuide ? (OnboardingStep)next : OnboardingStep.ImportGuide;
    }

    [RelayCommand]
    private void Back()
    {
        var prev = (int)Step - 1;
        Step = prev >= 0 ? (OnboardingStep)prev : OnboardingStep.Welcome;
    }

    [RelayCommand]
    private async Task ConnectGmail()
    {
        SigningIn = true;
        SignInError = null;
        try { await AppState.Auth.SignInAsync(); }
        catch (Exception ex) { SignInError = ex.Message; }
        finally { SigningIn = false; }
    }

    [RelayCommand]
    private async Task ValidateKey()
    {
        var trimmed = ApiKeyInput.Trim();
        if (trimmed.Length == 0) return;
        KeyState = KeyValidationState.Validating;
        AppState.SaveApiKey(trimmed);
        var ok = await AppState.ValidateApiKeyAsync();
        KeyState = ok ? KeyValidationState.Valid : KeyValidationState.Invalid;
        if (ok) ApiKeyInput = "";
    }

    [RelayCommand]
    private void ImportArchive() => RequestArchiveImport?.Invoke();

    /// Called by the View after the file dialog returns a path.
    public void FinishAndImport(string path)
    {
        Finish();
        AppState.Pipeline.ImportMbox(path);
    }

    [RelayCommand]
    private void Finish()
    {
        AppState.Prefs.OnboardingDone = true;
        Finished?.Invoke();
    }
}
