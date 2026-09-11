using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JobTracker.Converters;
using JobTracker.Models;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class PipelineBoardView : UserControl
{
    private readonly MainShellViewModel _shellVm;
    private readonly ApplicationStatus[] _columns;
    private readonly HashSet<ApplicationStatus> _expandedColumns = [];
    private const int InitialColumnCap = 40;

    private static readonly StatusToBrushConverter StatusBrush = new();

    public PipelineBoardView(MainShellViewModel shellVm, ApplicationStatus[] columns)
    {
        InitializeComponent();
        _shellVm = shellVm;
        _columns = columns;

        _shellVm.Filters.Timeline ??= TimelineMath.DefaultWindow(_shellVm.TimelineDomain);
        Scrubber.Update(_shellVm.TimelineDomain, _shellVm.MonthHistogram(), _shellVm.Filters.Timeline.Value);
        Scrubber.SelectionChanged += range => _shellVm.Filters.Timeline = range;

        BuildColumns();
    }

    private void BuildColumns()
    {
        ColumnsPanel.Children.Clear();
        var applications = _shellVm.FilteredApplications;
        if (applications.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;

        var grouped = applications.GroupBy(a => a.Status).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var status in _columns)
        {
            ColumnsPanel.Children.Add(BuildColumn(status, grouped.GetValueOrDefault(status, [])));
        }
    }

    private Border BuildColumn(ApplicationStatus status, List<JobApplication> items)
    {
        var isExpanded = _expandedColumns.Contains(status);
        var visible = isExpanded ? items : items.Take(InitialColumnCap).ToList();
        var statusBrush = (SolidColorBrush)StatusBrush.Convert(status, typeof(Brush), null, System.Globalization.CultureInfo.CurrentCulture)!;

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconText = new TextBlock { Text = status.IconGlyph(), Foreground = statusBrush, VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock
        {
            Text = status.DisplayName(), FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        var countBadge = new Border
        {
            Background = (Brush)FindResource("BorderBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 2, 7, 2),
            Child = new TextBlock { Text = items.Count.ToString(), FontSize = 11 },
        };
        Grid.SetColumn(iconText, 0);
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(countBadge, 2);
        headerGrid.Children.Add(iconText);
        headerGrid.Children.Add(titleText);
        headerGrid.Children.Add(countBadge);

        var cardsPanel = new StackPanel();
        foreach (var app in visible)
        {
            var card = new ApplicationCardView(app);
            card.CardClicked += a => _shellVm.SelectedApplication = a;
            cardsPanel.Children.Add(card);
        }
        if (!isExpanded && items.Count > InitialColumnCap)
        {
            var showAll = new Button
            {
                Content = $"Show all {items.Count}…",
                Style = (Style)FindResource("PlainButton"),
                Foreground = (Brush)FindResource("AccentBrush"),
                HorizontalAlignment = HorizontalAlignment.Left,
                FontSize = 12,
            };
            showAll.Click += (_, _) => { _expandedColumns.Add(status); BuildColumns(); };
            cardsPanel.Children.Add(showAll);
        }

        var scroll = new ScrollViewer { Content = cardsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var columnStack = new StackPanel();
        columnStack.Children.Add(headerGrid);
        columnStack.Children.Add(scroll);

        var border = new Border
        {
            Width = _columns.Length == 1 ? 340 : 260,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 16, 0),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(16, statusBrush.Color.R, statusBrush.Color.G, statusBrush.Color.B)),
            Child = columnStack,
            AllowDrop = true,
            Tag = status,
        };
        border.Drop += Column_Drop;
        return border;
    }

    private void Column_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: ApplicationStatus status }) return;
        if (!e.Data.GetDataPresent("JobApplicationId")) return;
        if (e.Data.GetData("JobApplicationId") is not Guid id) return;
        var app = _shellVm.Applications.FirstOrDefault(a => a.Id == id);
        if (app is null) return;
        _shellVm.MoveToStatus(app, status);
        BuildColumns();
    }
}
