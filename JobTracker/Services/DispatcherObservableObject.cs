// ObservableObject that marshals PropertyChanged to the WPF UI thread when
// raised from a background thread (mbox-parse progress callbacks, the tray
// watcher's poll loop). Falls back to a direct raise when there's no
// Application (unit tests, or a property set before the app starts).

using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace JobTracker.Services;

public abstract class DispatcherObservableObject : ObservableObject
{
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(DispatcherPriority.DataBind, () => base.OnPropertyChanged(e));
            return;
        }
        base.OnPropertyChanged(e);
    }
}
