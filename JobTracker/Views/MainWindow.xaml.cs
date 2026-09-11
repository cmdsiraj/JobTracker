using System.Windows;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class MainWindow : Window
{
    private readonly AppState _appState;

    public MainWindow(AppState appState)
    {
        InitializeComponent();
        _appState = appState;

        Closing += (_, e) =>
        {
            if (((App)Application.Current).IsExiting) return;
            // Tray-app pattern: hide instead of quitting, so the tray icon
            // and its "Sync Now"/watcher keep working. The tray's Quit
            // action goes through App.RequestExit(), which sets IsExiting
            // first so this check lets the real close through.
            e.Cancel = true;
            Hide();
        };

        ShowRoot();
    }

    public void ShowRoot()
    {
        if (_appState.NeedsOnboarding)
        {
            var vm = new OnboardingViewModel(_appState);
            vm.Finished += ShowRoot;
            RootContent.Children.Clear();
            RootContent.Children.Add(new OnboardingView(vm));
        }
        else
        {
            RootContent.Children.Clear();
            RootContent.Children.Add(new MainShellView(new MainShellViewModel(_appState)));
        }
    }
}
