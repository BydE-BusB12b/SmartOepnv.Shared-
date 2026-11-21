using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartOepnv.AppShared.Models;

public enum AnnouncementSequenceEntryKind
{
    Audio,
    Pause,
    Line,
    Nach,
    RouteEndDestination
}

public partial class AnnouncementAudioSequenceItem : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public AnnouncementSequenceEntryKind Kind { get; init; }

    [ObservableProperty] private string displayName = string.Empty;

    [ObservableProperty] private string? sourcePath;

    [ObservableProperty] private double pauseSeconds = 0.5;

    [ObservableProperty] private string sequenceNote = string.Empty;

    public string ListLabel =>
        (Kind switch
        {
            AnnouncementSequenceEntryKind.Pause => $"Pause {PauseSeconds:0.###} s",
            AnnouncementSequenceEntryKind.Line => "Linie (aus Route)",
            AnnouncementSequenceEntryKind.Nach => "Nach",
            AnnouncementSequenceEntryKind.RouteEndDestination => "Endhaltestelle (Ziel)",
            _ => string.IsNullOrWhiteSpace(DisplayName) ? "Tondatei" : DisplayName
        })
        + (string.IsNullOrWhiteSpace(SequenceNote) ? string.Empty : $"  ·  {SequenceNote}");

    partial void OnPauseSecondsChanged(double value) => OnPropertyChanged(nameof(ListLabel));

    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(ListLabel));

    partial void OnSequenceNoteChanged(string value) => OnPropertyChanged(nameof(ListLabel));

    public AnnouncementAudioSequenceItem Clone() => new()
    {
        Kind = Kind,
        DisplayName = DisplayName,
        SourcePath = SourcePath,
        PauseSeconds = PauseSeconds
    };
}
