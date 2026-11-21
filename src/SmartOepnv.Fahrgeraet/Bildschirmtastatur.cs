using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Tastatur im Fenster. Die Systemtastatur von labwc liegt unter dem Vollbild
/// und erscheint deshalb bei einem Textfeld nicht.
/// </summary>
internal static class Bildschirmtastatur
{
    private static readonly (string Lower, string Upper)[][] Rows =
    [
        [("1", "1"), ("2", "2"), ("3", "3"), ("4", "4"), ("5", "5"), ("6", "6"), ("7", "7"), ("8", "8"), ("9", "9"), ("0", "0"), ("ß", "ß"), ("/", "/")],
        [("q", "Q"), ("w", "W"), ("e", "E"), ("r", "R"), ("t", "T"), ("z", "Z"), ("u", "U"), ("i", "I"), ("o", "O"), ("p", "P"), ("ü", "Ü")],
        [("a", "A"), ("s", "S"), ("d", "D"), ("f", "F"), ("g", "G"), ("h", "H"), ("j", "J"), ("k", "K"), ("l", "L"), ("ö", "Ö"), ("ä", "Ä")],
        [("y", "Y"), ("x", "X"), ("c", "C"), ("v", "V"), ("b", "B"), ("n", "N"), ("m", "M"), (".", "."), ("-", "-"), ("_", "_")]
    ];

    private static TextBox? _feld;
    private static Border? _leiste;
    private static bool _gross;
    private static readonly List<Button> _tasten = new();

    public static void Attach(Window window)
    {
        if (window.Content is not Grid root || _leiste is not null)
        {
            return;
        }

        _leiste = Bauen();
        _leiste.IsVisible = false;
        _leiste.ZIndex = 30;
        _leiste.VerticalAlignment = VerticalAlignment.Bottom;
        _leiste.HorizontalAlignment = HorizontalAlignment.Stretch;
        _leiste.Margin = new Thickness(4, 0, 4, 62);
        root.Children.Add(_leiste);

        window.AddHandler(InputElement.GotFocusEvent, OnFocus, RoutingStrategies.Bubble, true);
        window.AddHandler(InputElement.LostFocusEvent, OnLost, RoutingStrategies.Bubble, true);
    }

    public static void SystemEin() => SystemRuf(true);

    public static void SystemAus() => SystemRuf(false);

    private static void OnFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is not TextBox feld || _leiste is null)
        {
            return;
        }

        _feld = feld;
        _leiste.IsVisible = true;
        Dispatcher.UIThread.Post(() => SichtbarHalten(feld), DispatcherPriority.Background);
    }

    private static void OnLost(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not TextBox || _leiste is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(_leiste)?.FocusManager?.GetFocusedElement() is TextBox)
            {
                return;
            }

            _leiste.IsVisible = false;
        }, DispatcherPriority.Background);
    }

    private static Border Bauen()
    {
        var zeilen = new StackPanel { Spacing = 4 };
        foreach (var row in Rows)
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 4
            };
            foreach (var taste in row)
            {
                line.Children.Add(Taste(taste.Lower, taste.Upper));
            }

            zeilen.Children.Add(line);
        }

        var unten = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 4
        };
        var shift = Sonder("⇧", 64);
        shift.Click += (_, _) =>
        {
            _gross = !_gross;
            shift.Background = new SolidColorBrush(Color.Parse(_gross ? "#FFEB3B" : "#FFFFFF"));
            foreach (var taste in _tasten)
            {
                if (taste.Tag is (string lower, string upper))
                {
                    taste.Content = _gross ? upper : lower;
                }
            }
        };
        unten.Children.Add(shift);
        var space = Sonder("Leertaste", 280);
        space.Click += (_, _) => Schreiben(" ");
        unten.Children.Add(space);
        var back = Sonder("⌫", 72);
        back.Click += (_, _) => Loeschen();
        unten.Children.Add(back);
        var close = Sonder("fertig", 88);
        close.Click += (_, _) =>
        {
            if (_leiste is not null)
            {
                _leiste.IsVisible = false;
            }
        };
        unten.Children.Add(close);
        zeilen.Children.Add(unten);

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0D47A1")),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 8),
            Child = zeilen
        };
    }

    private static Button Taste(string lower, string upper)
    {
        var button = Sonder(_gross ? upper : lower, 44);
        button.Tag = (lower, upper);
        _tasten.Add(button);
        button.Click += (_, _) => Schreiben(_gross ? upper : lower);
        return button;
    }

    private static Button Sonder(string text, double width)
    {
        var button = new Button
        {
            Content = text,
            Width = width,
            Height = 42,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.Parse("#1A237E")),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            Focusable = false
        };
        return button;
    }

    private static void Schreiben(string text)
    {
        if (_feld is null)
        {
            return;
        }

        var value = _feld.Text ?? "";
        var start = Math.Min(_feld.SelectionStart, _feld.SelectionEnd);
        var end = Math.Max(_feld.SelectionStart, _feld.SelectionEnd);
        if (start < 0 || start > value.Length)
        {
            start = value.Length;
            end = value.Length;
        }

        end = Math.Min(end, value.Length);
        _feld.Text = value.Remove(start, end - start).Insert(start, text);
        _feld.SelectionStart = _feld.SelectionEnd = start + text.Length;
    }

    private static void Loeschen()
    {
        if (_feld is null)
        {
            return;
        }

        var value = _feld.Text ?? "";
        var start = Math.Min(_feld.SelectionStart, _feld.SelectionEnd);
        var end = Math.Max(_feld.SelectionStart, _feld.SelectionEnd);
        start = Math.Clamp(start, 0, value.Length);
        end = Math.Clamp(end, 0, value.Length);
        if (start == end)
        {
            if (start == 0)
            {
                return;
            }

            start--;
        }

        _feld.Text = value.Remove(start, end - start);
        _feld.SelectionStart = _feld.SelectionEnd = start;
    }

    private static void SichtbarHalten(TextBox feld)
    {
        StyledElement? knoten = feld;
        ScrollViewer? scroll = null;
        while (knoten is not null)
        {
            if (knoten is ScrollViewer viewer)
            {
                scroll = viewer;
                break;
            }

            knoten = knoten.Parent as StyledElement;
        }

        if (scroll is null)
        {
            return;
        }

        var punkt = feld.TranslatePoint(new Point(0, 0), scroll);
        if (punkt is Point pos)
        {
            scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Offset.Y + pos.Y - 80));
        }
    }

    private static void SystemRuf(bool an)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("busctl")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "--user", "call", "sm.puri.OSK0", "/sm/puri/OSK0", "sm.puri.OSK0", "SetVisible", "b", an ? "true" : "false"
                }
            });
        }
        catch
        {
            // Keine Systemtastatur vorhanden.
        }
    }
}
