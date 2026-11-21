using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Die Liste folgt dem Finger. Der Balken an der Seite ist keine Bedienung.
/// Ab wenigen Pixeln setzt die Bewegung ein, und der Weg ist verstärkt.
/// </summary>
internal static class FingerZug
{
    private const double Start = 3;
    private const double Kraft = 2.6;

    private static DateTime _ziehtBis = DateTime.MinValue;

    public static bool Zieht => DateTime.UtcNow < _ziehtBis;

    public static void An(ScrollViewer scroll, Action? beiZug = null)
    {
        if (scroll.Classes.Contains("finger"))
        {
            return;
        }

        scroll.Classes.Add("finger");
        scroll.Background ??= Brushes.Transparent;
        scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;

        var spur = new Spur();
        scroll.AddHandler(InputElement.PointerPressedEvent, (_, e) => Druck(scroll, spur, e), RoutingStrategies.Tunnel, true);
        scroll.AddHandler(InputElement.PointerMovedEvent, (_, e) => Zug(scroll, spur, e, beiZug), RoutingStrategies.Tunnel, true);
        scroll.AddHandler(InputElement.PointerReleasedEvent, (_, e) => Los(spur, e), RoutingStrategies.Tunnel, true);
        scroll.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => Abbruch(spur), RoutingStrategies.Direct, true);
    }

    public static void In(Control wurzel)
    {
        if (wurzel is ScrollViewer scroll)
        {
            An(scroll);
            if (scroll.Content is Control inhalt)
            {
                In(inhalt);
            }

            return;
        }

        if (wurzel is ListBox liste)
        {
            void Haken(object? sender, EventArgs args)
            {
                liste.ApplyTemplate();
                var innen = liste.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                if (innen is not null)
                {
                    An(innen);
                }
            }

            if (liste.IsLoaded)
            {
                Haken(null, EventArgs.Empty);
            }
            else
            {
                liste.Loaded += Haken;
            }
        }

        foreach (var kind in Kinder(wurzel))
        {
            In(kind);
        }
    }

    private static IEnumerable<Control> Kinder(Control wurzel)
    {
        switch (wurzel)
        {
            case Panel panel:
                foreach (var kind in panel.Children.OfType<Control>())
                {
                    yield return kind;
                }

                break;
            case ContentControl inhalt when inhalt.Content is Control kind:
                yield return kind;
                break;
            case Decorator dekor when dekor.Child is Control kind:
                yield return kind;
                break;
        }
    }

    private static void Druck(ScrollViewer scroll, Spur spur, PointerPressedEventArgs e)
    {
        var punkt = e.GetCurrentPoint(scroll);
        if (!punkt.Properties.IsLeftButtonPressed)
        {
            return;
        }

        var jetzt = DateTime.UtcNow;
        if (spur.Runter && (jetzt - spur.Zeit).TotalMilliseconds < 50)
        {
            return;
        }

        if (e.Pointer.Captured != null)
        {
            e.Pointer.Capture(null);
        }

        spur.Runter = true;
        spur.Zog = false;
        spur.Zeit = jetzt;
        spur.Y = punkt.Position.Y;
        spur.Start = punkt.Position.Y;
    }

    private static void Zug(ScrollViewer scroll, Spur spur, PointerEventArgs e, Action? beiZug)
    {
        if (!spur.Runter)
        {
            return;
        }

        var y = e.GetPosition(scroll).Y;
        if (!spur.Zog)
        {
            if (Math.Abs(y - spur.Start) < Start || InnenKann(scroll, e))
            {
                return;
            }

            spur.Zog = true;
            _ziehtBis = DateTime.UtcNow.AddMilliseconds(160);
            e.Pointer.Capture(scroll);
            beiZug?.Invoke();
        }

        var delta = (spur.Y - y) * Kraft;
        spur.Y = y;
        var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        var next = Math.Clamp(scroll.Offset.Y + delta, 0, max);
        scroll.Offset = new Vector(scroll.Offset.X, next);
        e.Handled = true;
    }

    private static bool InnenKann(ScrollViewer selbst, PointerEventArgs e)
    {
        var knoten = e.Source as Visual;
        while (knoten is not null && knoten != selbst)
        {
            if (knoten is ScrollViewer innen &&
                innen != selbst &&
                innen.Extent.Height > innen.Viewport.Height + 1)
            {
                return true;
            }

            knoten = knoten.GetVisualParent();
        }

        return false;
    }

    private static void Los(Spur spur, PointerReleasedEventArgs e)
    {
        if (!spur.Runter)
        {
            return;
        }

        if (spur.Zog)
        {
            _ziehtBis = DateTime.UtcNow.AddMilliseconds(160);
            e.Handled = true;
        }

        spur.Runter = false;
        spur.Zog = false;
        e.Pointer.Capture(null);
    }

    private static void Abbruch(Spur spur)
    {
        if (spur.Zog)
        {
            _ziehtBis = DateTime.UtcNow.AddMilliseconds(160);
        }

        spur.Runter = false;
        spur.Zog = false;
    }

    private sealed class Spur
    {
        public bool Runter;
        public bool Zog;
        public double Y;
        public double Start;
        public DateTime Zeit;
    }
}
