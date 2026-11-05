using System.Windows;

namespace SmartOepnv.AppShared.Kom;

/// <summary>
/// Stabiler Owner für KOM-Status-Fenster – nicht das schließende Sende-Dialogfenster.
/// </summary>
internal static class KomFeedbackOwner
{
    public static Window Resolve(Window? contextWindow)
    {
        var app = Application.Current;
        Window? main = app?.MainWindow;
        if (main is { IsLoaded: true } && main.IsVisible)
        {
            return main;
        }

        var active = app?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsLoaded);
        if (active is not null)
        {
            return active;
        }

        if (contextWindow is { IsLoaded: true })
        {
            return contextWindow;
        }

        if (main is not null)
        {
            return main;
        }

        return contextWindow ?? throw new InvalidOperationException("Kein Anwendungsfenster für KOM-Feedback.");
    }
}
