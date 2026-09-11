using System.Windows;
using JobTracker.Models;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public partial class AddLeadWindow : Window
{
    private readonly LeadsViewModel _vm;

    public AddLeadWindow(LeadsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        TypeChanged(this, new RoutedEventArgs());
    }

    private void TypeChanged(object sender, RoutedEventArgs e)
    {
        var isPerson = PersonRadio.IsChecked == true;
        PersonNamePanel.Visibility = isPerson ? Visibility.Visible : Visibility.Collapsed;
        TitleLabel.Text = isPerson ? "Role / Context" : "Role Title";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var type = PersonRadio.IsChecked == true ? LeadType.Person : LeadType.JobPosting;
        var isValid = type == LeadType.Person
            ? PersonNameBox.Text.Trim().Length > 0
            : TitleBox.Text.Trim().Length > 0 || CompanyBox.Text.Trim().Length > 0 || UrlBox.Text.Trim().Length > 0;
        if (!isValid) return;

        _vm.Add(type, TitleBox.Text, CompanyBox.Text, UrlBox.Text, PersonNameBox.Text, NotesBox.Text);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
