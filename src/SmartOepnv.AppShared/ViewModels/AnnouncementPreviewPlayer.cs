using System.IO;
using System.Windows;
using System.Windows.Media;

namespace SmartOepnv.AppShared.ViewModels;

internal static class AnnouncementPreviewPlayer
{
    private static readonly object Sync = new();
    private static MediaPlayer? _player;

    public static void Play(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Vorschau-Datei nicht gefunden.", filePath);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            PlayOnUiThread(filePath);
            return;
        }

        dispatcher.Invoke(() => PlayOnUiThread(filePath));
    }

    public static void Stop()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            StopOnUiThread();
            return;
        }

        dispatcher.Invoke(StopOnUiThread);
    }

    private static void PlayOnUiThread(string filePath)
    {
        lock (Sync)
        {
            _player ??= new MediaPlayer();
            _player.Stop();
            _player.Open(new Uri(Path.GetFullPath(filePath), UriKind.Absolute));
            _player.Volume = 1.0;
            _player.Play();
        }
    }

    private static void StopOnUiThread()
    {
        lock (Sync)
        {
            _player?.Stop();
        }
    }
}
