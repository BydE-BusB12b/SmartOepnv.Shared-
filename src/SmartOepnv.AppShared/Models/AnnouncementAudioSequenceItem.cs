using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartOepnv.AppShared.Models;

public enum AnnouncementSequenceEntryKind
{
    Audio,
    Pause
}

public partial class AnnouncementAudioSequenceItem : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public AnnouncementSequenceEntryKind Kind { get; init; }

    [ObservableProperty] private string displayName = string.Empty;

    [ObservableProperty] private string? sourcePath;

    [ObservableProperty] private double pauseSeconds = 0.5;

    public string ListLabel => Kind switch
    {
        AnnouncementSequenceEntryKind.Pause => $"Pause {PauseSeconds:0.###} s",
        _ => string.IsNullOrWhiteSpace(DisplayName) ? "Tondatei" : DisplayName
    };

    partial void OnPauseSecondsChanged(double value) => OnPropertyChanged(nameof(ListLabel));

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(ListLabel));

    public AnnouncementAudioSequenceItem Clone() => new()
    {
        Kind = Kind,
        DisplayName = DisplayName,
        SourcePath = SourcePath,
        PauseSeconds = PauseSeconds
    };
}
