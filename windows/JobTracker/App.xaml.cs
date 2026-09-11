using System.Diagnostics;
using System.Windows;
using JobTracker.Data;
using JobTracker.ViewModels;
using JobTracker.Views;

namespace JobTracker;

public partial class App : Application
{
    public JobTrackerDbContext Context { get; private set; } = null!;
    public AppState AppState { get; private set; } = null!;

    /// True once the app is intentionally exiting (tray "Quit", or a
    /// data-erase restart) — MainWindow's Closing handler checks this to
    /// distinguish a real quit from the user clicking the window's close
    /// button, which should just hide it (tray-app pattern).
    public bool IsExiting { get; private set; }

    public void RequestExit()
    {
        IsExiting = true;
        Shutdown();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        StoreManager.PerformArchitectureResetIfNeeded();
        Context = StoreManager.MakeContext();
        AppState = new AppState(Context);

        ShowMainWindow();
    }

    public void ShowMainWindow()
    {
        if (MainWindow is MainWindow existing)
        {
            existing.Show();
            existing.Activate();
            return;
        }
        var window = new MainWindow(AppState);
        MainWindow = window;
        window.Show();
    }

    /// Disposes the live DB connection, wipes the store, and relaunches the
    /// process — Windows won't delete a file a connection still has open,
    /// and every ViewModel in the running app holds references into the
    /// now-replaced store, so an in-place swap isn't safe. See
    /// SettingsViewModel.RequestEraseAndRestart.
    public void EraseAllDataAndRestart()
    {
        IsExiting = true;
        Context.Dispose();
        StoreManager.PerformArchitectureResetIfNeeded(force: true);
        if (Environment.ProcessPath is { } exePath)
        {
            Process.Start(exePath);
        }
        Shutdown();
    }
}
