using System.Windows;
using System.Windows.Media;
using System.IO;

namespace SmartOepnv.AppShared.ViewModels;

/// <summary>
/// Nutzt dieselben Tondateien wie die Android-App:
/// IVU_mailchat_notification.mp3 und IVU_mailchat_unfallruf.mp3.
/// </summary>
internal static class LeitstelleMessageTonePlayer
{
    private const string MailToneFile = "IVU_mailchat_notification.mp3";
    private const string SosToneFile = "IVU_mailchat_unfallruf.mp3";

    private static readonly object Sync = new();
    private static MediaPlayer? _player;

    public static void PlayMail()
    {
        PlayTone(MailToneFile);
    }

    public static void PlaySos()
    {
        PlayTone(SosToneFile);
    }

    private static void PlayTone(string fileName)
    {
        var tonePath = ResolveTonePath(fileName);
        if (tonePath is null)
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            PlayOnUiThread(tonePath);
            return;
        }

        dispatcher.Invoke(() => PlayOnUiThread(tonePath));
    }

    private static void PlayOnUiThread(string tonePath)
    {
        lock (Sync)
        {
            _player ??= new MediaPlayer();
            try
            {
                _player.Stop();
                _player.Open(new Uri(tonePath, UriKind.Absolute));
                _player.Volume = 1.0;
                _player.Play();
            }
            catch
            {
                // Ton ist optional; UI darf nicht abbrechen.
            }
        }
    }

    private static string? ResolveTonePath(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "sounds", fileName),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..",
                "GPSAnsagen", "app", "src", "main", "assets", "sounds", fileName)),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..",
                "GPSAnsagen", "app", "src", "main", "assets", "sounds", fileName))
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
