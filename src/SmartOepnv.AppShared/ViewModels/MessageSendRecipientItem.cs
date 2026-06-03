using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartOepnv.AppShared.ViewModels;

public partial class MessageSendRecipientItem : ObservableObject
{
    public string Name { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Name) ? PhoneNumber : $"{Name} ({PhoneNumber})";

    [ObservableProperty] private bool isSelected;
}
