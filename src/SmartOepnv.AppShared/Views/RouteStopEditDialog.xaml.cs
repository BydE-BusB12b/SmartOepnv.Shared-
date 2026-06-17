using System.Windows;
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
        _viewModel.StopDetailEditedCommand.Execute(null);
        _viewModel.SaveChangesCommand.Execute(null);
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _stop.CopyFrom(_snapshot);
        _viewModel.NotifyStopEditorStateChanged();
        DialogResult = false;
        Close();
    }
}
