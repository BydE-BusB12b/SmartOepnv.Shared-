using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class AnnouncementsLibraryViewModel
{
    private HashSet<string> _suppressedStopAnnouncementKeys = new(StringComparer.OrdinalIgnoreCase);

    private void ReloadSuppressedStopAnnouncementKeys()
    {
        _suppressedStopAnnouncementKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!AppServices.IsPlannerApp || AppServices.PlanerAppSettings is null)
        {
            return;
        }

        foreach (var raw in AppServices.PlanerAppSettings.Load().SuppressedStopAnnouncementSoundKeys)
        {
            AddSuppressionKeys(_suppressedStopAnnouncementKeys, raw);
        }
    }

    private void SuppressStopAnnouncementSound(string? embeddedFileName, string? displayName)
    {
        if (!AppServices.IsPlannerApp || AppServices.PlanerAppSettings is null)
        {
            return;
        }

        var settings = AppServices.PlanerAppSettings.Load();
        var keys = new HashSet<string>(settings.SuppressedStopAnnouncementSoundKeys, StringComparer.OrdinalIgnoreCase);
        AddSuppressionKeys(keys, embeddedFileName);
        AddSuppressionKeys(keys, displayName);
        settings.SuppressedStopAnnouncementSoundKeys = keys.ToList();
        AppServices.PlanerAppSettings.Save(settings);
        ReloadSuppressedStopAnnouncementKeys();
    }

    private static void AddSuppressionKeys(ISet<string> keys, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var trimmed = raw.Trim();
        keys.Add(trimmed);
        var matchKey = NormalizeSoundMatchKey(trimmed);
        if (!string.IsNullOrEmpty(matchKey))
        {
            keys.Add(matchKey);
        }
    }

    private bool IsSuppressedStopAnnouncementSound(string fileName)
    {
        if (_suppressedStopAnnouncementKeys.Count == 0)
        {
            return false;
        }

        if (_suppressedStopAnnouncementKeys.Contains(fileName))
        {
            return true;
        }

        var matchKey = NormalizeSoundMatchKey(fileName);
        return !string.IsNullOrEmpty(matchKey) && _suppressedStopAnnouncementKeys.Contains(matchKey);
    }

    private void DetachStopSoundsForRemovedAnnouncement(ManagedAnnouncementTemplateItem ann)
    {
        var fileName = ann.EmbeddedSoundFileName?.Trim();
        var matchKey = string.IsNullOrEmpty(fileName)
            ? NormalizeSoundMatchKey(ann.DisplayName)
            : NormalizeSoundMatchKey(fileName);

        foreach (var stop in _allStops)
        {
            var stopFile = stop.EmbeddedSoundFileName?.Trim();
            if (string.IsNullOrEmpty(stopFile))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(fileName) &&
                string.Equals(stopFile, fileName, StringComparison.OrdinalIgnoreCase))
            {
                stop.EmbeddedSoundFileName = string.Empty;
                continue;
            }

            if (!string.IsNullOrEmpty(matchKey) &&
                string.Equals(NormalizeSoundMatchKey(stopFile), matchKey, StringComparison.Ordinal))
            {
                stop.EmbeddedSoundFileName = string.Empty;
            }
        }
    }
}
