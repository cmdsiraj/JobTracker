using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using JobTracker.Models;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class MergeTargetWindow : Window
{
    private readonly ApplicationDetailViewModel _vm;
    private readonly List<JobApplication> _allOthers;

    public MergeTargetWindow(ApplicationDetailViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DescriptionText.Text =
            $"Moves every email, note, and thread from “{vm.Application.Company}” into the application you pick, then deletes this one.";
        _allOthers = [.. vm.ContextApplications().Where(a => a.Id != vm.Application.Id)];
        Render(_allOthers);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text;
        var filtered = text.Length == 0
            ? _allOthers
            : _allOthers.Where(a =>
                a.Company.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                a.RoleTitle.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        Render(filtered);
    }

    private void Render(List<JobApplication> candidates)
    {
        CandidatesPanel.Children.Clear();
        EmptyText.Visibility = candidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var candidate in candidates)
        {
            CandidatesPanel.Children.Add(BuildRow(candidate));
        }
    }

    private Border BuildRow(JobApplication target)
    {
        var grid = new Grid { Margin = new Thickness(4, 8, 4, 8) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = target.Company, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = target.RoleTitle.Length == 0 ? target.Status.DisplayName() : target.RoleTitle,
            FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"),
        });
        grid.Children.Add(stack);

        var border = new Border { Cursor = Cursors.Hand, Child = grid };
        border.MouseLeftButtonUp += (_, _) =>
        {
            _vm.MergeInto(target);
            Close();
        };
        return border;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
