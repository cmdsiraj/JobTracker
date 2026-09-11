using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JobTracker.Converters;
using JobTracker.Models;

namespace JobTracker.Views;

public partial class ApplicationCardView : UserControl
{
    public JobApplication Application { get; }
    public event Action<JobApplication>? CardClicked;

    private Point _dragStart;
    private bool _dragging;

    private static readonly RelativeTimeConverter RelativeTime = new();

    public ApplicationCardView(JobApplication app)
    {
        InitializeComponent();
        Application = app;

        CompanyText.Text = app.Company.Length == 0 ? "Unknown Company" : app.Company;
        RoleText.Text = app.RoleTitle;
        RoleText.Visibility = app.RoleTitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        LocationText.Text = app.Location ?? "";
        LocationText.Visibility = !string.IsNullOrEmpty(app.Location) ? Visibility.Visible : Visibility.Collapsed;
        OutreachBadge.Visibility = app.Status == ApplicationStatus.Outreach ? Visibility.Visible : Visibility.Collapsed;
        ReviewBadge.Visibility = app.NeedsReview ? Visibility.Visible : Visibility.Collapsed;

        if (app.Cycle.Length > 0)
        {
            CycleBadge.Visibility = Visibility.Visible;
            CycleText.Text = app.Cycle;
        }
        if (!string.IsNullOrEmpty(app.Source))
        {
            SourceBadge.Visibility = Visibility.Visible;
            SourceText.Text = app.Source;
        }
        UpdatedText.Text = (string)RelativeTime.Convert(app.LastUpdated, typeof(string), null, CultureInfo.CurrentCulture)!;
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragging = false;
    }

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragging) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }
        _dragging = true;
        var data = new DataObject("JobApplicationId", Application.Id);
        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        _dragging = false;
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging) return;
        CardClicked?.Invoke(Application);
    }
}
