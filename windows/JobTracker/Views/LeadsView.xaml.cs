using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JobTracker.Models;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class LeadsView : UserControl
{
    private readonly LeadsViewModel _vm;

    public LeadsView(LeadsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        _vm.PropertyChanged += (_, _) => { BuildStageFilter(); BuildList(); };
        BuildStageFilter();
        BuildList();
    }

    private void BuildStageFilter()
    {
        StageFilterPanel.Children.Clear();
        StageFilterPanel.Children.Add(StageChip("All", _vm.StageFilter is null, () => _vm.StageFilter = null));
        foreach (var stage in Enum.GetValues<LeadStage>())
        {
            StageFilterPanel.Children.Add(StageChip(stage.DisplayName(), _vm.StageFilter == stage, () => _vm.StageFilter = stage));
        }
    }

    private Border StageChip(string title, bool isOn, Action action)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Background = isOn ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = title, FontSize = 11, Foreground = isOn ? Brushes.White : (Brush)FindResource("SecondaryTextBrush") },
        };
        border.MouseLeftButtonUp += (_, _) => action();
        return border;
    }

    private void BuildList()
    {
        LeadsPanel.Children.Clear();
        var leads = _vm.FilteredLeads;
        EmptyState.Visibility = _vm.Leads.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var lead in leads)
        {
            LeadsPanel.Children.Add(BuildRow(lead));
        }
    }

    private Border BuildRow(Lead lead)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = lead.Type == LeadType.JobPosting ? "🔗" : "👤",
            FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Width = 28,
        };
        Grid.SetColumn(icon, 0);

        var (primary, secondary) = TitlesFor(lead);
        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock { Text = primary, FontWeight = FontWeights.SemiBold, FontSize = 13 });
        if (secondary.Length > 0)
        {
            textStack.Children.Add(new TextBlock { Text = secondary, FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush") });
        }
        Grid.SetColumn(textStack, 1);

        var linkButton = new Button
        {
            Content = "🔗", Style = (Style)FindResource("PlainButton"),
            Visibility = lead.LinkUrl is not null ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 0, 10, 0),
        };
        if (lead.LinkUrl is { } url)
        {
            linkButton.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        Grid.SetColumn(linkButton, 2);

        var stageMenuButton = new Button
        {
            Style = (Style)FindResource("PlainButton"),
            Content = new Border
            {
                Background = (Brush)FindResource("BorderBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2),
                Child = new TextBlock { Text = lead.Stage.DisplayName(), FontSize = 11, FontWeight = FontWeights.Medium },
            },
        };
        var stageMenu = new ContextMenu();
        foreach (var stage in Enum.GetValues<LeadStage>())
        {
            var item = new MenuItem { Header = stage.DisplayName(), IsCheckable = true, IsChecked = lead.Stage == stage };
            item.Click += (_, _) => _vm.SetStage(lead, stage);
            stageMenu.Items.Add(item);
        }
        stageMenuButton.Click += (_, _) => { stageMenu.PlacementTarget = stageMenuButton; stageMenu.IsOpen = true; };
        Grid.SetColumn(stageMenuButton, 3);

        grid.Children.Add(icon);
        grid.Children.Add(textStack);
        grid.Children.Add(linkButton);
        grid.Children.Add(stageMenuButton);

        var rowContextMenu = new ContextMenu();
        var convertItem = new MenuItem { Header = "Convert to Application" };
        convertItem.Click += (_, _) => _vm.Convert(lead);
        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => _vm.Delete(lead);
        rowContextMenu.Items.Add(convertItem);
        rowContextMenu.Items.Add(new Separator());
        rowContextMenu.Items.Add(deleteItem);

        return new Border { Padding = new Thickness(0, 8, 0, 8), ContextMenu = rowContextMenu, Child = grid };
    }

    private static (string Primary, string Secondary) TitlesFor(Lead lead) => lead.Type switch
    {
        LeadType.JobPosting => (
            lead.Title.Length > 0 ? lead.Title : (lead.Company.Length > 0 ? lead.Company : "Untitled Posting"),
            lead.Company),
        LeadType.Person => (
            lead.PersonName.Length > 0 ? lead.PersonName : "Unnamed Contact",
            string.Join(" · ", new[] { lead.Title, lead.Company }.Where(s => s.Length > 0))),
        _ => ("", ""),
    };

    private void AddLead_Click(object sender, RoutedEventArgs e)
    {
        var window = new AddLeadWindow(_vm) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}
