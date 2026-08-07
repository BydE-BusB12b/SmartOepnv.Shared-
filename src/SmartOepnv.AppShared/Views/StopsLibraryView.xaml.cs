using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartOepnv.AppShared.Views;

public partial class StopsLibraryView : UserControl
{
    public StopsLibraryView()
    {
        InitializeComponent();
    }

    private void RouteAssignSearchBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        box.TextChanged -= RouteAssignSearchBox_OnTextChanged;
        box.TextChanged += RouteAssignSearchBox_OnTextChanged;
    }

    private void RouteAssignSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        // Nach Filter-Refresh Fokus zurück ins Suchfeld (sonst muss man jeden Buchstaben neu anklicken).
        box.Dispatcher.BeginInvoke(() =>
        {
            if (box.IsKeyboardFocusWithin)
            {
                return;
            }

            var combo = FindVisualParent<ComboBox>(box);
            if (combo is null || !combo.IsDropDownOpen)
            {
                return;
            }

            Keyboard.Focus(box);
            box.CaretIndex = box.Text?.Length ?? 0;
        }, DispatcherPriority.Input);
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
