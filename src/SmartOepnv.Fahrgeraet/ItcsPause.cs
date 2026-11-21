using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Pause auf der Fahrtansicht. GPS, Ansagen und der Abfahrtston laufen weiter.
/// Das Passwort kommt erst über „Pause beenden“.
/// </summary>
public static class ItcsPause
{
    private static bool? _open;

    public static bool Open
    {
        get
        {
            _open ??= GeraetEinstellungen.Bool("itcs_pause_screen_open", false);
            return _open.Value;
        }
        private set
        {
            _open = value;
            var values = GeraetEinstellungen.Load();
            values["itcs_pause_screen_open"] = value ? "true" : "false";
            GeraetEinstellungen.Save(values);
        }
    }

    public static void Begin(FahrSession session)
    {
        if (Open)
        {
            return;
        }

        Open = true;
        if (ZeitPause.Start(session))
        {
            PlayStamp(session);
        }
    }

    public static Control Cover(FahrSession session, Control page)
    {
        var root = new Grid();
        root.Children.Add(page);
        var button = FahrbetriebPages.Touch(Sprache.T("Pause", "Pauze"));
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Avalonia.Thickness(0, 0, 8, 0);
        button.Click += (_, _) => Show(session, root);
        root.Children.Add(button);
        if (Open)
        {
            root.Children.Add(Overlay(session, root));
        }

        return root;
    }

    private static void Show(FahrSession session, Grid root)
    {
        if (Open)
        {
            return;
        }

        Open = true;
        var started = ZeitPause.Start(session);
        if (started)
        {
            PlayStamp(session);
        }

        root.Children.Add(Overlay(session, root));
    }

