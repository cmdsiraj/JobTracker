using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JobTracker.Converters;
using JobTracker.Models;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class ReviewView : UserControl
{
    private readonly ReviewViewModel _vm;
    private static readonly StatusToBrushConverter StatusBrush = new();
    private static readonly PercentConverter Percent = new();

    public ReviewView(ReviewViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReviewViewModel.Applications)) BuildRows();
            if (e.PropertyName == nameof(ReviewViewModel.MergeResult)) ShowMergeResult();
        };
        BuildRows();
    }

    private void BuildRows()
    {
        RowsPanel.Children.Clear();
        EmptyState.Visibility = _vm.Applications.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var app in _vm.Applications)
        {
            RowsPanel.Children.Add(BuildRow(app));
        }
    }

    private Border BuildRow(JobApplication app)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock { Text = app.Status.IconGlyph(), Foreground = (Brush)StatusBrush.Convert(app.Status, typeof(Brush), null, System.Globalization.CultureInfo.CurrentCulture)!, VerticalAlignment = VerticalAlignment.Center, Width = 24 };
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = app.Company.Length == 0 ? "Unknown Company" : app.Company, FontWeight = FontWeights.SemiBold });
        if (app.RoleTitle.Length > 0) titleStack.Children.Add(new TextBlock { Text = app.RoleTitle, FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var confirm = new Button { Content = "✓ Looks Right", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, 0, 0, 0) };
        confirm.Click += (_, _) => _vm.Confirm(app);
        var open = new Button { Content = "↗ Open", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, 0, 0, 0) };
        open.Click += (_, _) => _vm.Open(app);
        var delete = new Button { Content = "🗑 Delete", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(4, 0, 0, 0), Foreground = (Brush)FindResource("DangerBrush") };
        delete.Click += (_, _) => _vm.Delete(app);
        buttons.Children.Add(confirm);
        buttons.Children.Add(open);
        buttons.Children.Add(delete);

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(titleStack, 1);
        Grid.SetColumn(buttons, 2);
        headerGrid.Children.Add(icon);
        headerGrid.Children.Add(titleStack);
        headerGrid.Children.Add(buttons);
        stack.Children.Add(headerGrid);

        foreach (var evt in ReviewViewModel.LowConfidenceEvents(app))
        {
            var eventGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            var eventStack = new StackPanel { Orientation = Orientation.Horizontal };
            eventStack.Children.Add(new TextBlock { Text = "❓ " + Percent.Convert(evt.MatchConfidence, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture), FontSize = 10, Foreground = (Brush)FindResource("WarningBrush"), VerticalAlignment = VerticalAlignment.Center, Width = 60 });
            var textStack = new StackPanel();
            textStack.Children.Add(new TextBlock { Text = evt.Subject, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            textStack.Children.Add(new TextBlock { Text = evt.Snippet, FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            eventStack.Children.Add(textStack);
            var border = new Border
            {
                Background = (Brush)FindResource("SidebarBackgroundBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(8),
                Child = eventStack,
            };
            eventGrid.Children.Add(border);
            stack.Children.Add(eventGrid);
        }

        return new Border { Padding = new Thickness(0, 0, 0, 4), Child = stack };
    }

    private void MergeDuplicates_Click(object sender, RoutedEventArgs e) => _vm.MergeDuplicates();

    private async void ShowMergeResult()
    {
        if (_vm.MergeResult is not { } count) return;
        MergeResultText.Text = count == 0 ? "No duplicates found" : $"Merged {count} duplicate{(count == 1 ? "" : "s")}";
        MergeResultText.Visibility = Visibility.Visible;
        await System.Threading.Tasks.Task.Delay(2500);
        MergeResultText.Visibility = Visibility.Collapsed;
    }
}
