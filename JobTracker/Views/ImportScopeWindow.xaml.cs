using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JobTracker.Services;

namespace JobTracker.Views;

public partial class ImportScopeWindow : Window
{
    private readonly PendingImport _pending;
    private readonly SyncPipeline _pipeline;
    private bool _confirmed;

    public ImportScopeWindow(PendingImport pending, SyncPipeline pipeline)
    {
        InitializeComponent();
        _pending = pending;
        _pipeline = pipeline;

        SummaryText.Text =
            $"Scanned {pending.Summary.TotalMessages:N0} emails and found {pending.Summary.Candidates:N0} " +
            "job-related candidates. Classification runs in batches of 8 per API request — pick how far back to import.";

        ScopeButtonsPanel.Children.Add(ScopeButton("Current cycle",
            $"Since {PendingImport.CurrentCycleStart:MMMM yyyy} — what you're actively tracking",
            pending.CurrentCycleCount, ImportScope.CurrentCycle, prominent: true));
        ScopeButtonsPanel.Children.Add(ScopeButton("Last 12 months",
            "Includes the tail of the previous cycle",
            pending.LastYearCount, ImportScope.LastYear, prominent: false));
        ScopeButtonsPanel.Children.Add(ScopeButton("Everything",
            "The whole archive — may exceed free-tier API credits",
            pending.Candidates.Count, ImportScope.Everything, prominent: false));

        Closed += (_, _) => { if (!_confirmed) _pipeline.DiscardPendingImport(); };
    }

    private static string Estimate(int count)
    {
        var seconds = count / 8.0 * 2.5;
        if (seconds < 60) return $"{count:N0} emails · under a minute";
        var minutes = (int)Math.Ceiling(seconds / 60);
        return $"{count:N0} emails · ~{minutes} min";
    }

    private Border ScopeButton(string title, string subtitle, int count, ImportScope scope, bool prominent)
    {
        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        textStack.Children.Add(new TextBlock { Text = subtitle, FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap });

        var grid = new Grid();
        grid.Children.Add(textStack);
        var estimateText = new TextBlock
        {
            Text = Estimate(count), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Foreground = prominent ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SecondaryTextBrush"),
        };
        grid.Children.Add(estimateText);

        var border = new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb((byte)(prominent ? 40 : 20), 0x6B, 0x6B, 0x72)),
            Child = grid,
            Cursor = count > 0 ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
            Opacity = count > 0 ? 1.0 : 0.5,
        };
        if (count > 0)
        {
            border.MouseLeftButtonUp += (_, _) =>
            {
                _confirmed = true;
                _pipeline.ConfirmImport(scope);
                Close();
            };
        }
        return border;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
