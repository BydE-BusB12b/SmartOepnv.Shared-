using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class AnnouncementsLibraryViewModel
{
    private static readonly Regex ZusammenSuffixRegex = new(
        @"[\s_\-]*zusammen$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex NonLetterDigitRegex = new(
        @"[^0-9a-zäöüß]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LeadingAnnouncementCodeRegex = new(
        @"^\d{4}(?:[\s_\-]+|$)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RepeatedLetterRegex = new(
        @"(.)\1+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HbfTokenRegex = new(
        @"\bhbf\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HauptbahnhofTokenRegex = new(
        @"\bhauptbahnhof\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // „Bf Mitte“ ↔ „Bahnhof Mitte“, „Bhf“ ↔ „Bahnhof“.
    private static readonly Regex BfTokenRegex = new(
        @"\bbf\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BhfTokenRegex = new(
        @"\bbhf\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BahnhofTokenRegex = new(
        @"\bbahnhof\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [RelayCommand]
    private void SoundFileUpdate()
    {
        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var owner = DialogOwnerHelper.ResolveOwner();
        var dialog = CreateAudioOpenFileDialog();
        dialog.Title = "Sounddateien zum Ersetzen wählen";
        dialog.Multiselect = true;
        DialogOwnerHelper.PrepareForModalDialog(owner);
        if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
        {
            DialogOwnerHelper.RestoreAfterModalDialog(owner);
            return;
        }

        var existingByName = CollectExistingSoundFileNames();
        var existingByMatchKey = BuildExistingByMatchKey(existingByName.Keys, _allAnnouncements);
        var claimedExisting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<SoundFileUpdateDialog.Row>();

        foreach (var path in dialog.FileNames.OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path) || !EmbeddedSoundCatalog.IsAudioFile(path))
            {
                continue;
            }

            var newFileName = Path.GetFileName(path).Trim();
            if (string.IsNullOrWhiteSpace(newFileName))
            {
                continue;
            }

            var targetName = TryMatchExistingSoundFile(newFileName, existingByName, existingByMatchKey, claimedExisting);
            if (targetName is not null)
            {
                claimedExisting.Add(targetName);
            }

            rows.Add(new SoundFileUpdateDialog.Row
            {
                TargetFileName = targetName ?? newFileName,
                ExistingLabel = targetName ?? "(nicht vorhanden)",
                NewLabel = newFileName,
                NewPath = path,
                CanReplace = targetName is not null
            });
        }

        if (rows.Count == 0)
        {
            StatusMessage = "Keine gültigen Audiodateien ausgewählt.";
            return;
        }

        var preview = new SoundFileUpdateDialog(rows);
        if (DialogOwnerHelper.ShowOwnedDialog(preview, owner) != true)
        {
            return;
        }

        ReplaceSoundFiles(preview.ReplaceableRows);
    }

    private static string? TryMatchExistingSoundFile(
        string newFileName,
        IReadOnlyDictionary<string, string?> existingByName,
        IReadOnlyDictionary<string, List<string>> existingByMatchKey,
        ISet<string> claimedExisting)
    {
        if (existingByName.ContainsKey(newFileName) && !claimedExisting.Contains(newFileName))
        {
            return existingByName.Keys.First(k =>
                string.Equals(k, newFileName, StringComparison.OrdinalIgnoreCase));
        }

        List<string>? bestCandidates = null;
        foreach (var key in EnumerateSoundMatchKeys(newFileName))
        {
            if (!existingByMatchKey.TryGetValue(key, out var candidates))
            {
                continue;
            }

            var free = candidates.Where(c => !claimedExisting.Contains(c)).ToList();
            if (free.Count == 0)
            {
                continue;
            }

            bestCandidates = bestCandidates is null
                ? free
                : bestCandidates.Concat(free).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (bestCandidates is null || bestCandidates.Count == 0)
        {
            return null;
        }

        // Bei mehreren Treffern: exakte Basis ohne Extension bevorzugen, sonst kürzesten Namen.
        var newBase = Path.GetFileNameWithoutExtension(newFileName);
        return bestCandidates
            .OrderByDescending(c =>
                string.Equals(Path.GetFileNameWithoutExtension(c), newBase, StringComparison.OrdinalIgnoreCase))
            .ThenBy(c => c.Length)
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static Dictionary<string, List<string>> BuildExistingByMatchKey(
        IEnumerable<string> existingNames,
        IEnumerable<ManagedAnnouncementTemplateItem> announcements)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        void Register(string targetName, string? keySource)
        {
            foreach (var key in EnumerateSoundMatchKeys(keySource))
            {
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (!map.TryGetValue(key, out var list))
                {
                    list = [];
                    map[key] = list;
                }

                if (!list.Any(x => string.Equals(x, targetName, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(targetName);
                }
            }
        }

        foreach (var name in existingNames)
        {
            Register(name, name);
        }

        foreach (var ann in announcements)
        {
            var target = ann.EmbeddedSoundFileName?.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            Register(target, target);
            Register(target, ann.DisplayName);
            if (!string.IsNullOrWhiteSpace(ann.Description))
            {
                Register(target, ann.Description);
            }
        }

        return map;
    }

    /// <summary>
    /// Gleicher Schlüssel für z. B. „0096_Düsseldorf_Hbf_zusammen.wav“, Kartei „Düsseldorf Hbf“
    /// und neue Datei „Düsseldorf Hauptbahnhof.wav“.
    /// </summary>
    internal static string NormalizeSoundMatchKey(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return string.Empty;
        }

        baseName = ZusammenSuffixRegex.Replace(baseName, string.Empty);
        baseName = baseName.Trim().ToLowerInvariant();
        baseName = NonLetterDigitRegex.Replace(baseName, " ");
        return string.Join(' ', baseName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    internal static IEnumerable<string> EnumerateSoundMatchKeys(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            yield break;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                keys.Add(key);
            }
        }

        var primary = NormalizeSoundMatchKey(input);
        Add(primary);

        if (string.IsNullOrEmpty(primary))
        {
            yield break;
        }

        var withoutCode = LeadingAnnouncementCodeRegex.Replace(primary, string.Empty).Trim();
        Add(withoutCode);

        foreach (var key in new[] { primary, withoutCode })
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            Add(CollapseRepeatedLetters(key));
            foreach (var synonym in ExpandSoundMatchSynonyms(key))
            {
                Add(synonym);
                Add(CollapseRepeatedLetters(synonym));
            }
        }

        foreach (var key in keys)
        {
            yield return key;
        }
    }

    private static IEnumerable<string> ExpandSoundMatchSynonyms(string key)
    {
        if (HbfTokenRegex.IsMatch(key))
        {
            yield return HbfTokenRegex.Replace(key, "hauptbahnhof");
        }

        if (HauptbahnhofTokenRegex.IsMatch(key))
        {
            yield return HauptbahnhofTokenRegex.Replace(key, "hbf");
        }

        // Bf / Bhf / Bahnhof (z. B. „Bf Mitte“ ↔ „Bahnhof Mitte“)
        if (BfTokenRegex.IsMatch(key))
        {
            yield return BfTokenRegex.Replace(key, "bahnhof");
            yield return BfTokenRegex.Replace(key, "bhf");
        }

        if (BhfTokenRegex.IsMatch(key))
        {
            yield return BhfTokenRegex.Replace(key, "bahnhof");
            yield return BhfTokenRegex.Replace(key, "bf");
        }

        if (BahnhofTokenRegex.IsMatch(key))
        {
            yield return BahnhofTokenRegex.Replace(key, "bf");
            yield return BahnhofTokenRegex.Replace(key, "bhf");
        }
    }

    private static string CollapseRepeatedLetters(string key) =>
        RepeatedLetterRegex.Replace(key, "$1");

    private Dictionary<string, string?> CollectExistingSoundFileNames()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        void Add(string? name, string? path = null)
        {
            var n = name?.Trim();
            if (string.IsNullOrWhiteSpace(n))
            {
                return;
            }

            if (!map.ContainsKey(n))
            {
                map[n] = path;
            }
            else if (string.IsNullOrWhiteSpace(map[n]) && !string.IsNullOrWhiteSpace(path))
            {
                map[n] = path;
            }
        }

        if (AppServices.IsInitialized)
        {
            var embeddedDir = PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(AppServices.Workspace);
            if (Directory.Exists(embeddedDir))
            {
                foreach (var path in Directory.EnumerateFiles(embeddedDir))
                {
                    if (EmbeddedSoundCatalog.IsAudioFile(path))
                    {
                        Add(Path.GetFileName(path), path);
                    }
                }
            }

            var rawDir = PlanerAnnouncementRawSoundsWorkspace.GetRawSoundsDirectory(AppServices.Workspace);
            if (Directory.Exists(rawDir))
            {
                foreach (var path in Directory.EnumerateFiles(rawDir))
                {
                    if (EmbeddedSoundCatalog.IsAudioFile(path))
                    {
                        Add(Path.GetFileName(path), path);
                    }
                }
            }
        }

        var editor = AppServices.Routes.Editor;
        if (editor is not null)
        {
            foreach (var name in GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(editor.PackageRoot).Keys)
            {
                Add(name);
            }
        }

        foreach (var ann in _allAnnouncements)
        {
            Add(ann.EmbeddedSoundFileName, ann.LocalAudioPath);
            foreach (var entry in ann.AnnouncementSequence)
            {
                if (entry.Kind == AnnouncementExportEntryKind.Audio)
                {
                    Add(entry.FileName);
                }
            }
        }

        return map;
    }

    private void ReplaceSoundFiles(IReadOnlyList<SoundFileUpdateDialog.Row> rows)
    {
        if (rows.Count == 0)
        {
            StatusMessage = "Keine ersetzbaren Dateien.";
            return;
        }

        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var editor = AppServices.Routes.Editor;
        var embeddedDir = PlanerEmbeddedSoundsWorkspace.GetSoundsDirectory(AppServices.Workspace);
        var rawDir = PlanerAnnouncementRawSoundsWorkspace.GetRawSoundsDirectory(AppServices.Workspace);
        Directory.CreateDirectory(embeddedDir);
        Directory.CreateDirectory(rawDir);

        AnnouncementPreviewPlayer.Stop();

        var replaced = 0;
        var errors = new List<string>();
        foreach (var row in rows)
        {
            try
            {
                if (!File.Exists(row.NewPath))
                {
                    errors.Add($"{row.TargetFileName}: Quelldatei fehlt");
                    continue;
                }

                // Inhalt der neuen Datei unter dem bestehenden Planer-Namen speichern
                // (Referenzen / Sequenz bleiben gültig).
                var embeddedTarget = Path.Combine(embeddedDir, row.TargetFileName);
                File.Copy(row.NewPath, embeddedTarget, overwrite: true);

                var rawTarget = Path.Combine(rawDir, row.TargetFileName);
                File.Copy(row.NewPath, rawTarget, overwrite: true);

                EmbeddedSoundsEditor.UpsertFromFile(editor.PackageRoot, row.TargetFileName, embeddedTarget);

                foreach (var ann in _allAnnouncements)
                {
                    if (string.Equals(ann.EmbeddedSoundFileName, row.TargetFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        ann.LocalAudioPath = embeddedTarget;
                    }
                }

                replaced++;
            }
            catch (Exception ex)
            {
                errors.Add($"{row.TargetFileName}: {ex.Message}");
            }
        }

        if (replaced > 0)
        {
            editor.InvalidateEmbeddedSoundsJsonCache();
            MarkDirty();
        }

        StatusMessage = errors.Count == 0
            ? $"{replaced} Sounddatei(en) ersetzt – „Speichern & JSON“ fürs Handy-Update."
            : $"{replaced} ersetzt, {errors.Count} Fehler: {string.Join("; ", errors.Take(3))}";
    }
}
