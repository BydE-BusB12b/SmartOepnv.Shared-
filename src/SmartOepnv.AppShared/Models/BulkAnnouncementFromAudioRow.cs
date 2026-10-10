using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Models;

/// <summary>Zeile im Dialog „Mehrere Ansagen mit Tondatei“.</summary>
public sealed class BulkAnnouncementFromAudioRow : INotifyPropertyChanged
{
    private readonly string _allocatedCode;
    private string _announcementCode;
    private string _displayName;
    private bool _includeGong;
    private bool _includeSondergong;
    private bool _includeNextStopGerman;
    private bool _includeNextStopMp3;
    private bool _includeFollowingStops;
    private bool _alreadyExists;
    private bool _updateExisting;
    private string? _existingMatchLabel;
    private string? _existingAnnouncementId;
    private string? _existingAnnouncementCode;
    private readonly Action<BulkAnnouncementFromAudioRow>? _refreshDuplicateCheck;

    public BulkAnnouncementFromAudioRow(
        string announcementCode,
        string sourcePath,
        string displayName,
        Action<BulkAnnouncementFromAudioRow>? refreshDuplicateCheck = null)
    {
        _allocatedCode = ManagedAnnouncementTemplateItem.NormalizeCode(announcementCode);
        _announcementCode = _allocatedCode;
        SourcePath = sourcePath;
        SoundFileName = Path.GetFileName(sourcePath);
        _displayName = displayName;
        _refreshDuplicateCheck = refreshDuplicateCheck;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Aktuelle ID (bei „aktualisieren“ = ID der vorhandenen Ansage).</summary>
    public string AnnouncementCode => _announcementCode;

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
            Notify(nameof(IsUnknownStop));
            Notify(nameof(CanUpdateExisting));
            if (!_alreadyExists && _updateExisting)
            {
                UpdateExisting = false;
            }
        }
    }

    /// <summary>Keine Treffer in Ansagen/Haltestellen → weiß, Klick auf Sounddatei für Vorschläge.</summary>
    public bool IsUnknownStop => !AlreadyExists;

    /// <summary>Vorhandene Ansage mit ID – Spalte „aktualisieren“ nutzbar.</summary>
    public bool CanUpdateExisting =>
        AlreadyExists && !string.IsNullOrWhiteSpace(_existingAnnouncementId);

    public bool UpdateExisting
    {
        get => _updateExisting;
        set
        {
            if (!CanUpdateExisting)
            {
                value = false;
            }

            if (_updateExisting == value)
            {
                return;
            }

            _updateExisting = value;
            ApplyAnnouncementCodeForUpdateState();
            Notify(nameof(UpdateExisting));
        }
    }

    public string? ExistingAnnouncementId => _existingAnnouncementId;

    public string? ExistingAnnouncementCode => _existingAnnouncementCode;

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

    public void SetExistingMatch(
        bool exists,
        string? label,
        string? existingAnnouncementId = null,
        string? existingAnnouncementCode = null)
    {
        _existingAnnouncementId = string.IsNullOrWhiteSpace(existingAnnouncementId)
            ? null
            : existingAnnouncementId.Trim();
        _existingAnnouncementCode = string.IsNullOrWhiteSpace(existingAnnouncementCode)
            ? null
            : ManagedAnnouncementTemplateItem.NormalizeCode(existingAnnouncementCode);
        AlreadyExists = exists;
        ExistingMatchLabel = label;
        Notify(nameof(CanUpdateExisting));
        Notify(nameof(ExistingAnnouncementId));
        Notify(nameof(ExistingAnnouncementCode));

        if (!CanUpdateExisting && _updateExisting)
        {
            UpdateExisting = false;
        }
        else if (_updateExisting)
        {
            ApplyAnnouncementCodeForUpdateState();
        }
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

    private void ApplyAnnouncementCodeForUpdateState()
    {
        var next = _updateExisting && !string.IsNullOrEmpty(_existingAnnouncementCode)
            ? _existingAnnouncementCode!
            : _allocatedCode;
        if (string.Equals(_announcementCode, next, StringComparison.Ordinal))
        {
            Notify(nameof(SaveFileName));
            return;
        }

        _announcementCode = next;
        Notify(nameof(AnnouncementCode));
        Notify(nameof(AnnouncementCodeDisplay));
        Notify(nameof(SaveFileName));
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
