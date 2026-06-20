using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public partial class RouteStopEditDialog : Window
{
    private readonly RoutesViewModel _viewModel;
    private readonly RouteStopItem _stop;
    private readonly RouteStopItem _snapshot;

    public RouteStopEditDialog(RoutesViewModel viewModel, RouteStopItem stop)
    {
        _viewModel = viewModel;
        _stop = stop;
        _snapshot = stop.Clone();

        viewModel.PrepareStopEditDialog(stop);

        InitializeComponent();
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        var displayName = string.IsNullOrWhiteSpace(stop.Name) ? "Haltestelle" : stop.Name.Trim();
        TitleText.Text = displayName;
        Title = $"Haltestelle bearbeiten – {displayName}";

        EditPanel.DataContext = viewModel;
        viewModel.NotifyStopEditorStateChanged();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        FlushPendingFieldBindings(EditPanel);
        _viewModel.StopDetailEditedCommand.Execute(null);
        _viewModel.SaveChangesCommand.Execute(null);
        DialogResult = true;
        Close();
    }

    private static void FlushPendingFieldBindings(DependencyObject root)
    {
        foreach (var textBox in EnumerateVisualChildren<TextBox>(root))
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    private static IEnumerable<T> EnumerateVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in EnumerateVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _stop.CopyFrom(_snapshot);
        _viewModel.NotifyStopEditorStateChanged();
        DialogResult = false;
        Close();
    }
}
