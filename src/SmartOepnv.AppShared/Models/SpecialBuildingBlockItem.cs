using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartOepnv.AppShared.Models;

/// <summary>Eintrag in der Spezialbausteine-Bibliothek (und, Nach, über, Linien, …).</summary>
public partial class SpecialBuildingBlockItem : ObservableObject
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string FileName { get; init; }
    public required string Description { get; init; }
    public bool IsLineBlock { get; init; }
    public bool IsUserAdded { get; init; }

    [ObservableProperty] private bool isPresent;
    [ObservableProperty] private string statusLabel = "⚠ fehlt";
    [ObservableProperty] private string? resolvedPath;
    [ObservableProperty] private string voice2FileName = string.Empty;
    [ObservableProperty] private string voice2StatusLabel = string.Empty;
    [ObservableProperty] private string? voice2ResolvedPath;
    [ObservableProperty] private bool supportsVoice2;

    public string DisplayLabel =>
        IsPresent
            ? $"✓  {Title}  ·  {FileName}"
            : $"⚠  {Title}  ·  {FileName}";
}
