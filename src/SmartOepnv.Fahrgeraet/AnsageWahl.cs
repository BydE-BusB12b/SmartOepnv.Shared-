using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Stimme 2 aus <c>routeAnnouncementVoices</c>: <c>und.wav</c> bleibt Petra, <c>und_2.wav</c> ist Stimme 2.
/// </summary>
public static class AnsageWahl
{
    public static bool RouteUsesVoice2(FahrSession session)
    {
        if (session.Package is null || string.IsNullOrWhiteSpace(session.RouteName))
        {
            return false;
        }

        var map = RouteAnnouncementVoiceEditor.LoadFromRoot(session.Package.PackageRoot);
        return AnnouncementVoiceFiles.IsVoice2(
            RouteAnnouncementVoiceEditor.GetForRoute(map, session.RouteName));
    }

    public static string? Prefer(
        IReadOnlyDictionary<string, (string Base64, int Size)> sounds,
        string? fileName,
        bool voice2)
    {
        var name = fileName?.Trim() ?? "";
        if (name.Length == 0)
        {
            return null;
        }

        if (voice2 && !NeverSwap(name))
        {
            var alt = AnnouncementVoiceFiles.Voice2FileName(name);
            var swapped = Find(sounds, alt);
            if (swapped is not null && !string.Equals(swapped, name, StringComparison.OrdinalIgnoreCase))
            {
                return swapped;
            }
        }

        return Find(sounds, name);
    }

    public static bool KeepPetra(string? fileName) => NeverSwap(fileName ?? "");

    private static bool NeverSwap(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        return name.Contains("gong") || name.Contains("next stop") || name.Contains("nextstop");
    }

    private static string? Find(IReadOnlyDictionary<string, (string Base64, int Size)> sounds, string fileName) =>
        sounds.Keys.FirstOrDefault(key => string.Equals(key, fileName, StringComparison.OrdinalIgnoreCase));
}
