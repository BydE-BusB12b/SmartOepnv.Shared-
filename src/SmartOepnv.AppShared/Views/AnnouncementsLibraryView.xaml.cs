using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public partial class AnnouncementsLibraryView : UserControl
{
    public AnnouncementsLibraryView()
    {
        InitializeComponent();
    }

    private void CycleAudioOutput_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AnnouncementsLibraryViewModel vm)
        {
            return;
        }

        var selected = vm.SelectedAnnouncement;
        if (selected is null)
        {
            return;
        }

        selected.CycleAudioOutput();
        vm.MarkDirtyFromUi();
        vm.StatusMessage = $"Lautsprecher: {selected.AudioOutputLabel}";
    }
}
