using System.Windows;
using MaterialDesignThemes.Wpf;

namespace SmartOepnv.AppShared.Models;

public sealed class NavigationItem
{
    public required string Title { get; init; }
    public required PackIconKind Icon { get; init; }
    public required Func<FrameworkElement> CreateContent { get; init; }
    public string? Description { get; init; }

    public string BadgeText { get; set; } = string.Empty;

    private FrameworkElement? _content;

    public FrameworkElement Content => _content ??= CreateContent();
}