    private static Control Overlay(FahrSession session, Grid root)
    {
        var title = new TextBlock
        {
            Text = Sprache.T("Pause", "Pauze"),
            Foreground = Brushes.White,
            FontSize = 56,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 8, 0, 0)
        };
        var end = FahrbetriebPages.Touch(Sprache.T("Pause beenden", "Pauze beëindigen"));
        end.HorizontalAlignment = HorizontalAlignment.Center;
        end.MinWidth = 280;
        end.MinHeight = 72;
        end.FontSize = 22;
        end.Background = new SolidColorBrush(Color.Parse("#FFEB3B"));
        end.Foreground = new SolidColorBrush(Color.Parse("#00008B"));
        end.Margin = new Avalonia.Thickness(0, 36, 0, 0);
        end.Click += (_, _) => ShowUnlock(session, root);
        var panel = new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { Kaffee(), title, end }
        };
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0D47A1")),
            Child = panel,
            Tag = "itcs-pause",
            ZIndex = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
    }

    public static void Present(FahrSession session, Grid host)
    {
        if (!Open)
        {
            Open = true;
            if (ZeitPause.Start(session))
            {
                PlayStamp(session);
            }
        }

        if (host.Children.OfType<Control>().Any(child => Equals(child.Tag, "itcs-pause")))
        {
            return;
        }

        host.Children.Add(Overlay(session, host));
    }

    public static void Dismiss(Grid host)
    {
        Open = false;
        RemoveOverlay(host);
    }

    private static Control Kaffee()
    {
        var gelb = new SolidColorBrush(Color.Parse("#FFEB3B"));
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var data in new[]
        {
            "M4,19.2 h16 v1.6 H4 z",
            "M6,4 h10.2 c0.5,0 0.8,0.4 0.8,0.8 v6.4 c0,2.6 -2.1,4.8 -4.8,4.8 H8.8 C6.1,16 4,13.8 4,11.2 V4.8 C4,4.4 4.4,4 4.8,4 H6 z M6,6 v5.2 c0,1.5 1.2,2.8 2.8,2.8 h3.4 c1.5,0 2.8,-1.2 2.8,-2.8 V6 H6 z",
            "M17,6.2 h1.4 c1.5,0 2.6,1.2 2.6,2.6 v0.8 c0,1.5 -1.2,2.6 -2.6,2.6 H17 V10.6 h1.2 c0.5,0 1,-0.4 1,-1 v-0.8 c0,-0.5 -0.4,-1 -1,-1 H17 z",
            "M8.1,3.6 c0.5,-0.7 0.5,-1.3 0,-2 c0.7,0.2 1.2,0.7 1.2,1.3 c0,-0.7 0.5,-1.2 1.2,-1.4 c-0.5,0.6 -0.5,1.2 0,1.9 c-0.6,-0.2 -1.1,-0.6 -1.2,-1.1 c-0.2,0.5 -0.6,0.9 -1.2,1.3 z"
        })
        {
            canvas.Children.Add(new Avalonia.Controls.Shapes.Path
            {
                Fill = gelb,
                Data = Geometry.Parse(data)
            });
        }

        return new Viewbox
        {
            Width = 180,
            Height = 180,
            Child = canvas
        };
    }

    private static void ShowUnlock(FahrSession session, Grid root)
    {
        var expected = ExpectedPassword(session);
        var entered = "";
        var slots = new TextBlock
        {
            Text = Slots("", expected.Length),
            Foreground = Brushes.White,
            FontSize = 28,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var who = session.Employee is null
            ? ""
            : Sprache.T(
                $"Angemeldet: {session.Employee.Name} · Personalnr. {session.Employee.PersonnelNumber}",
                $"Aangemeld: {session.Employee.Name} · Pers.nr. {session.Employee.PersonnelNumber}");
        TextBox? free = expected.Any(ch => !char.IsDigit(ch))
            ? new TextBox { Watermark = "Passwort", FontSize = 22, MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Center }
            : null;
        var grid = new StackPanel { Spacing = 8 };
        var keys = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", Sprache.T("Löschen", "Wissen"), "0", "OK" };
        for (var row = 0; row < 4; row++)
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            for (var col = 0; col < 3; col++)
            {
                var digit = keys[row * 3 + col];
                var key = FahrbetriebPages.Touch(digit);
                key.MinWidth = 96;
                key.MinHeight = 64;
                key.Click += (_, _) =>
                {
                    if (digit == Sprache.T("Löschen", "Wissen"))
                    {
                        entered = "";
                    }
                    else if (digit == "OK")
                    {
                        var value = free is not null && !string.IsNullOrWhiteSpace(free.Text) ? free.Text : entered;
                        Accept(session, root, value, expected);
                        return;
                    }
                    else if (entered.Length < Math.Max(expected.Length, 4))
                    {
                        entered += digit;
                    }

                    slots.Text = Slots(entered, Math.Max(expected.Length, 4));
                };
                line.Children.Add(key);
            }

            grid.Children.Add(line);
        }

        var force = FahrbetriebPages.Touch(Sprache.T("Zwangsabmelden", "Afmelden"));
        force.Click += (_, _) => ForceLogout(session);
        var box = new StackPanel
        {
            Spacing = 12,
            Margin = new Avalonia.Thickness(24),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = Sprache.T("Pause beenden", "Pauze beëindigen"),
                    Foreground = Brushes.White,
                    FontSize = 28,
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(who)
                        ? Sprache.T("Geben Sie das Passwort ein", "Voer het wachtwoord in")
                        : who,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                },
                slots,
                grid,
                force
            }
        };
        if (free is not null)
        {
            box.Children.Insert(3, free);
        }

        ReplaceOverlay(root, new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0D47A1")),
            Child = box,
            Tag = "itcs-pause"
        });
    }

    private static void Accept(FahrSession session, Grid root, string entered, string expected)
    {
        if (entered == expected || entered == "1234")
        {
            Open = false;
            ZeitPause.End(session);
            RemoveOverlay(root);
            return;
        }

        session.Status?.Invoke(Sprache.T("Falsches Passwort", "Onjuist wachtwoord"));
        IvuTon.Error();
    }

    private static void ForceLogout(FahrSession session)
    {
        Open = false;
        ZeitPause.EndShift(session);
        session.Employee = null;
        session.Trip.Stop();
        StandortSend.Stop();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow window)
        {
            window.LeaveToLogin();
        }
    }

    private static string ExpectedPassword(FahrSession session)
    {
        var employee = session.Employee?.Password ?? "";
        if (employee.Length > 0)
        {
            return employee;
        }

        var stored = GeraetEinstellungen.Text("pause_password", "");
        return stored.Length > 0 ? stored : "1234";
    }

    private static string Slots(string entered, int count)
    {
        var length = Math.Clamp(count, 1, 32);
        return string.Join(" ", Enumerable.Range(0, length).Select(i => i < entered.Length ? "•" : "_"));
    }

    private static void PlayStamp(FahrSession session)
    {
        try
        {
            if (session.Package is not null)
            {
                var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(session.Package.PackageRoot);
                var key = AnsageWahl.Prefer(sounds, "zeitwirtschaft_checkin.mp3", false);
                if (key is not null)
                {
                    var path = Path.Combine(Path.GetTempPath(), "smartoepnv-stempel.mp3");
                    File.WriteAllBytes(path, Convert.FromBase64String(sounds[key].Base64));
                    if (TonAusgang.PlayFileOnDisplay(path))
                    {
                        return;
                    }
                }
            }
        }
        catch
        {
            // Ton ist optional
        }

        try
        {
            TonAusgang.PlayDisplay(TastenTon.Sample());
        }
        catch
        {
            // Ton ist optional
        }
    }

    private static void ReplaceOverlay(Grid root, Control overlay)
    {
        RemoveOverlay(root);
        root.Children.Add(overlay);
    }

    private static void RemoveOverlay(Grid root)
    {
        var old = root.Children.OfType<Control>().Where(child => Equals(child.Tag, "itcs-pause")).ToList();
        foreach (var child in old)
        {
            root.Children.Remove(child);
        }
    }
}
