using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JobTracker.Services;
using JobTracker.ViewModels;
using Microsoft.Win32;

namespace JobTracker.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private bool _loading = true;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _vm.RequestEraseAndRestart += () => ((App)Application.Current).EraseAllDataAndRestart();

        ProviderCombo.ItemsSource = _vm.Providers.Select(p => p.DisplayName()).ToList();

        LoadGeneral();
        LoadAccount();
        _loading = false;
    }

    // MARK: - General

    private void LoadGeneral()
    {
        TrayWatcherCheck.IsChecked = _vm.AppState.Prefs.TrayWatcherEnabled;
        PollIntervalPanel.Visibility = _vm.AppState.Prefs.TrayWatcherEnabled ? Visibility.Visible : Visibility.Collapsed;
        PollIntervalBox.Text = ((int)_vm.AppState.Prefs.PollInterval.TotalSeconds).ToString();
    }

    private void TrayWatcher_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var enabled = TrayWatcherCheck.IsChecked == true;
        _vm.SetTrayWatcherEnabled(enabled);
        PollIntervalPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PollInterval_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse(PollIntervalBox.Text, out var seconds))
        {
            var clamped = Math.Clamp(seconds, 30, 600);
            _vm.AppState.Prefs.PollInterval = TimeSpan.FromSeconds(clamped);
            PollIntervalBox.Text = clamped.ToString();
        }
    }

    // MARK: - Account

    private void LoadAccount()
    {
        var auth = _vm.AppState.Auth;
        GmailStatusText.Text = auth.IsSignedIn ? "Connected" : "Not connected";
        GmailAccountText.Text = auth.AccountEmail ?? "";
        GmailAccountText.Visibility = string.IsNullOrEmpty(auth.AccountEmail) ? Visibility.Collapsed : Visibility.Visible;
        ClientIdBox.Text = _vm.AppState.Prefs.GoogleClientId;
        ReconnectButton.IsEnabled = AppConfig.IsGoogleConfigured;
        SignOutButton.IsEnabled = auth.IsSignedIn;

        ProviderCombo.SelectedIndex = (int)_vm.AppState.Prefs.Provider;
        ModelBox.Text = _vm.AppState.Prefs.Model;
        CustomEndpointPanel.Visibility = _vm.AppState.Prefs.Provider == LlmProvider.Custom ? Visibility.Visible : Visibility.Collapsed;
        EndpointBox.Text = _vm.AppState.Prefs.CustomBaseUrl;
        ApiKeyStatusText.Text = $"API Key: {(_vm.AppState.HasApiKey ? "Set" : "Missing")}";
        UpdateKeyValidationText();
    }

    private void ClientIdBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _vm.AppState.Prefs.GoogleClientId = ClientIdBox.Text;
        ReconnectButton.IsEnabled = AppConfig.IsGoogleConfigured;
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        try { await _vm.ReconnectAsync(); }
        catch { /* surfaced via ActivityLog */ }
        LoadAccount();
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _vm.SignOut();
        LoadAccount();
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _vm.ProviderChanged((LlmProvider)ProviderCombo.SelectedIndex);
        ModelBox.Text = _vm.AppState.Prefs.Model;
        CustomEndpointPanel.Visibility = _vm.AppState.Prefs.Provider == LlmProvider.Custom ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyStatusText.Text = $"API Key: {(_vm.AppState.HasApiKey ? "Set" : "Missing")}";
        ApiKeyBox.Password = "";
        UpdateKeyValidationText();
    }

    private void ModelBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.AppState.Prefs.Model = ModelBox.Text;
    }

    private void EndpointBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.AppState.Prefs.CustomBaseUrl = EndpointBox.Text;
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e) => _vm.ApiKeyInput = ApiKeyBox.Password;

    private async void ValidateKey_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ValidateAndSaveAsync();
        ApiKeyStatusText.Text = $"API Key: {(_vm.AppState.HasApiKey ? "Set" : "Missing")}";
        if (_vm.KeyState == KeyValidationState.Valid) ApiKeyBox.Password = "";
        UpdateKeyValidationText();
    }

    private void UpdateKeyValidationText()
    {
        (KeyValidationText.Text, KeyValidationText.Foreground) = _vm.KeyState switch
        {
            KeyValidationState.Valid => ("Key validated and saved.", (Brush)FindResource("SuccessBrush")),
            KeyValidationState.Invalid => ("Key saved, but validation failed. Check the key, model id, or endpoint.", (Brush)FindResource("WarningBrush")),
            _ => ("Each provider keeps its own key in the local encrypted store — switching back restores it.", (Brush)FindResource("SecondaryTextBrush")),
        };
    }

    // MARK: - Data

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Mail archives (*.mbox)|*.mbox|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true) _vm.AppState.Pipeline.ImportMbox(dialog.FileName);
    }

    private void ClearData_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "The tracker will be emptied. Gmail, API keys, and settings are kept — the next sync or archive import starts fresh.",
                "Clear all applications, events, and leads?", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        _vm.ClearData();
        ClearedMessageText.Text = _vm.ClearedMessage ?? "";
        ClearedMessageText.Visibility = string.IsNullOrEmpty(_vm.ClearedMessage) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void EraseAll_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "All applications, timelines, keys, and settings will be removed. JobTracker will restart into onboarding.",
                "Erase all JobTracker data?", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        _vm.EraseAll();
    }
}
