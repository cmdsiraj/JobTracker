using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JobTracker.Services;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class ActivityLogWindow : Window
{
    private readonly ActivityLogViewModel _vm = new();

    public ActivityLogWindow()
    {
        InitializeComponent();
        _vm.PropertyChanged += (_, _) => Render();
        Render();
    }

    private void Render()
    {
        var entries = _vm.Entries;
        EntryCountText.Text = $"{entries.Count} entries";
        EmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        LogItems.Items.Clear();
        foreach (var entry in entries)
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            stack.Children.Add(new TextBlock { Text = entry.Date.ToString("HH:mm:ss"), FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = (Brush)FindResource("TertiaryTextBrush"), Width = 70 });
            stack.Children.Add(new TextBlock { Text = entry.Level.Glyph(), FontFamily = new FontFamily("Consolas"), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = ColorFor(entry.Level), Width = 16 });
            stack.Children.Add(new TextBlock { Text = entry.Message, FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
            LogItems.Items.Add(stack);
        }

        if (AutoScrollCheck.IsChecked == true) LogScroll.ScrollToBottom();
    }

    private Brush ColorFor(ActivityLog.Level level) => level switch
    {
        ActivityLog.Level.Success => (Brush)FindResource("SuccessBrush"),
        ActivityLog.Level.Warning => (Brush)FindResource("WarningBrush"),
        ActivityLog.Level.Error => (Brush)FindResource("DangerBrush"),
        _ => (Brush)FindResource("SecondaryTextBrush"),
    };

    private void Copy_Click(object sender, RoutedEventArgs e) => _vm.Copy();
    private void Clear_Click(object sender, RoutedEventArgs e) => _vm.Clear();
}
