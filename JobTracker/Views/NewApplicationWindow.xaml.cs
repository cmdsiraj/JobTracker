using System.Windows;
using JobTracker.Models;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class NewApplicationWindow : Window
{
    private readonly NewApplicationViewModel _vm;

    public NewApplicationWindow(NewApplicationViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        StatusCombo.ItemsSource = Enum.GetValues<ApplicationStatus>().Select(s => s.DisplayName()).ToList();
        StatusCombo.SelectedIndex = 0;
        NewStatusCombo.ItemsSource = new[] { "Keep current status" }
            .Concat(Enum.GetValues<ApplicationStatus>().Select(s => s.DisplayName())).ToList();
        NewStatusCombo.SelectedIndex = 0;
        TargetAppCombo.ItemsSource = new[] { "Choose…" }
            .Concat(_vm.Applications.Select(a => $"{a.Company} — {a.RoleTitle}")).ToList();
        TargetAppCombo.SelectedIndex = 0;
        AppliedDatePicker.SelectedDate = DateTime.Now;

        ModeChanged(this, new RoutedEventArgs());
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        var isUpdate = UpdateRadio.IsChecked == true;
        NewApplicationPanel.Visibility = isUpdate ? Visibility.Collapsed : Visibility.Visible;
        UpdatePanel.Visibility = isUpdate ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Content = isUpdate ? "Log Update" : "Add Application";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (UpdateRadio.IsChecked == true)
        {
            _vm.Mode = NewApplicationMode.Update;
            _vm.TargetApp = TargetAppCombo.SelectedIndex > 0 ? _vm.Applications[TargetAppCombo.SelectedIndex - 1] : null;
            _vm.UpdateText = UpdateTextBox.Text;
            _vm.NewStatus = NewStatusCombo.SelectedIndex > 0
                ? Enum.GetValues<ApplicationStatus>()[NewStatusCombo.SelectedIndex - 1]
                : null;
        }
        else
        {
            _vm.Mode = NewApplicationMode.NewApplication;
            _vm.Company = CompanyBox.Text;
            _vm.Role = RoleBox.Text;
            _vm.Status = Enum.GetValues<ApplicationStatus>()[StatusCombo.SelectedIndex];
            _vm.Cycle = CycleBox.Text;
            _vm.Location = LocationBox.Text;
            _vm.Source = SourceBox.Text;
            _vm.AppliedDate = new DateTimeOffset(AppliedDatePicker.SelectedDate ?? DateTime.Now);
            _vm.Notes = NotesBox.Text;
        }

        if (!_vm.CanSave) return;
        _vm.Save();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
