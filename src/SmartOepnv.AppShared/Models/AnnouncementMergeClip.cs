namespace SmartOepnv.AppShared.Models;

public sealed class AnnouncementMergeClip
{
    public required string DisplayName { get; init; }

    public required string SourcePath { get; init; }

    public bool IsGong { get; init; }
}
