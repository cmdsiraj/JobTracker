using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Shapes;
using JobTracker.Services;
using JobTracker.ViewModels;
using Microsoft.Win32;

namespace JobTracker.Views;

public partial class OnboardingView : UserControl
{
    private readonly OnboardingViewModel _vm;

    public OnboardingView(OnboardingViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OnboardingViewModel.Step) or null) UpdateStep();
            if (e.PropertyName is nameof(OnboardingViewModel.SigningIn) or nameof(OnboardingViewModel.SignInError) or null) UpdateGmailPanel();
            if (e.PropertyName is nameof(OnboardingViewModel.KeyState) or null) UpdateKeyStatus();
        };
        _vm.AppState.Auth.PropertyChanged += (_, _) => UpdateGmailPanel();

        ClientIdBox.Text = _vm.AppState.Prefs.GoogleClientId;
        UpdateStep();
        UpdateGmailPanel();
        UpdateKeyStatus();
    }

    // MARK: - Step routing

    private void UpdateStep()
    {
        WelcomePanel.Visibility = _vm.Step == OnboardingStep.Welcome ? Visibility.Visible : Visibility.Collapsed;
        GmailPanel.Visibility = _vm.Step == OnboardingStep.Gmail ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPanel.Visibility = _vm.Step == OnboardingStep.ApiKey ? Visibility.Visible : Visibility.Collapsed;
        ImportPanel.Visibility = _vm.Step == OnboardingStep.ImportGuide ? Visibility.Visible : Visibility.Collapsed;

        BackButton.Visibility = _vm.Step == OnboardingStep.Welcome ? Visibility.Hidden : Visibility.Visible;
        ContinueButton.Visibility = _vm.Step == OnboardingStep.ImportGuide ? Visibility.Collapsed : Visibility.Visible;
        ContinueButton.Content = _vm.ContinueTitle;

        StepDots.Children.Clear();
        foreach (var step in _vm.AllSteps)
        {
            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(3, 0, 3, 0),
                Fill = step == _vm.Step
                    ? (Brush)FindResource("AccentBrush")
                    : new SolidColorBrush(Color.FromArgb(80, 0x6B, 0x6B, 0x72)),
            };
            StepDots.Children.Add(dot);
        }
    }

    // MARK: - Gmail step

    private void ClientIdBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _vm.AppState.Prefs.GoogleClientId = ClientIdBox.Text;
        var configured = AppConfig.IsGoogleConfigured;
        ClientIdWarning.Visibility = ClientIdBox.Text.Length > 0 && !configured ? Visibility.Visible : Visibility.Collapsed;
        ConnectGmailButton.IsEnabled = configured && !_vm.SigningIn;
    }

    private async void ConnectGmail_Click(object sender, RoutedEventArgs e) => await _vm.ConnectGmailCommand.ExecuteAsync(null);

    private void UpdateGmailPanel()
    {
        var signedIn = _vm.AppState.Auth.IsSignedIn;
        GmailConnectedPanel.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        GmailDisconnectedPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        AccountEmailText.Text = _vm.AppState.Auth.AccountEmail ?? "";
        ConnectGmailButton.Content = _vm.SigningIn ? "Connecting…" : "Connect Gmail";
        ConnectGmailButton.IsEnabled = !_vm.SigningIn && AppConfig.IsGoogleConfigured;
        SignInErrorText.Text = _vm.SignInError ?? "";
        SignInErrorText.Visibility = string.IsNullOrEmpty(_vm.SignInError) ? Visibility.Collapsed : Visibility.Visible;
        UpdateStep();
    }

    // MARK: - API key step

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e) => _vm.ApiKeyInput = ApiKeyBox.Password;

    private async void ValidateKey_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ValidateKeyCommand.ExecuteAsync(null);
        if (_vm.KeyState == KeyValidationState.Valid) ApiKeyBox.Password = "";
    }

    private void UpdateKeyStatus()
    {
        (KeyStatusText.Text, KeyStatusText.Foreground) = _vm.KeyState switch
        {
            KeyValidationState.Valid => ("Key works — you're all set.", (Brush)FindResource("SuccessBrush")),
            KeyValidationState.Invalid => ("That key didn't work. Double-check it and try again.", (Brush)FindResource("DangerBrush")),
            KeyValidationState.Idle when _vm.AppState.HasApiKey => ("A key is already saved. You can replace it above.", (Brush)FindResource("SecondaryTextBrush")),
            _ => ("", System.Windows.Media.Brushes.Transparent),
        };
        ValidateKeyButton.Content = _vm.KeyState == KeyValidationState.Validating ? "Validating…" : "Validate & Save";
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    // MARK: - Import step

    private void ImportArchive_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Mail archives (*.mbox)|*.mbox|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true) _vm.FinishAndImport(dialog.FileName);
    }

    private void Finish_Click(object sender, RoutedEventArgs e) => _vm.FinishCommand.Execute(null);

    // MARK: - Controls

    private void Back_Click(object sender, RoutedEventArgs e) => _vm.BackCommand.Execute(null);
    private void Continue_Click(object sender, RoutedEventArgs e) => _vm.NextCommand.Execute(null);
}
