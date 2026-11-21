using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Zweite Ansagestimme: gleiche Datei mit <c>_2</c> vor der Endung
/// (<c>und.wav</c> → <c>und_2.wav</c>). Petra bleibt der bisherige Name.
/// </summary>
public static class AnnouncementVoiceFiles
{
    public const string Voice2 = "2";
    public const string PetraLabel = "Linguatec Petra";
    public const string Voice2Label = "Stimme 2";

    public static bool IsVoice2(string? value) =>
        string.Equals(value?.Trim(), Voice2, StringComparison.Ordinal);

    public static string LabelFor(string? value) =>
        IsVoice2(value) ? Voice2Label : PetraLabel;

    public static string StorageValueForLabel(string? label) =>
        string.Equals(label?.Trim(), Voice2Label, StringComparison.Ordinal) ? Voice2 : string.Empty;

    /// <summary><c>Nach.wav</c> → <c>Nach_2.wav</c>. Ein schon gesetztes <c>_2</c> bleibt.</summary>
    public static string Voice2FileName(string fileName)
    {
        var name = fileName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return string.Empty;
        }

        var ext = Path.GetExtension(name);
        var stem = ext.Length == 0 ? name : Path.GetFileNameWithoutExtension(name);
        if (stem.EndsWith("_2", StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return stem + "_2" + ext;
    }

    /// <summary>
    /// Liegt neben einer Petra-Datei die <c>_2</c>-Variante im Workspace, wird sie mit eingebettet.
    /// </summary>
    public static bool TryEmbedVoice2Sibling(
        JsonObject root,
        LocalWorkspaceStore? workspace,
        string petraFileName,
        ISet<string> existingNames)
    {
        var petra = petraFileName?.Trim() ?? string.Empty;
        if (petra.Length == 0 || workspace is null)
        {
            return false;
        }

        var voice2Name = Voice2FileName(petra);
        if (voice2Name.Length == 0 ||
            string.Equals(voice2Name, petra, StringComparison.OrdinalIgnoreCase) ||
            existingNames.Contains(voice2Name))
        {
            return false;
        }

        var path = PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, voice2Name)
                   ?? PlanerAnnouncementRawSoundsWorkspace.TryGetLocalFilePath(workspace, voice2Name);
        if (path is null)
        {
            return false;
        }

        EmbeddedSoundsEditor.UpsertFromFile(root, voice2Name, path);
        existingNames.Add(voice2Name);
        return true;
    }

    public static bool Voice2SiblingWaiting(
        LocalWorkspaceStore? workspace,
        ISet<string> existingNames,
        string petraFileName)
    {
        var petra = petraFileName?.Trim() ?? string.Empty;
        if (petra.Length == 0 || workspace is null)
        {
            return false;
        }

        var voice2Name = Voice2FileName(petra);
        if (voice2Name.Length == 0 ||
            string.Equals(voice2Name, petra, StringComparison.OrdinalIgnoreCase) ||
            existingNames.Contains(voice2Name))
        {
            return false;
        }

        return PlanerEmbeddedSoundsWorkspace.TryGetLocalFilePath(workspace, voice2Name) is not null
               || PlanerAnnouncementRawSoundsWorkspace.TryGetLocalFilePath(workspace, voice2Name) is not null;
    }

    /// <summary>
    /// Gong, Next Stop und der Sondergong bleiben eine Datei. Alles Gesprochene bekommt eine _2-Variante.
    /// </summary>
    public static bool SharesPetraFile(string? fileName, string? sondergongFileName = null)
    {
        var name = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        if (name.Length == 0)
        {
            return true;
        }

        var sondergong = Path.GetFileName(sondergongFileName?.Trim() ?? string.Empty);
        if (sondergong.Length > 0 &&
            string.Equals(name, sondergong, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ext = Path.GetExtension(name);
        var stem = ext.Length == 0 ? name : Path.GetFileNameWithoutExtension(name);
        var underscore = stem.IndexOf('_');
        var bare = stem;
        if (underscore > 0 && stem[..underscore].All(char.IsDigit))
        {
            bare = stem[(underscore + 1)..];
        }

        return bare.Equals("Hambloch Gong", StringComparison.OrdinalIgnoreCase) ||
               bare.Equals("Gong", StringComparison.OrdinalIgnoreCase) ||
               bare.Equals("Next Stop", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Hängt <c>Name_2.wav</c> an, wenn die Datei existiert. Der Export nimmt sonst nur den Petra-Namen mit.
    /// </summary>
    public static void IncludeExistingVoice2Siblings(
        ISet<string> names,
        Func<string, bool> voice2FileExists,
        string? sondergongFileName = null)
    {
        foreach (var name in names.ToArray())
        {
            if (SharesPetraFile(name, sondergongFileName))
            {
                continue;
            }

            var voice2Name = Voice2FileName(name);
            if (voice2Name.Length == 0 ||
                string.Equals(voice2Name, name, StringComparison.OrdinalIgnoreCase) ||
                names.Contains(voice2Name))
            {
                continue;
            }

            if (voice2FileExists(voice2Name))
            {
                names.Add(voice2Name);
            }
        }
    }
}
