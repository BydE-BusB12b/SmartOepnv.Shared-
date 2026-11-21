using System.Windows.Threading;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Schwere Speicherung erst nach dem nächsten Bild, damit der Klick sofort sichtbar ist.
/// </summary>
public static class UiDispatch
{
    public static void RunAfterPaint(Action work)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            work();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Background, work);
    }
}
