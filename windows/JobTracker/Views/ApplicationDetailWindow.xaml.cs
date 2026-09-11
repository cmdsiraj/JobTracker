using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JobTracker.Models;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class ApplicationDetailWindow : Window
{
    private readonly ApplicationDetailViewModel _vm;
    private bool _loading = true;

    public ApplicationDetailWindow(ApplicationDetailViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _vm.RequestClose += Close;

        StatusCombo.ItemsSource = Enum.GetValues<ApplicationStatus>().Select(s => s.DisplayName()).ToList();

        LoadFields();
        RenderTimeline();
        _loading = false;
    }

    private void LoadFields()
    {
        var app = _vm.Application;
        Title = app.Company.Length > 0 ? app.Company : "Application";
        CompanyBox.Text = app.Company;
        RoleBox.Text = app.RoleTitle;
        StatusCombo.SelectedIndex = (int)app.Status;
        ReviewBanner.Visibility = app.NeedsReview ? Visibility.Visible : Visibility.Collapsed;
        LocationBox.Text = app.Location ?? "";
        SourceBox.Text = app.Source ?? "";
        CycleBox.Text = app.Cycle;
        NextActionBox.Text = app.NextAction;
        TagsBox.Text = _vm.TagsText;
        AppliedDateText.Text = app.AppliedDate?.ToString("MMM d, yyyy") ?? "—";
        ContactText.Text = string.IsNullOrEmpty(app.ContactEmail) ? "—" : app.ContactEmail;
        NotesBox.Text = app.Notes;
    }

    // MARK: - Field persistence

    private void CompanyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.Application.Company = CompanyBox.Text;
        _vm.OnFieldChanged();
        Title = _vm.Application.Company.Length > 0 ? _vm.Application.Company : "Application";
    }

    private void RoleBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.Application.RoleTitle = RoleBox.Text;
        _vm.OnFieldChanged();
    }

    private void LocationBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.Application.Location = LocationBox.Text.Length == 0 ? null : LocationBox.Text;
        _vm.OnFieldChanged();
    }

    private void SourceBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.Application.Source = SourceBox.Text.Length == 0 ? null : SourceBox.Text;
        _vm.OnFieldChanged();
    }

    private void CycleBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.Application.Cycle = CycleBox.Text;
        _vm.OnFieldChanged();
    }

    private void NextActionBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.Application.NextAction = NextActionBox.Text;
        _vm.OnFieldChanged();
    }

    private void TagsBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.TagsText = TagsBox.Text;
    }

    private void NotesBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _vm.Application.Notes = NotesBox.Text;
        _vm.OnFieldChanged();
    }

    private void StatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var newStatus = (ApplicationStatus)StatusCombo.SelectedIndex;
        _vm.ChangeStatus(newStatus);
        RenderTimeline();
    }

    private void MarkReviewed_Click(object sender, RoutedEventArgs e)
    {
        _vm.MarkReviewedCommand.Execute(null);
        ReviewBanner.Visibility = Visibility.Collapsed;
    }

    // MARK: - Menu / close

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var merge = new MenuItem { Header = "Merge Into…" };
        merge.Click += (_, _) =>
        {
            var window = new MergeTargetWindow(_vm) { Owner = this };
            window.ShowDialog();
        };
        var delete = new MenuItem { Header = "Delete Application", Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush") };
        delete.Click += (_, _) =>
        {
            if (System.Windows.MessageBox.Show(this, "Delete this application? This cannot be undone.", "Delete Application",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
            {
                _vm.DeleteCommand.Execute(null);
            }
        };
        menu.Items.Add(merge);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.PlacementTarget = MenuButton;
        menu.IsOpen = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // MARK: - Timeline

    private void AddNote_Click(object sender, RoutedEventArgs e) => AddNote();

    private void NewNoteBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddNote();
    }

    private void AddNote()
    {
        _vm.NewNote = NewNoteBox.Text;
        if (_vm.NewNote.Trim().Length == 0) return;
        _vm.AddNoteCommand.Execute(null);
        NewNoteBox.Text = "";
        RenderTimeline();
    }

    private void RenderTimeline()
    {
        TimelinePanel.Children.Clear();
        var events = _vm.SortedEvents.ToList();
        NoActivityText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var evt in events)
        {
            TimelinePanel.Children.Add(BuildTimelineRow(evt));
        }
    }

    private Border BuildTimelineRow(EmailEvent evt)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var (icon, color) = IconFor(evt);
        var iconBorder = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromArgb(35, color.R, color.G, color.B)),
            Child = new TextBlock { Text = icon, FontSize = 10, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(iconBorder, 0);

        var contentStack = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        var topRow = new DockPanel();
        var dateText = new TextBlock { Text = evt.ReceivedDate.ToString("MMM d, yyyy h:mm tt"), FontSize = 10, Foreground = (Brush)FindResource("TertiaryTextBrush") };
        DockPanel.SetDock(dateText, Dock.Right);
        topRow.Children.Add(dateText);

        switch (evt.Kind)
        {
            case EventKind.StatusChange:
                topRow.Children.Add(new TextBlock { Text = evt.Snippet, FontWeight = FontWeights.Medium, FontSize = 12 });
                contentStack.Children.Add(topRow);
                break;
            case EventKind.Note:
                topRow.Children.Add(new TextBlock { Text = "NOTE", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("SecondaryTextBrush") });
                contentStack.Children.Add(topRow);
                contentStack.Children.Add(new TextBlock { Text = evt.Snippet, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                break;
            default: // Email
                var senderStack = new StackPanel { Orientation = Orientation.Horizontal };
                var senderLine = evt.Direction == EventDirection.Outgoing
                    ? $"You → {(evt.Sender.Length == 0 ? "recipient" : evt.Sender)}"
                    : (evt.Sender.Length == 0 ? "Unknown sender" : evt.Sender);
                senderStack.Children.Add(new TextBlock { Text = senderLine, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = evt.Direction == EventDirection.Outgoing ? (Brush)FindResource("WarningBrush") : (Brush)FindResource("SecondaryTextBrush") });
                if (evt.DetectedStatus is { } detected)
                {
                    senderStack.Children.Add(new Border
                    {
                        Margin = new Thickness(6, 0, 0, 0), CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 0),
                        Background = new SolidColorBrush(Color.FromArgb(35, color.R, color.G, color.B)),
                        Child = new TextBlock { Text = detected.DisplayName(), FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color) },
                    });
                }
                if (evt.MatchConfidence < 0.9)
                {
                    senderStack.Children.Add(new TextBlock
                    {
                        Text = $" ❓{Math.Round(evt.MatchConfidence * 100)}%", FontSize = 9, Foreground = (Brush)FindResource("WarningBrush"),
                        ToolTip = $"Matched to this application with {Math.Round(evt.MatchConfidence * 100)}% confidence",
                    });
                }
                topRow.Children.Add(senderStack);
                contentStack.Children.Add(topRow);
                contentStack.Children.Add(new TextBlock { Text = evt.Subject, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
                contentStack.Children.Add(new TextBlock { Text = evt.Snippet, FontSize = 10, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
                if (evt.GmailUrl is { } url)
                {
                    var link = new TextBlock { Text = "Open in Gmail", FontSize = 10, Foreground = (Brush)FindResource("AccentBrush"), Cursor = Cursors.Hand, Margin = new Thickness(0, 3, 0, 0) };
                    link.MouseLeftButtonUp += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
                    contentStack.Children.Add(link);
                }
                break;
        }
        Grid.SetColumn(contentStack, 1);
        grid.Children.Add(iconBorder);
        grid.Children.Add(contentStack);

        var contextMenu = new ContextMenu();
        var detach = new MenuItem { Header = "Detach into New Application" };
        detach.Click += (_, _) => { _vm.Detach(evt); RenderTimeline(); };
        var delete = new MenuItem { Header = "Delete Event" };
        delete.Click += (_, _) => { _vm.DeleteEvent(evt); RenderTimeline(); };
        contextMenu.Items.Add(detach);
        contextMenu.Items.Add(delete);

        return new Border { ContextMenu = contextMenu, Child = grid };
    }

    private (string Icon, Color Color) IconFor(EmailEvent evt) => evt.Kind switch
    {
        EventKind.StatusChange => ("⇄", evt.DetectedStatus is { } s ? StatusColor(s) : Color.FromRgb(0x8B, 0x5C, 0xF6)),
        EventKind.Note => ("📝", Color.FromRgb(0x63, 0x66, 0xF1)),
        _ => (evt.Direction == EventDirection.Outgoing ? "↗" : "↙", evt.Direction == EventDirection.Outgoing ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x35, 0x7A, 0xBD)),
    };

    private static Color StatusColor(ApplicationStatus status)
    {
        var brush = (SolidColorBrush)new JobTracker.Converters.StatusToBrushConverter()
            .Convert(status, typeof(Brush), null, System.Globalization.CultureInfo.CurrentCulture)!;
        return brush.Color;
    }
}
