// Builds the toolbar's filter ContextMenu (status/company/cycle/source
// checklists + Clear Filters), mirroring MainShell's filterMenu in Swift.

using System.Windows.Controls;
using JobTracker.Models;
using JobTracker.ViewModels;

namespace JobTracker.Views;

public static class FilterMenuBuilder
{
    public static ContextMenu Build(MainShellViewModel vm)
    {
        var menu = new ContextMenu();

        var statusHeader = new MenuItem { Header = "Status", IsEnabled = false, FontWeight = System.Windows.FontWeights.SemiBold };
        menu.Items.Add(statusHeader);
        foreach (var status in ApplicationStatusExtensions.BoardColumns)
        {
            var item = new MenuItem
            {
                Header = status.DisplayName(),
                IsCheckable = true,
                IsChecked = vm.Filters.Statuses.Contains(status),
            };
            item.Click += (_, _) => vm.Filters.ToggleStatus(status);
            menu.Items.Add(item);
        }

        AddSeparatorIfAny(menu, vm.AvailableCompanies, "Company", vm.Filters.Companies, vm.Filters.ToggleCompany);
        AddSeparatorIfAny(menu, vm.AvailableCycles, "Cycle", vm.Filters.Cycles, vm.Filters.ToggleCycle);
        AddSeparatorIfAny(menu, vm.AvailableSources, "Source", vm.Filters.Sources, vm.Filters.ToggleSource);

        if (vm.Filters.HasActiveFilters)
        {
            menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Clear Filters" };
            clear.Click += (_, _) => vm.Filters.Clear();
            menu.Items.Add(clear);
        }

        return menu;
    }

    private static void AddSeparatorIfAny(ContextMenu menu, List<string> values, string header,
        HashSet<string> selected, Action<string> toggle)
    {
        if (values.Count == 0) return;
        menu.Items.Add(new Separator());
        var sub = new MenuItem { Header = header };
        foreach (var value in values)
        {
            var item = new MenuItem { Header = value, IsCheckable = true, IsChecked = selected.Contains(value) };
            item.Click += (_, _) => toggle(value);
            sub.Items.Add(item);
        }
        menu.Items.Add(sub);
    }
}
