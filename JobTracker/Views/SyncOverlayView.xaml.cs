// Floating staged-progress card shown while the sync pipeline runs: a
// vertical checklist of stages with live counts. Visibility is controlled
// by the parent (MainShellView) from pipeline.Stage via MainShellViewModel.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JobTracker.Services;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class SyncOverlayView : UserControl
{
    private readonly SyncPipeline _pipeline;

    public SyncOverlayView(SyncPipeline pipeline)
    {
        InitializeComponent();
        _pipeline = pipeline;
        _pipeline.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SyncPipeline.Stage)) Render(); };
        Render();
    }

    private void Render()
    {
        var stage = _pipeline.Stage;

        HeaderPanel.Children.Clear();
        switch (stage)
        {
            case SyncStage.Finished finished:
                HeaderPanel.Children.Add(new TextBlock { Text = "✅", FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
                var doneStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                doneStack.Children.Add(new TextBlock { Text = "Sync Complete", FontWeight = FontWeights.SemiBold });
                doneStack.Children.Add(new TextBlock
                {
                    Text = finished.Updates == 0 ? "Everything is up to date." : $"{finished.Updates} application(s) updated.",
                    FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"),
                });
                HeaderPanel.Children.Add(doneStack);
                break;
            case SyncStage.Failed:
                HeaderPanel.Children.Add(new TextBlock { Text = "⚠️", FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
                HeaderPanel.Children.Add(new TextBlock { Text = "Sync Failed", FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                break;
            default:
                HeaderPanel.Children.Add(new TextBlock { Text = "🔄", FontSize = 18, VerticalAlignment = VerticalAlignment.Center });
                HeaderPanel.Children.Add(new TextBlock { Text = "Syncing", FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                break;
        }

        ChecklistPanel.Children.Clear();
        FailureText.Visibility = Visibility.Collapsed;

        if (stage is SyncStage.Failed failed)
        {
            FailureText.Text = failed.Message;
            FailureText.Visibility = Visibility.Visible;
        }
        else if (stage is not SyncStage.Finished)
        {
            var current = stage.CurrentStepIndex();
            foreach (var step in SyncStageExtensions.AllSteps)
            {
                ChecklistPanel.Children.Add(StepRow(step, current, stage));
            }
        }

        CancelButton.Visibility = stage.IsCancellable() ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid StepRow(SyncStep step, int? current, SyncStage stage)
    {
        var isDone = current is { } c && (int)step < c;
        var isActive = current is { } c2 && (int)step == c2;

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        UIElement marker = isDone
            ? new TextBlock { Text = "✓", Foreground = (Brush)FindResource("SuccessBrush"), FontWeight = FontWeights.Bold, Width = 18 }
            : isActive
                ? new ProgressBar { IsIndeterminate = true, Width = 16, Height = 16 }
                : new TextBlock { Text = "○", Foreground = (Brush)FindResource("TertiaryTextBrush"), Width = 18 };
        Grid.SetColumn(marker, 0);

        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock
        {
            Text = step.Title() + (isActive ? "…" : ""), FontSize = 12,
            Foreground = !isDone && !isActive ? (Brush)FindResource("TertiaryTextBrush") : (Brush)FindResource("PrimaryTextBrush"),
        });
        if (isActive && stage.DetailText() is { } detail)
        {
            textStack.Children.Add(new TextBlock { Text = detail, FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush") });
        }
        Grid.SetColumn(textStack, 1);

        grid.Children.Add(marker);
        grid.Children.Add(textStack);
        return grid;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _pipeline.CancelImport();
}
