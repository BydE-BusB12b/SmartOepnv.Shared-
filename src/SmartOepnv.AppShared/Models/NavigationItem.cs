using System.Windows;
using MaterialDesignThemes.Wpf;

namespace SmartOepnv.AppShared.Models;

public sealed class NavigationItem
{
    public required string Title { get; init; }
    public required PackIconKind Icon { get; init; }
    public required FrameworkElement Content { get; init; }
    public string? Description { get; init; }
}
