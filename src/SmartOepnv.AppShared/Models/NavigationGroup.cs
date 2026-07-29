using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MaterialDesignThemes.Wpf;

namespace SmartOepnv.AppShared.Models;

/// <summary>Gruppierter Navigationspunkt (Hover öffnet Unterpunkte).</summary>
public sealed class NavigationGroup : ObservableObject
{
    public required string Title { get; init; }
    public required PackIconKind Icon { get; init; }
    public required IReadOnlyList<NavigationItem> Children { get; init; }

    private bool _isHoverOpen;
    private bool _keepOpenBecauseSelected;
    private bool _childrenAttached;

    public bool IsExpanded => _isHoverOpen || _keepOpenBecauseSelected;

    public string BadgeText
    {
        get
        {
            foreach (var child in Children)
            {
                if (!string.IsNullOrEmpty(child.BadgeText))
                {
                    return child.BadgeText;
                }
            }

            return string.Empty;
        }
    }

    public static NavigationGroup Create(string title, PackIconKind icon, params NavigationItem[] children)
    {
        var group = new NavigationGroup
        {
            Title = title,
            Icon = icon,
            Children = children
        };
        group.AttachChildren();
        return group;
    }

    private void AttachChildren()
    {
        if (_childrenAttached)
        {
            return;
        }

        _childrenAttached = true;
        foreach (var child in Children)
        {
            child.PropertyChanged += OnChildPropertyChanged;
        }
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationItem.BadgeText))
        {
            OnPropertyChanged(nameof(BadgeText));
        }
    }

    public void SetHoverOpen(bool open)
    {
        if (_isHoverOpen == open)
        {
            return;
        }

        _isHoverOpen = open;
        OnPropertyChanged(nameof(IsExpanded));
    }

    public void SyncSelection(NavigationItem? selected)
    {
        var keep = selected is not null &&
                   Children.Any(c => ReferenceEquals(c, selected));
        if (_keepOpenBecauseSelected == keep)
        {
            return;
        }

        _keepOpenBecauseSelected = keep;
        OnPropertyChanged(nameof(IsExpanded));
    }
}
