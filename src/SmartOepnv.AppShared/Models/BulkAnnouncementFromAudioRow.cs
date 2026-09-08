using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Models;

/// <summary>Zeile im Dialog „Mehrere Ansagen mit Tondatei“.</summary>
public sealed class BulkAnnouncementFromAudioRow : INotifyPropertyChanged
{
    private string _displayName;
    private bool _includeGong;
    private bool _includeSondergong;
    private bool _includeNextStopGerman;
    private bool _includeNextStopMp3;
    private bool _includeFollowingStops;
    private bool _alreadyExists;
    private string? _existingMatchLabel;
    private readonly Action<BulkAnnouncementFromAudioRow>? _refreshDuplicateCheck;

    public BulkAnnouncementFromAudioRow(
        string announcementCode,
        string sourcePath,
        string displayName,
        Action<BulkAnnouncementFromAudioRow>? refreshDuplicateCheck = null)
    {
        AnnouncementCode = announcementCode;
        SourcePath = sourcePath;
        SoundFileName = Path.GetFileName(sourcePath);
        _displayName = displayName;
        _refreshDuplicateCheck = refreshDuplicateCheck;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string AnnouncementCode { get; }

    public string AnnouncementCodeDisplay =>
        ManagedAnnouncementTemplateItem.NormalizeCode(AnnouncementCode);

    public string SourcePath { get; }

    public string SoundFileName { get; }

    public bool AlreadyExists
    {
        get => _alreadyExists;
        private set
        {
            if (_alreadyExists == value)
            {
                return;
            }

            _alreadyExists = value;
            Notify(nameof(AlreadyExists));
        }
    }

    public string? ExistingMatchLabel
    {
        get => _existingMatchLabel;
        private set
        {
            if (string.Equals(_existingMatchLabel, value, StringComparison.Ordinal))
            {
                return;
            }

            _existingMatchLabel = value;
            Notify(nameof(ExistingMatchLabel));
        }
    }

    public void SetExistingMatch(bool exists, string? label)
    {
        AlreadyExists = exists;
        ExistingMatchLabel = label;
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            var trimmed = value?.Trim() ?? string.Empty;
            if (_displayName == trimmed)
            {
                return;
            }

            _displayName = trimmed;
            Notify(nameof(DisplayName));
            Notify(nameof(SaveFileName));
            _refreshDuplicateCheck?.Invoke(this);
        }
    }

    public string SaveFileName =>
        ManagedAnnouncementTemplateItem.DefaultEmbeddedFileName(AnnouncementCode, DisplayName);

    public bool IncludeGong
    {
        get => _includeGong;
        set => SetFlag(ref _includeGong, value, nameof(IncludeGong));
    }

    public bool IncludeSondergong
    {
        get => _includeSondergong;
        set => SetFlag(ref _includeSondergong, value, nameof(IncludeSondergong));
    }

    public bool IncludeNextStopGerman
    {
        get => _includeNextStopGerman;
        set => SetFlag(ref _includeNextStopGerman, value, nameof(IncludeNextStopGerman));
    }

    public bool IncludeNextStopMp3
    {
        get => _includeNextStopMp3;
        set => SetFlag(ref _includeNextStopMp3, value, nameof(IncludeNextStopMp3));
    }

    public bool IncludeFollowingStops
    {
        get => _includeFollowingStops;
        set => SetFlag(ref _includeFollowingStops, value, nameof(IncludeFollowingStops));
    }

    private void SetFlag(ref bool field, bool value, string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        Notify(propertyName);
    }

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
