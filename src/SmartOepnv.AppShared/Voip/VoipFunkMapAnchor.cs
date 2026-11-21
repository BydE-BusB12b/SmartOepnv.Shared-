using System.Windows;
using System.Windows.Media;

namespace SmartOepnv.AppShared.Voip;

/// <summary>Referenz auf die Fahrzeugkarte – für Position des kompakten Funk-Fensters.</summary>
public static class VoipFunkMapAnchor
{
    private static WeakReference<FrameworkElement>? _mapHost;

    public static event Action? MapBoundsChanged;

    public static void Register(FrameworkElement mapHost) =>
        _mapHost = new WeakReference<FrameworkElement>(mapHost);

    public static void Unregister(FrameworkElement mapHost)
    {
        if (_mapHost?.TryGetTarget(out var current) == true && ReferenceEquals(current, mapHost))
        {
            _mapHost = null;
        }
    }

    public static void NotifyBoundsChanged() => MapBoundsChanged?.Invoke();

    public static Rect? TryGetMapScreenRect()
    {
        if (_mapHost?.TryGetTarget(out var element) != true ||
            element is not { IsVisible: true } ||
            element.ActualWidth < 80 ||
            element.ActualHeight < 80)
        {
            return null;
        }

        try
        {
            var topLeft = element.PointToScreen(new Point(0, 0));
            return new Rect(topLeft.X, topLeft.Y, element.ActualWidth, element.ActualHeight);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
