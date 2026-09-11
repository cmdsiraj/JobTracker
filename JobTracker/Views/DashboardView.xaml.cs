// Analytics overview. Rebuilds its card grid from DashboardViewModel.Data
// whenever it changes (scope/cycle change or a data reload) — LiveCharts2
// controls are cheap enough to recreate for this dataset size, and it keeps
// the view free of incremental-update bugs.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using JobTracker.Converters;
using JobTracker.Models;
using JobTracker.ViewModels;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using SkiaSharp;

namespace JobTracker.Views;

public partial class DashboardView : UserControl
{
    private readonly DashboardViewModel _vm;
    private static readonly StatusToBrushConverter StatusBrushConverter = new();

    public DashboardView(DashboardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        Scrubber.SelectionChanged += range => _vm.Timeline = range;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.Data)) Render();
            if (e.PropertyName == nameof(DashboardViewModel.Applications)) UpdateScrubber();
        };

        UpdateScrubber();
        Render();
    }

    private void UpdateScrubber()
    {
        var domain = _vm.ScrubberDomain;
        Scrubber.Update(domain, _vm.MonthHistogram(), _vm.Timeline ?? TimelineMath.DefaultWindow(domain));
    }

    private static Brush StatusBrush(ApplicationStatus status) =>
        (Brush)StatusBrushConverter.Convert(status, typeof(Brush), null, System.Globalization.CultureInfo.CurrentCulture)!;

    // MARK: - Root render

    private void Render()
    {
        ContentPanel.Children.Clear();
        BuildCycleChips();

        if (_vm.Data.IsEmpty)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;

        var stats = new WrapPanel();
        stats.Children.Add(StatCard("Total", _vm.Data.Total, "📥", (Brush)FindResource("AccentBrush")));
        stats.Children.Add(StatCard("This Week", _vm.Data.ThisWeek, "🗓", Brushes.MediumPurple));
        stats.Children.Add(StatCard("This Month", _vm.Data.ThisMonth, "📅", (Brush)FindResource("SuccessBrush")));
        stats.Children.Add(StatCard("This Year", _vm.Data.ThisYear, "✨", (Brush)FindResource("WarningBrush")));
        stats.Children.Add(StatCard("Active", _vm.Data.Active, "⚡", (Brush)FindResource("SuccessBrush")));
        ContentPanel.Children.Add(stats);

        ContentPanel.Children.Add(Card("Pipeline at a Glance", "📚", StatusBreakdownRow(), fullWidth: true));

        var charts = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        charts.Children.Add(Card("Daily Activity", "📈", DailyActivityChart()));
        charts.Children.Add(Card("Applications per Week", "📊", WeeklyChart()));
        charts.Children.Add(Card("Pipeline Funnel", "🔻", FunnelChart()));
        charts.Children.Add(Card("Current Status Mix", "🥧", DonutCardContent()));
        charts.Children.Add(Card("Activity Heatmap — Last 16 Weeks", "🔥", HeatmapGrid()));
        charts.Children.Add(Card("Cycle Comparison", "🔄", CycleChart()));
        ContentPanel.Children.Add(charts);
    }

    // MARK: - Scope bar

    private void BuildCycleChips()
    {
        CycleChipsPanel.Children.Clear();
        var cycles = _vm.AvailableCycles;
        if (cycles.Count == 0)
        {
            CycleChipsScroll.Visibility = Visibility.Collapsed;
            return;
        }
        CycleChipsScroll.Visibility = Visibility.Visible;

        CycleChipsPanel.Children.Add(Chip("All Cycles", _vm.SelectedCycles.Count == 0, _vm.ClearCycles));
        foreach (var cycle in cycles)
        {
            var captured = cycle;
            CycleChipsPanel.Children.Add(Chip(captured, _vm.SelectedCycles.Contains(captured), () => _vm.ToggleCycle(captured)));
        }
    }

    private Border Chip(string title, bool isOn, Action action)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0),
            Background = isOn ? new SolidColorBrush(Color.FromArgb(40, 0x35, 0x7A, 0xBD)) : (Brush)FindResource("BorderBrush"),
            BorderBrush = isOn ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = title, FontSize = 12, FontWeight = isOn ? FontWeights.SemiBold : FontWeights.Normal },
        };
        border.MouseLeftButtonUp += (_, _) => action();
        return border;
    }

    // MARK: - Headline stats

    private Border StatCard(string title, int value, string icon, Brush tint)
    {
        var stack = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = icon, FontSize = 12, Foreground = tint, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock
        {
            Text = title, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = tint,
            Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(header);
        stack.Children.Add(new TextBlock { Text = value.ToString(), FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
        return new Border { Style = (Style)FindResource("CardBorder"), Child = stack, MinWidth = 150, Margin = new Thickness(0, 0, 12, 12) };
    }

    // MARK: - Status breakdown

    private UIElement StatusBreakdownRow()
    {
        var grid = new UniformGrid { Rows = 1 };
        foreach (var status in ApplicationStatusExtensions.BoardColumns)
        {
            var count = _vm.Data.StatusCounts.FirstOrDefault(sc => sc.Status == status).Count;
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(new TextBlock { Text = status.IconGlyph(), Foreground = StatusBrush(status), HorizontalAlignment = HorizontalAlignment.Center });
            stack.Children.Add(new TextBlock { Text = count.ToString(), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
            stack.Children.Add(new TextBlock
            {
                Text = status.DisplayName(), FontSize = 9, Foreground = (Brush)FindResource("SecondaryTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            grid.Children.Add(stack);
        }
        return grid;
    }

    // MARK: - Charts

    private CartesianChart DailyActivityChart()
    {
        var values = _vm.Data.Daily.Select(d => (double)d.Count).ToArray();
        var labels = _vm.Data.Daily.Select(d => d.Date.ToString("M/d")).ToArray();
        return new CartesianChart
        {
            Height = 180,
            Series =
            [
                new LineSeries<double>
                {
                    Values = values,
                    Fill = new SolidColorPaint(new SKColor(0x35, 0x7A, 0xBD, 60)),
                    Stroke = new SolidColorPaint(new SKColor(0x35, 0x7A, 0xBD)) { StrokeThickness = 2 },
                    GeometrySize = 0,
                    Name = "Applications",
                },
            ],
            XAxes = [new Axis { Labeler = v => Label(labels, v), LabelsRotation = 0, MinStep = 5, ForceStepToMin = true }],
            YAxes = [new Axis { MinLimit = 0 }],
        };
    }

    private CartesianChart WeeklyChart()
    {
        var values = _vm.Data.Weekly.Select(w => (double)w.Count).ToArray();
        var labels = _vm.Data.Weekly.Select(w => w.Date.ToString("M/d")).ToArray();
        return new CartesianChart
        {
            Height = 180,
            Series =
            [
                new ColumnSeries<double>
                {
                    Values = values,
                    Fill = new SolidColorPaint(new SKColor(0x0E, 0x8E, 0x80)),
                    Name = "Applications",
                },
            ],
            XAxes = [new Axis { Labeler = v => Label(labels, v) }],
            YAxes = [new Axis { MinLimit = 0 }],
        };
    }

    private CartesianChart FunnelChart()
    {
        var values = _vm.Data.Funnel.Select(f => (double)f.Count).ToArray();
        var labels = _vm.Data.Funnel.Select(f => f.Status.DisplayName()).ToArray();
        return new CartesianChart
        {
            Height = 200,
            Series =
            [
                new RowSeries<double>
                {
                    Values = values,
                    Fill = new SolidColorPaint(new SKColor(0x8B, 0x5C, 0xF6)),
                    Name = "Count",
                    DataLabelsPaint = new SolidColorPaint(new SKColor(0x6B, 0x6B, 0x72)),
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.End,
                    ShowDataLabels = true,
                },
            ],
            XAxes = [new Axis { MinLimit = 0, IsVisible = false }],
            YAxes = [new Axis { Labels = labels }],
        };
    }

    private UIElement DonutCardContent()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var series = _vm.Data.StatusCounts.Select(sc =>
        {
            var brush = (SolidColorBrush)StatusBrush(sc.Status);
            return new PieSeries<double>
            {
                Values = [sc.Count],
                Name = sc.Status.DisplayName(),
                Fill = new SolidColorPaint(new SKColor(brush.Color.R, brush.Color.G, brush.Color.B)),
                InnerRadius = 55,
            };
        }).ToArray();
        row.Children.Add(new PieChart { Height = 190, Width = 190, Series = series });

        var legend = new StackPanel { Width = 130, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var sc in _vm.Data.StatusCounts)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            line.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = StatusBrush(sc.Status), Margin = new Thickness(0, 0, 6, 0) });
            line.Children.Add(new TextBlock { Text = sc.Status.DisplayName(), FontSize = 11, Width = 70, TextTrimming = TextTrimming.CharacterEllipsis });
            line.Children.Add(new TextBlock { Text = sc.Count.ToString(), FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });
            legend.Children.Add(line);
        }
        row.Children.Add(legend);
        return row;
    }

    private UIElement HeatmapGrid()
    {
        var data = _vm.Data.Heat;
        var maxCount = Math.Max(1, data.Count > 0 ? data.Max(h => h.Count) : 1);

        var grid = new Grid { Height = 140 };
        for (var w = 0; w < 16; w++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var d = 0; d < 7; d++) grid.RowDefinitions.Add(new RowDefinition());

        for (var w = 0; w < 16; w++)
        {
            for (var d = 0; d < 7; d++)
            {
                var idx = w * 7 + d;
                if (idx >= data.Count) continue;
                var cell = data[idx];
                var alpha = cell.Count == 0 ? 0.08 : 0.25 + 0.75 * cell.Count / maxCount;
                var rect = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 0x22, 0xA5, 0x5D)),
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(1.5),
                    ToolTip = $"{cell.WeekLabel.Trim()} {cell.DayLabel}: {cell.Count}",
                };
                Grid.SetColumn(rect, w);
                Grid.SetRow(rect, d);
                grid.Children.Add(rect);
            }
        }

        var labelsRow = new Grid { Height = 16 };
        for (var w = 0; w < 16; w++) labelsRow.ColumnDefinitions.Add(new ColumnDefinition());
        for (var w = 0; w < 16; w++)
        {
            var label = data.Count > w * 7 ? data[w * 7].WeekLabel.Trim() : "";
            var tb = new TextBlock { Text = label, FontSize = 9, Foreground = (Brush)FindResource("TertiaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(tb, w);
            labelsRow.Children.Add(tb);
        }

        var stack = new StackPanel();
        stack.Children.Add(labelsRow);
        stack.Children.Add(grid);
        return stack;
    }

    private UIElement CycleChart()
    {
        if (_vm.Data.Cycles.Count == 0)
        {
            return new TextBlock
            {
                Text = "No recruiting cycles detected yet.", Foreground = (Brush)FindResource("SecondaryTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 40, 0, 40),
            };
        }
        var values = _vm.Data.Cycles.Select(c => (double)c.Count).ToArray();
        var labels = _vm.Data.Cycles.Select(c => c.Cycle).ToArray();
        return new CartesianChart
        {
            Height = 180,
            Series =
            [
                new ColumnSeries<double>
                {
                    Values = values,
                    Fill = new SolidColorPaint(new SKColor(0x63, 0x66, 0xF1)),
                    Name = "Applications",
                },
            ],
            XAxes = [new Axis { Labels = labels }],
            YAxes = [new Axis { IsVisible = false, MinLimit = 0 }],
        };
    }

    private static string Label(string[] labels, double value)
    {
        var i = (int)Math.Round(value);
        return i >= 0 && i < labels.Length ? labels[i] : "";
    }

    // MARK: - Card shell

    private Border Card(string title, string icon, UIElement content, bool fullWidth = false)
    {
        var stack = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock { Text = icon, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        stack.Children.Add(header);
        stack.Children.Add(content);
        return new Border
        {
            Style = (Style)FindResource("CardBorder"),
            Child = stack,
            Width = fullWidth ? double.NaN : 400,
            Margin = new Thickness(0, 0, 16, 16),
        };
    }
}
