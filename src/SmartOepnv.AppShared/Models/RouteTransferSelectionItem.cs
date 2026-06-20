using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartOepnv.AppShared.Models;

public sealed partial class RouteTransferSelectionItem : ObservableObject
{
    public RouteTransferSelectionItem(string routeName)
    {
        RouteName = routeName;
    }

    public string RouteName { get; }

    [ObservableProperty] private bool isSelected;
}
