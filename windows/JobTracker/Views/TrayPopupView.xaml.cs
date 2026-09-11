using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JobTracker.Converters;
using JobTracker.Models;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class TrayPopupView : UserControl
{
    private readonly TrayViewModel _vm;
    private static readonly RelativeTimeConverter RelativeTime = new();
    private static readonly StatusToBrushConverter StatusBrush = new();

    public TrayPopupView(TrayViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _vm.AppState.Pipeline.PropertyChanged += (_, _) => Render();
        _vm.AppState.Auth.PropertyChanged += (_, _) => Render();
        Render();
    }

    private void Render()
    {
        _vm.Reload();

        StatusPanel.Children.Clear();
        if (_vm.AppState.Pipeline.IsRunning)
        {
            StatusPanel.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0) });
        }
        else
        {
            StatusPanel.Children.Add(new Border
            {
                Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 4, 0),
                Background = _vm.AppState.Auth.IsSignedIn ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("WarningBrush"),
            });
        }
        StatusPanel.Children.Add(new TextBlock { Text = _vm.StatusText, FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });

        RecentPanel.Children.Clear();
        NoAppsText.Visibility = _vm.RecentApplications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var app in _vm.RecentApplications)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock
            {
                Text = app.Status.IconGlyph(),
                Foreground = (Brush)StatusBrush.Convert(app.Status, typeof(Brush), null, System.Globalization.CultureInfo.CurrentCulture)!,
                Width = 20, VerticalAlignment = VerticalAlignment.Center,
            });
            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock { Text = app.Company, FontSize = 12 });
            textStack.Children.Add(new TextBlock { Text = app.Status.DisplayName(), FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush") });
            stack.Children.Add(textStack);
            row.Children.Add(stack);
            row.Children.Add(new TextBlock
            {
                Text = (string)RelativeTime.Convert(app.LastUpdated, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture)!,
                FontSize = 10, Foreground = (Brush)FindResource("TertiaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Right,
            });
            RecentPanel.Children.Add(row);
        }

        SyncNowButton.IsEnabled = !_vm.AppState.Pipeline.IsRunning;
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e) => await _vm.SyncNow();
    private void Logs_Click(object sender, RoutedEventArgs e) => new ActivityLogWindow().Show();
    private void Open_Click(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).ShowMainWindow();
    private void Quit_Click(object sender, RoutedEventArgs e) => ((App)System.Windows.Application.Current).RequestExit();
}
