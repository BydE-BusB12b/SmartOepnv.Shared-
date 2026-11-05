using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class StopsLibraryView : UserControl
{
    public StopsLibraryView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookRouteAssignSearch();
        Loaded += (_, _) => HookRouteAssignSearch();
    }

    private StopsLibraryViewModel? _routeAssignVm;
    private TextBox? _routeAssignSearchBox;

    private void HookRouteAssignSearch()
    {
        if (_routeAssignVm is not null)
        {
            _routeAssignVm.RouteAssignSearchApplied -= OnRouteAssignSearchApplied;
        }

        _routeAssignVm = DataContext as StopsLibraryViewModel;
        if (_routeAssignVm is not null)
        {
            _routeAssignVm.RouteAssignSearchApplied += OnRouteAssignSearchApplied;
        }
    }

    private void OnRouteAssignSearchApplied()
    {
        var box = _routeAssignSearchBox;
        if (box is null)
        {
            return;
        }

        var combo = FindVisualParent<ComboBox>(box);
        var length = box.Text?.Length ?? 0;
        box.Dispatcher.BeginInvoke(() =>
        {
            if (combo is null || !combo.IsDropDownOpen || !box.IsVisible)
            {
                return;
            }

            if (!box.IsKeyboardFocused)
            {
                Keyboard.Focus(box);
            }

            if (box.IsKeyboardFocused)
            {
                box.CaretIndex = length;
            }
        }, DispatcherPriority.ContextIdle);
    }

    private void RouteAssignSearchBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        _routeAssignSearchBox = box;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
            {
                return match;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }
}
