using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JobTracker.Models;
using JobTracker.Services;
using JobTracker.ViewModels;
using Microsoft.Win32;

namespace JobTracker.Views;

public partial class MainShellView : UserControl
{
    private readonly MainShellViewModel _vm;
    private ApplicationDetailWindow? _detailWindow;
    private ImportScopeWindow? _importScopeWindow;

    public MainShellView(MainShellViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _vm.PropertyChanged += Vm_PropertyChanged;
        _vm.Filters.Changed += () =>
        {
            BuildSidebar();
            if (_vm.Selection.Section is SidebarSection.AllApplications or SidebarSection.Status) RefreshContent();
        };
        _vm.AppState.Pipeline.PropertyChanged += Pipeline_PropertyChanged;

        _vm.Reload();
        BuildSidebar();
        RefreshContent();
        UpdateToolbarSyncStatus();
        UpdateOverlay();

        Loaded += (_, _) => _vm.AppState.Pipeline.SyncOnLaunchIfNeeded();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainShellViewModel.Selection):
                BuildSidebar();
                RefreshContent();
                break;
            case nameof(MainShellViewModel.Applications):
                BuildSidebar();
                RefreshContent();
                break;
            case nameof(MainShellViewModel.StatusTally):
            case nameof(MainShellViewModel.ReviewCount):
                // Board content already refreshed itself locally (e.g. after
                // a drag-drop move) — just the sidebar badge counts are stale.
                BuildSidebar();
                break;
            case nameof(MainShellViewModel.OverlayVisible):
                UpdateOverlay();
                break;
            case nameof(MainShellViewModel.SelectedApplication):
                HandleSelectedApplicationChanged();
                break;
        }
    }

    private void Pipeline_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncPipeline.Stage)) UpdateToolbarSyncStatus();
        if (e.PropertyName == nameof(SyncPipeline.PendingImport)) HandlePendingImport();
    }

    // MARK: - Sidebar

    private void BuildSidebar()
    {
        SidebarList.SelectionChanged -= SidebarList_SelectionChanged;
        SidebarList.Items.Clear();

        void AddHeader(string title) => SidebarList.Items.Add(MakeHeaderItem(title));
        void AddRow(string icon, string title, int? badge, SidebarItem item)
        {
            var row = MakeRowItem(icon, title, badge);
            row.Tag = item;
            SidebarList.Items.Add(row);
            if (item.Equals(_vm.Selection)) SidebarList.SelectedItem = row;
        }

        AddHeader("OVERVIEW");
        AddRow("📊", "Dashboard", null, SidebarItem.Dashboard);
        AddHeader("PIPELINE");
        AddRow("📥", "All Applications", _vm.FilteredApplications.Count, SidebarItem.AllApplications);
        foreach (var status in ApplicationStatusExtensions.BoardColumns.Where(s => s != ApplicationStatus.Outreach))
        {
            AddRow(status.IconGlyph(), status.DisplayName(), _vm.StatusTally.GetValueOrDefault(status), SidebarItem.ForStatus(status));
        }
        AddHeader("OUTREACH");
        AddRow(ApplicationStatus.Outreach.IconGlyph(), ApplicationStatus.Outreach.DisplayName(),
            _vm.StatusTally.GetValueOrDefault(ApplicationStatus.Outreach), SidebarItem.ForStatus(ApplicationStatus.Outreach));
        AddHeader("LEADS");
        AddRow("✨", "Leads", null, SidebarItem.Leads);
        AddHeader("TRIAGE");
        AddRow("⚠️", "Needs Review", _vm.ReviewCount, SidebarItem.Review);

        SidebarList.SelectionChanged += SidebarList_SelectionChanged;
    }

    private static ListBoxItem MakeHeaderItem(string title) => new()
    {
        Content = new TextBlock
        {
            Text = title,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("TertiaryTextBrush"),
            Margin = new Thickness(14, 14, 0, 4),
        },
        IsEnabled = false,
        Focusable = false,
        Background = Brushes.Transparent,
    };

    private static ListBoxItem MakeRowItem(string icon, string title, int? badge)
    {
        var grid = new Grid { Margin = new Thickness(10, 6, 10, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconText = new TextBlock { Text = icon, Width = 20, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(iconText, 0);
        var titleText = new TextBlock { Text = title, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        Grid.SetColumn(titleText, 1);
        grid.Children.Add(iconText);
        grid.Children.Add(titleText);

        if (badge is { } count && count > 0)
        {
            var badgeBorder = new Border
            {
                Background = (Brush)Application.Current.FindResource("BorderBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 1, 6, 1),
                Child = new TextBlock { Text = count.ToString(), FontSize = 10 },
            };
            Grid.SetColumn(badgeBorder, 2);
            grid.Children.Add(badgeBorder);
        }

        return new ListBoxItem { Content = grid, Padding = new Thickness(0) };
    }

    private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SidebarList.SelectedItem is ListBoxItem { Tag: SidebarItem item }) _vm.Selection = item;
    }

    // MARK: - Content routing

    private void RefreshContent()
    {
        DetailHost.Content = _vm.Selection.Section switch
        {
            SidebarSection.Dashboard => BuildDashboard(),
            SidebarSection.AllApplications => BuildPipelineBoard(ApplicationStatusExtensions.BoardColumns),
            SidebarSection.Status => BuildPipelineBoard([_vm.Selection.Status!.Value]),
            SidebarSection.Leads => BuildLeadsView(),
            SidebarSection.Review => BuildReviewView(),
            _ => new Grid(),
        };
    }

    private DashboardView BuildDashboard()
    {
        var vm = new DashboardViewModel(_vm.AppState.Context);
        vm.Reload();
        return new DashboardView(vm);
    }

    private PipelineBoardView BuildPipelineBoard(ApplicationStatus[] columns) => new(_vm, columns);

    private LeadsView BuildLeadsView() => new(new LeadsViewModel(_vm.AppState.Context));

    private ReviewView BuildReviewView()
    {
        var vm = new ReviewViewModel(_vm.AppState.Context);
        vm.RequestOpen += app => _vm.SelectedApplication = app;
        return new ReviewView(vm);
    }

    // MARK: - Toolbar actions

    private void AddManually_Click(object sender, RoutedEventArgs e)
    {
        var vm = new NewApplicationViewModel(_vm.AppState.Context);
        var window = new NewApplicationWindow(vm) { Owner = Window.GetWindow(this) };
        vm.Saved += () => { window.Close(); _vm.Reload(); };
        window.ShowDialog();
    }

    private void ActivityLog_Click(object sender, RoutedEventArgs e)
    {
        var window = new ActivityLogWindow { Owner = Window.GetWindow(this) };
        window.Show();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Mail archives (*.mbox)|*.mbox|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true) _vm.AppState.Pipeline.ImportMbox(dialog.FileName);
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e) => await _vm.SyncNowCommand.ExecuteAsync(null);

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(new SettingsViewModel(_vm.AppState)) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _vm.Filters.SearchText = SearchBox.Text;

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = FilterMenuBuilder.Build(_vm);
        FilterButton.ContextMenu = menu;
        menu.PlacementTarget = FilterButton;
        menu.IsOpen = true;
    }

    private void UpdateToolbarSyncStatus()
    {
        var running = _vm.AppState.Pipeline.IsRunning;
        SyncProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        SyncStatusText.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        SyncStatusText.Text = running ? _vm.AppState.Pipeline.Stage.ShortDescription() : "";
        SyncNowButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        ImportButton.IsEnabled = !running;

        FilterButton.Content = _vm.Filters.HasActiveFilters
            ? $"▾ Filters ({_vm.Filters.ActiveFilterCount})"
            : "▾ Filters";
    }

    // MARK: - Detail sheet (Application Detail window)

    private void HandleSelectedApplicationChanged()
    {
        if (_vm.SelectedApplication is { } app)
        {
            var vm = new ApplicationDetailViewModel(_vm.AppState.Context, app);
            vm.RequestClose += () =>
            {
                _detailWindow?.Close();
                _detailWindow = null;
                _vm.SelectedApplication = null;
                _vm.Reload();
            };
            _detailWindow = new ApplicationDetailWindow(vm) { Owner = Window.GetWindow(this) };
            _detailWindow.Closed += (_, _) =>
            {
                _detailWindow = null;
                _vm.SelectedApplication = null;
                _vm.Reload();
            };
            _detailWindow.Show();
        }
        else
        {
            _detailWindow?.Close();
            _detailWindow = null;
        }
    }

    // MARK: - Import scope sheet

    private void HandlePendingImport()
    {
        var pending = _vm.AppState.Pipeline.PendingImport;
        if (pending is not null && _importScopeWindow is null)
        {
            _importScopeWindow = new ImportScopeWindow(pending, _vm.AppState.Pipeline) { Owner = Window.GetWindow(this) };
            // ImportScopeWindow itself discards the pending import on close
            // if the user didn't confirm a scope — just track the reference here.
            _importScopeWindow.Closed += (_, _) => _importScopeWindow = null;
            _importScopeWindow.Show();
        }
        else if (pending is null && _importScopeWindow is not null)
        {
            _importScopeWindow.Close();
            _importScopeWindow = null;
        }
    }

    // MARK: - Sync overlay

    private void UpdateOverlay()
    {
        if (_vm.OverlayVisible)
        {
            OverlayHost.Child = new SyncOverlayView(_vm.AppState.Pipeline);
            OverlayHost.Visibility = Visibility.Visible;
        }
        else
        {
            OverlayHost.Visibility = Visibility.Collapsed;
            OverlayHost.Child = null;
        }
    }
}
