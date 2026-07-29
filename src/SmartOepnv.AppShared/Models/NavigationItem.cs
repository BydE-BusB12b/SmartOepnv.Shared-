using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using MaterialDesignThemes.Wpf;

namespace SmartOepnv.AppShared.Models;

public sealed class NavigationItem : INotifyPropertyChanged
{
    public required string Title { get; init; }
    public required PackIconKind Icon { get; init; }
    public required Func<FrameworkElement> CreateContent { get; init; }
    public string? Description { get; init; }

    private string _badgeText = string.Empty;
    private bool _isSelected;

    public string BadgeText
    {
        get => _badgeText;
        set
        {
            if (_badgeText == value)
            {
                return;
            }

            _badgeText = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    private FrameworkElement? _content;

    public FrameworkElement Content => _content ??= CreateContent();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
