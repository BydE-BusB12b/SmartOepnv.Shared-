using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NAudio.Wave;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

public static class FahrBetrieb
{
    public static bool Paused { get; set; }
}

public static class TastenTon
{
    public static byte[] Sample() => Beep();

    public static void Play()
    {
        if (!GeraetEinstellungen.Bool("button_sounds_enabled", true))
        {
            return;
        }

        try
        {
            if (IvuTon.Piep())
            {
                return;
            }

            TonAusgang.PlayDisplay(Beep());
        }
        catch
        {
            // Kein Ausgabegerät
        }
    }

    private static byte[] Beep()
    {
        const int rate = 8000;
        const int samples = 640;
        var data = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * 880 * i / rate) * 8000);
            data[i * 2] = (byte)(sample & 0xFF);
            data[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + data.Length);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(data.Length);
            writer.Write(data);
        }

        return stream.ToArray();
    }
}

public static class BluetoothZwang
{
    private static int _started;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    if (GeraetEinstellungen.Bool("bluetooth_force_pairing", true))
                    {
                        if (OperatingSystem.IsLinux())
                        {
                            var mac = GeraetEinstellungen.Text("bluetooth_device", "").Trim();
                            RunBluetooth("bluetoothctl", "power on");
                            if (mac.Length > 0)
                            {
                                RunBluetooth("bluetoothctl", "connect " + mac);
                            }
                        }
                        else if (OperatingSystem.IsWindows())
                        {
                            RunBluetooth("powershell", "-NoProfile -Command \"Start-Service bthserv -ErrorAction SilentlyContinue; Get-PnpDevice -Class Bluetooth | Where-Object { $_.Status -ne 'OK' } | Enable-PnpDevice -Confirm:$false\"");
                        }
                    }
                }
                catch
                {
                    // Bluetooth-Dienst fehlt auf diesem Rechner
                }

                await Task.Delay(TimeSpan.FromSeconds(15));
            }
        });
    }

    private static void RunBluetooth(string file, string arguments)
    {
        Process.Start(new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });
    }
}

public static class FahrProtokoll
{
    public static void Add(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            var dir = Path.Combine(
                SmartOepnv.Core.AppPaths.GetRoamingDataDirectory(FahrgeraetRouteSync.AppFolder),
                "workspace");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "protokoll.txt"), $"{DateTime.Now:HH:mm:ss}  {text}\n");
        }
        catch
        {
            // Protokoll ist optional
        }
    }

    public static string Read()
    {
        var path = Path.Combine(
            SmartOepnv.Core.AppPaths.GetRoamingDataDirectory(FahrgeraetRouteSync.AppFolder),
            "workspace",
            "protokoll.txt");
        return File.Exists(path) ? File.ReadAllText(path) : "Noch keine Einträge.";
    }
}

public static class FahrLuecken
{
    public static Control DropboxSeite()
    {
        var store = new DropboxSettingsStore(FahrgeraetRouteSync.AppFolder);
        var settings = store.Load();
        var folder = FahrbetriebPages.Field("Dropbox-Ordner");
        folder.Text = string.IsNullOrWhiteSpace(settings.FolderPath) ? DropboxConstants.DefaultFolderPath : settings.FolderPath;
        var code = FahrbetriebPages.Field("Freigabecode");
        var info = FahrbetriebPages.Info(settings.IsConnected
            ? $"Verbunden: {settings.ConnectedAccountEmail ?? settings.ConnectedAccountName}"
            : "Nicht verbunden.");
        var open = FahrbetriebPages.Touch("Bei Dropbox anmelden");
        open.Click += (_, _) =>
        {
            settings.FolderPath = folder.Text ?? DropboxConstants.DefaultFolderPath;
            settings.AppKey = DropboxConstants.DefaultAppKey;
            settings.AppSecret = DropboxConstants.DefaultAppSecret;
            store.Save(settings);
            var url = new DropboxApiClient(store).BuildAuthorizeUrl();
            try
            {
                Open(url);
                info.Text = "Im Browser anmelden und den Code hier einfügen.";
            }
            catch (Exception ex)
            {
                info.Text = ex.Message + "\n" + url;
            }
        };
        var exchange = FahrbetriebPages.Touch("Code einlösen");
        exchange.Click += async (_, _) =>
        {
            try
            {
                settings.FolderPath = folder.Text ?? settings.FolderPath;
                store.Save(settings);
                await new DropboxApiClient(store).ExchangeCodeForTokensAsync(code.Text ?? "");
                info.Text = "Dropbox ist verbunden.";
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        var saveFolder = FahrbetriebPages.Touch("Ordner speichern");
        saveFolder.Click += (_, _) =>
        {
            settings = store.Load();
            settings.FolderPath = folder.Text ?? DropboxConstants.DefaultFolderPath;
            store.Save(settings);
            info.Text = "Ordner gespeichert.";
        };
        var test = FahrbetriebPages.Touch("Verbindung prüfen");
        test.Click += async (_, _) =>
        {
            try
            {
                var result = await new DropboxApiClient(store).TestConnectionAsync();
                info.Text = result.Message;
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        var disconnect = FahrbetriebPages.Touch("Trennen");
        disconnect.Click += (_, _) =>
        {
            settings = store.Load();
            settings.AccessToken = null;
            settings.RefreshToken = null;
            settings.ConnectedAccountEmail = null;
            settings.ConnectedAccountName = null;
            store.Save(settings);
            info.Text = "Verbindung gelöscht.";
        };
        return FahrbetriebPages.Column("Dropbox", folder, open, code, exchange, saveFolder, test, disconnect, info);
    }

    public static Control Transfer(FahrSession session)
    {
        var list = Routes(session);
        list.SelectionMode = SelectionMode.Multiple;
        var info = FahrbetriebPages.Info("Import und Export nutzen dasselbe Routenpaket wie das Tablet.");
        var import = FahrbetriebPages.Touch("Paket importieren");
        import.Click += async (_, _) =>
        {
            var path = await Pick(list, false, "*.json");
            if (path is null || session.Package is null && path.Length == 0)
            {
                return;
            }

            session.Package = EditableRoutePackage.FromJson(await File.ReadAllTextAsync(path));
            SavePackage(session);
            FillRoutes(session, list);
            info.Text = "Paket importiert.";
        };
        var export = FahrbetriebPages.Touch("Paket exportieren");
        export.Click += async (_, _) => await SaveJson(list, session, session.Package?.ToJson(false, false, true), "routes_export.json", info);
        var one = FahrbetriebPages.Touch("Gewählte Routen exportieren");
        one.Click += async (_, _) =>
        {
            var names = list.SelectedItems?.OfType<string>().ToList() ?? [];
            if (names.Count == 0 || session.Package is null)
            {
                info.Text = "Mindestens eine Route wählen.";
                return;
            }

            var root = new JsonObject();
            foreach (var name in names)
            {
                root[name] = JsonSerializer.SerializeToNode(session.Package.GetStops(name).Select(stop => stop.Name));
            }

            await SaveJson(list, session, root.ToJsonString(), "routen.json", info);
        };
        var all = FahrbetriebPages.Touch("Route an alle Fahrzeuge senden");
        all.Click += async (_, _) =>
        {
            var client = FahrbetriebPages.OpenDropbox();
            if (client is null || session.Package is null)
            {
                info.Text = "Dropbox oder Paket fehlt.";
                return;
            }

            await client.UploadRouteFileAsync(session.Package.ToJson(false, false, true));
            info.Text = "routes_export.json ist in Dropbox. Alle Fahrzeuge übernehmen sie beim Abgleich.";
        };
        FillRoutes(session, list);
        return FahrbetriebPages.Column("Datentransfer", import, export, one, all, info, list);
    }

    public static Control Ansagen(FahrSession session)
    {
        var name = FahrbetriebPages.Field("Dateiname");
        name.Text = "ansage.wav";
        var info = FahrbetriebPages.Info("Eine Ansage aufnehmen oder mehrere Dateien ins Paket übernehmen.");
        var start = FahrbetriebPages.Touch("Aufnahme starten");
        var stop = FahrbetriebPages.Touch("Aufnahme speichern");
        var recorder = new AnsageRecorder();
        start.Click += (_, _) =>
        {
            recorder.Start();
            info.Text = "Aufnahme läuft.";
        };
        stop.Click += (_, _) =>
        {
            var path = recorder.Stop();
            if (path is null || session.Package is null)
            {
                info.Text = "Keine Aufnahme.";
                return;
            }

            EmbeddedSoundsEditor.UpsertFromFile(session.Package.PackageRoot, name.Text ?? "ansage.wav", path);
            SavePackage(session);
            info.Text = "Ansage im Paket gespeichert.";
        };
        var many = FahrbetriebPages.Touch("Mehrere Ansagen einlesen");
        many.Click += async (_, _) =>
        {
            if (session.Package is null)
            {
                info.Text = "Kein Paket.";
                return;
            }

            var window = TopLevel.GetTopLevel(name) as Window;
            if (window is null)
            {
                return;
            }

            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Audio") { Patterns = ["*.wav", "*.mp3"] }]
            });
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    EmbeddedSoundsEditor.UpsertFromFile(session.Package.PackageRoot, Path.GetFileName(path), path);
                }
            }

            SavePackage(session);
            info.Text = $"{files.Count} Dateien übernommen.";
        };
        var specials = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        if (session.Package?.PackageRoot["specialAnnouncements"] is JsonObject obj)
        {
            foreach (var item in obj)
            {
                specials.Items.Add(item.Key);
            }
        }

        var play = FahrbetriebPages.Touch("Sonderansage abspielen");
        play.Click += (_, _) =>
        {
            if (specials.SelectedItem is not string key || session.Package is null)
            {
                return;
            }

            var voice2 = GeraetEinstellungen.Bool("special_list_announcement_voice_2", false);
            var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(session.Package.PackageRoot);
            var match = AnsageWahl.Prefer(sounds, key + ".wav", voice2)
                        ?? sounds.Keys.FirstOrDefault(file => file.Contains(key, StringComparison.OrdinalIgnoreCase) &&
                            (!voice2 || file.Contains("_2", StringComparison.OrdinalIgnoreCase)));
            if (match is null)
            {
                info.Text = "Keine Tondatei zu dieser Sonderansage.";
                return;
            }

            var zone = AnnouncementAudioOutput.Both;
            if (session.Package.PackageRoot["specialAnnouncements"] is JsonObject list &&
                list[key] is JsonObject entry)
            {
                zone = AnnouncementAudioOutput.Normalize(entry["audioOutput"]?.ToString());
            }

            Task.Run(() => AnsageSequenz.PlayClip(session, match, voice2, zone));
            info.Text = (voice2 ? "Stimme 2 · " : "Petra · ") + key + " · Bluetooth";
        };
        var petra = FahrbetriebPages.Touch("Petra");
        var stimme2 = FahrbetriebPages.Touch("Stimme 2");
        void PaintVoice()
        {
            var on = GeraetEinstellungen.Bool("special_list_announcement_voice_2", false);
            info.Text = on ? "Spezialliste: Stimme 2" : "Spezialliste: Petra";
        }

        petra.Click += (_, _) =>
        {
            var values = GeraetEinstellungen.Load();
            values["special_list_announcement_voice_2"] = "false";
            GeraetEinstellungen.Save(values);
            PaintVoice();
        };
        stimme2.Click += (_, _) =>
        {
            var values = GeraetEinstellungen.Load();
            values["special_list_announcement_voice_2"] = "true";
            GeraetEinstellungen.Save(values);
            PaintVoice();
        };
        PaintVoice();
        return FahrbetriebPages.Column("Ansagen", name, start, stop, many, info, specials, petra, stimme2, play);
    }

    public static Control LinienCodes(FahrSession session)
    {
        var box = new TextBox { AcceptsReturn = true, MinHeight = 160, FontSize = 18 };
        box.Text = GeraetEinstellungen.Text("line_number_codes", "");
        var info = FahrbetriebPages.Info("Eine Zeile je Code, zum Beispiel 901.");
        var save = FahrbetriebPages.Touch("Codes speichern");
        save.Click += (_, _) =>
        {
            var values = GeraetEinstellungen.Load();
            values["line_number_codes"] = box.Text ?? "";
            GeraetEinstellungen.Save(values);
            info.Text = "Gespeichert.";
        };
        var send = FahrbetriebPages.Touch("Gewählte Zeile senden");
        send.Click += (_, _) =>
        {
            var line = (box.Text ?? "").Split('\n').FirstOrDefault(row => row.Trim().Length > 0) ?? "";
            var digits = new string(line.Where(char.IsDigit).Take(3).ToArray()).PadLeft(3, '0');
            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("l" + digits), 9600);
            info.Text = "l" + digits;
        };
        return FahrbetriebPages.Column("Liniennummerncodes", box, save, send, info);
    }

    public static Control ManuelleAnzeige(FahrSession session)
    {
        var interior = FahrbetriebPages.Field("Innenanzeige");
        var destination = FahrbetriebPages.Field("Außenziel");
        var info = FahrbetriebPages.Info("");
        var send = FahrbetriebPages.Touch("Anzeige senden");
        send.Click += async (_, _) =>
        {
            info.Text = await DisplayTelegrams.SendAsync(
                session,
                "",
                destination.Text ?? "",
                interior.Text ?? "");
        };
        var clear = FahrbetriebPages.Touch("Matrix löschen");
        clear.Click += (_, _) =>
        {
            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("l000"), 9600);
            VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("lE00"), 9600);
            info.Text = "Löschung gesendet.";
        };
        var test = FahrbetriebPages.Touch("Anzeigentest");
        test.Click += async (_, _) =>
        {
            info.Text = await DisplayTelegrams.SendAsync(session, "000", "TEST", "TEST");
        };
        return FahrbetriebPages.Column("Manuelle Anzeige", interior, destination, send, clear, test, info);
    }

    public static Control Ziele(FahrSession session)
    {
        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        var names = new List<string>();
        if (session.Package is not null && !string.IsNullOrWhiteSpace(session.RouteName))
        {
            foreach (var stop in session.Package.GetStops(session.RouteName))
            {
                foreach (var value in new[] { stop.Destination, stop.EndDestination, stop.Name })
                {
                    if (!string.IsNullOrWhiteSpace(value) && !names.Contains(value))
                    {
                        names.Add(value);
                        list.Items.Add(value);
                    }
                }
            }
        }

        var pdf = FahrbetriebPages.Touch("PDF aller Ziele");
        pdf.Click += async (_, _) =>
        {
            var window = TopLevel.GetTopLevel(list) as Window;
            var file = window is null
                ? null
                : await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    SuggestedFileName = "ziele.pdf",
                    FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }]
                });
            var path = file?.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            await File.WriteAllBytesAsync(path, SimplePdf.Create(names));
        };
        return FahrbetriebPages.Column("Zielliste", pdf, list);
    }

    public static Control Abfahrten(FahrSession session)
    {
        var id = FahrbetriebPages.Field("VRR-Haltestellennummer");
        if (session.Package is not null && !string.IsNullOrWhiteSpace(session.RouteName))
        {
            id.Text = session.Package.GetStops(session.RouteName).Select(stop => stop.VrrStopId).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        var info = FahrbetriebPages.Info("Echtzeit von efa.vrr.de.");
        var load = FahrbetriebPages.Touch("Abfahrten laden");
        load.Click += async (_, _) =>
        {
            list.Items.Clear();
            try
            {
                foreach (var row in await VrrAbfahrten.LoadAsync(id.Text ?? ""))
                {
                    list.Items.Add(row);
                }

                info.Text = list.Items.Count == 0 ? "Keine Abfahrten." : $"{list.Items.Count} Abfahrten.";
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        return FahrbetriebPages.Column("VRR-Abfahrten", id, load, info, list);
    }

    public static Control Mitarbeiter(FahrSession session)
    {
        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        void Show()
        {
            list.Items.Clear();
            foreach (var employee in session.Package?.Employees ?? [])
            {
                list.Items.Add($"{employee.Name}  {employee.PersonnelNumber}");
            }
        }

        var person = FahrbetriebPages.Field("Name");
        var number = FahrbetriebPages.Field("Personalnummer");
        var password = FahrbetriebPages.Field("Passwort");
        password.PasswordChar = '•';
        var info = FahrbetriebPages.Info("");
        var add = FahrbetriebPages.Touch("Mitarbeiter speichern");
        add.Click += (_, _) =>
        {
            if (session.Package is null)
            {
                info.Text = "Kein Paket.";
                return;
            }

            var employees = session.Package.Employees.ToList();
            employees.Add(new EmployeeRosterItem
            {
                Name = person.Text ?? "",
                PersonnelNumber = number.Text ?? "",
                Password = password.Text ?? ""
            });
            session.Package.ReplaceEmployees(employees);
            EmployeeRosterEditor.SaveToRoot(session.Package.PackageRoot, employees);
            SavePackage(session);
            password.Text = "";
            Show();
            info.Text = "Gespeichert.";
        };
        Show();
        return FahrbetriebPages.Column("Mitarbeiter", list, person, number, password, add, info);
    }

    public static Control Geraete(FahrSession session)
    {
        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        foreach (var vehicle in session.Package?.RegisteredVehicles ?? [])
        {
            list.Items.Add($"{vehicle.Name}  {vehicle.PhoneNumber}");
        }

        var name = FahrbetriebPages.Field("Gerätename");
        name.Text = GeraetEinstellungen.Text("device_display_name", "");
        var phone = FahrbetriebPages.Field("Telefon");
        phone.Text = session.DevicePhone;
        var info = FahrbetriebPages.Info("");
        var save = FahrbetriebPages.Touch("Gerät anmelden");
        save.Click += (_, _) =>
        {
            if (session.Package is null)
            {
                info.Text = "Kein Paket.";
                return;
            }

            var vehicles = session.Package.RegisteredVehicles.ToList();
            vehicles.Add(new RegisteredVehicleItem { Name = name.Text ?? "", PhoneNumber = phone.Text ?? "" });
            session.Package.ReplaceRegisteredVehicles(vehicles);
            RegisteredVehiclesEditor.SaveToRoot(session.Package.PackageRoot, vehicles, session.Package.RegisteredVehiclePhoneRedirects);
            SavePackage(session);
            session.DevicePhone = phone.Text ?? session.DevicePhone;
            info.Text = "Gerät im Paket gespeichert.";
        };
        return FahrbetriebPages.Column("Geräteanmeldung", list, name, phone, save, info);
    }

    public static Control Nachricht(FahrSession session)
    {
        var text = new TextBox { AcceptsReturn = true, MinHeight = 120, FontSize = 18, Watermark = "Nachricht an die Leitstelle" };
        var info = FahrbetriebPages.Info("");
        var send = FahrbetriebPages.Touch("Senden");
        send.Click += async (_, _) =>
        {
            var client = FahrbetriebPages.OpenDropbox();
            var phone = new string((session.DevicePhone ?? "").Where(char.IsDigit).ToArray());
            if (client is null || phone.Length == 0)
            {
                info.Text = "Dropbox oder Gerätetelefon fehlt.";
                return;
            }

            var json = JsonSerializer.Serialize(new
            {
                text = text.Text ?? "",
                from = session.Employee?.Name ?? "",
                phone,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            await client.UploadNamedFileAsync($"mailchat_{phone}.json", json);
            info.Text = "Nachricht liegt in Dropbox.";
        };
        return FahrbetriebPages.Column("Nachricht an die Leitstelle", text, send, info);
    }

    public static Control Pause(FahrSession session, Action refresh)
    {
        var password = FahrbetriebPages.Field("Pausenpasswort");
        password.PasswordChar = '•';
        var info = FahrbetriebPages.Info(FahrBetrieb.Paused ? "Pause läuft." : "Passwort festlegen oder eingeben.");
        var set = FahrbetriebPages.Touch("Passwort festlegen");
        set.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(password.Text))
            {
                info.Text = "Bitte ein Passwort eingeben.";
                return;
            }

            var values = GeraetEinstellungen.Load();
            values["pause_password"] = password.Text;
            GeraetEinstellungen.Save(values);
            password.Text = "";
            info.Text = "Passwort gespeichert.";
        };
        var start = FahrbetriebPages.Touch("Pause beginnen");
        start.Click += (_, _) =>
        {
            var expected = GeraetEinstellungen.Text("pause_password", "");
            if (expected.Length == 0 || !string.Equals(expected, password.Text, StringComparison.Ordinal))
            {
                info.Text = "Passwort stimmt nicht.";
                return;
            }

            FahrBetrieb.Paused = true;
            password.Text = "";
            refresh();
        };
        var end = FahrbetriebPages.Touch("Pause beenden");
        end.Click += (_, _) =>
        {
            var expected = GeraetEinstellungen.Text("pause_password", "");
            if (!string.Equals(expected, password.Text, StringComparison.Ordinal))
            {
                info.Text = "Passwort stimmt nicht.";
                return;
            }

            FahrBetrieb.Paused = false;
            password.Text = "";
            refresh();
        };
        return FahrbetriebPages.Column("Pause", password, set, start, end, info);
    }

    public static Control Anleitung() => FahrbetriebPages.Column(
        "Bedienungsanleitung",
        FahrbetriebPages.Info(
            "Anmelden, Route wählen, Fahrt starten. Die Ansage kommt im Radius der nächsten Haltestelle. " +
            "Innen links, außen rechts. Die Leitstelle ruft über VoIP an, die Verbindung öffnet sich von selbst. " +
            "Dropbox, Ansagen, Anzeigen und der Entwerter liegen in den weiteren Seiten. " +
            "Pause sperrt die anderen Seiten, bis das Pausenpasswort erneut eingegeben wird."));

    public static Control Protokoll() => FahrbetriebPages.Column(
        "Protokoll",
        new TextBox
        {
            Text = Tail(FahrProtokoll.Read()),
            IsReadOnly = true,
            AcceptsReturn = true,
            MinHeight = 420,
            FontSize = 14
        });

    public static Control Impressum(FahrSession session)
    {
        var company = FahrbetriebPages.Field("Unternehmen");
        company.Text = GeraetEinstellungen.Text("company_name", "");
        var text = new TextBox { AcceptsReturn = true, MinHeight = 160, FontSize = 16 };
        text.Text = GeraetEinstellungen.Text("impressum_text", "");
        var save = FahrbetriebPages.Touch("Speichern");
        var info = FahrbetriebPages.Info("");
        save.Click += (_, _) =>
        {
            var values = GeraetEinstellungen.Load();
            values["company_name"] = company.Text ?? "";
            values["impressum_text"] = text.Text ?? "";
            GeraetEinstellungen.Save(values);
            info.Text = "Gespeichert.";
        };
        return FahrbetriebPages.Column("Impressum", company, text, save, info);
    }

    public static Control Update()
    {
        var info = FahrbetriebPages.Info("Auf dem Raspberry Pi installiert eine neue fahrgeraet-linux.zip in Dropbox von selbst. Der Taster installiert sofort.");
        var check = FahrbetriebPages.Touch("Update installieren");
        check.Click += async (_, _) =>
        {
            var client = FahrbetriebPages.OpenDropbox();
            if (client is null)
            {
                info.Text = "Dropbox ist nicht verbunden.";
                return;
            }

            info.Text = "Lade fahrgeraet-linux.zip…";
            try
            {
                var meta = await client.TryGetNamedFileMetadataAsync(FahrUpdate.PackageName);
                var bytes = await client.TryDownloadNamedBinaryFileAsync(FahrUpdate.PackageName);
                if (bytes is null || bytes.Length == 0)
                {
                    info.Text = "fahrgeraet-linux.zip fehlt in Dropbox.";
                    return;
                }

                if (meta is not null)
                {
                    FahrUpdate.Remember(FahrUpdate.Identity(meta.Value));
                }

                FahrUpdate.Install(bytes);
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        return FahrbetriebPages.Column("App-Update", check, info);
    }

    public static Control Bluetooth()
    {
        var mac = FahrbetriebPages.Field("Bluetooth-Adresse für die Ansagen");
        mac.Text = GeraetEinstellungen.Text("bluetooth_device", "");
        var force = FahrbetriebPages.Check("Zwangskopplung", GeraetEinstellungen.Bool("bluetooth_force_pairing", true));
        var info = FahrbetriebPages.Info(
            "Nur Haltestellenansagen und Sonderansagen laufen über Bluetooth in den Fahrgastraum. Links ist innen, rechts ist außen. " +
            "Stempel, Abfahrt, Tastentöne, IVU-Töne und der Funk laufen über die Lautsprecher des Displays. " +
            TonAusgang.Describe());
        var save = FahrbetriebPages.Touch("Speichern");
        save.Click += (_, _) =>
        {
            var values = GeraetEinstellungen.Load();
            values["bluetooth_device"] = mac.Text ?? "";
            values["bluetooth_force_pairing"] = force.IsChecked == true ? "true" : "false";
            GeraetEinstellungen.Save(values);
            info.Text = "Gespeichert. " + TonAusgang.Describe();
        };
        var probeInside = FahrbetriebPages.Touch("Innen prüfen");
        probeInside.Click += (_, _) =>
        {
            StereoWav.Play(TastenTon.Sample(), true);
            info.Text = "Links, innen. " + TonAusgang.Describe();
        };
        var probeOutside = FahrbetriebPages.Touch("Außen prüfen");
        probeOutside.Click += (_, _) =>
        {
            StereoWav.Play(TastenTon.Sample(), false);
            info.Text = "Rechts, außen. " + TonAusgang.Describe();
        };
        var probeDisplay = FahrbetriebPages.Touch("Display prüfen");
        probeDisplay.Click += (_, _) => info.Text = TonAusgang.Describe();
        return FahrbetriebPages.Column("Bluetooth", mac, force, save, probeInside, probeOutside, probeDisplay, info);
    }

    private static ListBox Routes(FahrSession session)
    {
        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        FillRoutes(session, list);
        return list;
    }

    private static void FillRoutes(FahrSession session, ListBox list)
    {
        list.Items.Clear();
        if (session.Package is null)
        {
            return;
        }

        foreach (var name in session.Package.RouteNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            list.Items.Add(name);
        }
    }

    private static void SavePackage(FahrSession session)
    {
        if (session.Package is null)
        {
            return;
        }

        var path = Path.Combine(session.WorkspaceDir, "routes_local.json");
        File.WriteAllText(path, session.Package.ToJson(false, false, true));
    }

    private static async Task<string?> Pick(Control anchor, bool save, string pattern)
    {
        var window = TopLevel.GetTopLevel(anchor) as Window;
        if (window is null)
        {
            return null;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Datei") { Patterns = [pattern] }]
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private static async Task SaveJson(Control anchor, FahrSession session, string? json, string fileName, TextBlock info)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            info.Text = "Nichts zu exportieren.";
            return;
        }

        var window = TopLevel.GetTopLevel(anchor) as Window;
        var file = window is null
            ? null
            : await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = fileName });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        await File.WriteAllTextAsync(path, json);
        info.Text = path;
    }

    private static void Open(string url)
    {
        if (OperatingSystem.IsLinux())
        {
            Process.Start("xdg-open", url);
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string Tail(string text)
    {
        var lines = text.Split('\n');
        return string.Join('\n', lines.TakeLast(80));
    }
}

public sealed class AnsageRecorder
{
    private WaveInEvent? _wave;
    private WaveFileWriter? _writer;
    private Process? _linux;
    private string? _path;

    public void Start()
    {
        Stop();
        _path = Path.Combine(Path.GetTempPath(), "smartoepnv-ansage.wav");
        if (OperatingSystem.IsWindows())
        {
            _wave = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1) };
            _writer = new WaveFileWriter(_path, _wave.WaveFormat);
            _wave.DataAvailable += (_, e) => _writer?.Write(e.Buffer, 0, e.BytesRecorded);
            _wave.StartRecording();
            return;
        }

        _linux = Process.Start(new ProcessStartInfo("arecord", $"-f S16_LE -r 16000 -c 1 \"{_path}\"")
        {
            RedirectStandardError = true
        });
    }

    public string? Stop()
    {
        _wave?.StopRecording();
        _writer?.Dispose();
        _wave?.Dispose();
        _wave = null;
        _writer = null;
        if (_linux is not null && !_linux.HasExited)
        {
            _linux.Kill(true);
            _linux.WaitForExit(1000);
        }

        _linux = null;
        return File.Exists(_path) ? _path : null;
    }
}

public static class VrrAbfahrten
{
    private static readonly object CacheGate = new();
    private static List<Departure> _cached = [];

    internal readonly record struct Departure(string Line, string Destination, string Time, string Delay);

    public static async Task RefreshAsync(string stopId)
    {
        var rows = await LoadDeparturesAsync(stopId);
        lock (CacheGate)
        {
            _cached = rows;
        }
    }

    public static string ConnectionXml(string stamp)
    {
        List<Departure> copy;
        lock (CacheGate)
        {
            copy = _cached.ToList();
        }

        var body = string.Join("\n", copy.Select(row =>
        {
            var delay = row.Delay.Length == 0
                ? ""
                : $"<Delay><Value>{Xml(row.Delay)}</Value><Language>de</Language></Delay>";
            return $"""
                <CurrentConnection>
                  <LineName><Value>{Xml(row.Line)}</Value><Language>de</Language></LineName>
                  <DestinationName><Value>{Xml(row.Destination)}</Value><Language>de</Language></DestinationName>
                  <ExpectedDepartureTime><Value>{Xml(row.Time)}</Value><Language>de</Language></ExpectedDepartureTime>
                  {delay}
                </CurrentConnection>
                """;
        }));
        return $"<CurrentConnectionData>{stamp}{body}</CurrentConnectionData>";
    }

    public static async Task<IReadOnlyList<string>> LoadAsync(string stopId)
    {
        if (string.IsNullOrWhiteSpace(stopId))
        {
            return ["Haltestellennummer fehlt."];
        }

        var rows = await LoadDeparturesAsync(stopId);
        if (rows.Count > 0)
        {
            lock (CacheGate)
            {
                _cached = rows;
            }
        }

        return rows.Count == 0
            ? ["Keine Abfahrten."]
            : rows.Select(row => $"{row.Line}  {row.Destination}  {row.Time}  {row.Delay}".Trim()).ToList();
    }

    private static async Task<List<Departure>> LoadDeparturesAsync(string stopId)
    {
        var id = stopId.Trim();
        if (id.Length == 0)
        {
            return [];
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        var rows = await RequestAsync(http, id, "stop");
        return rows.Count > 0 ? rows : await RequestAsync(http, id, "any");
    }

    private static async Task<List<Departure>> RequestAsync(HttpClient http, string id, string type)
    {
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["language"] = "de",
            ["mode"] = "direct",
            ["outputFormat"] = "rapidJSON",
            ["type_dm"] = type,
            ["name_dm"] = id,
            ["useRealtime"] = "1",
            ["limit"] = "12"
        });
        using var response = await http.PostAsync("https://efa.vrr.de/vrr/XML_DM_REQUEST", body);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var rows = new List<Departure>();
        if (!doc.RootElement.TryGetProperty("stopEvents", out var events))
        {
            return rows;
        }

        foreach (var ev in events.EnumerateArray())
        {
            var transport = ev.TryGetProperty("transportation", out var t) ? t : default;
            var line = Text(transport, "disassembledName") ?? Text(transport, "number") ?? "";
            var destination = transport.ValueKind == JsonValueKind.Object &&
                              transport.TryGetProperty("destination", out var dest)
                ? Text(dest, "name") ?? ""
                : "";
            var planned = Text(ev, "departureTimePlanned");
            var live = Text(ev, "departureTimeEstimated");
            rows.Add(new Departure(
                line,
                destination,
                Clock(live) ?? Clock(planned) ?? "",
                DelayLabel(planned, live)));
        }

        return rows;
    }

    private static string Xml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? Clock(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso) || !DateTimeOffset.TryParse(iso, out var time))
        {
            return null;
        }

        return time.ToLocalTime().ToString("HH:mm");
    }

    /// <summary>Minuten gegen den Fahrplan. +1 später, -2 früher, +0 pünktlich. Ohne Echtzeit leer.</summary>
    internal static string DelayLabel(string? plannedIso, string? liveIso)
    {
        if (string.IsNullOrWhiteSpace(plannedIso) || string.IsNullOrWhiteSpace(liveIso))
        {
            return "";
        }

        if (!DateTimeOffset.TryParse(plannedIso, out var planned) ||
            !DateTimeOffset.TryParse(liveIso, out var live))
        {
            return "";
        }

        var minutes = (int)Math.Round((live - planned).TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes > 0)
        {
            return "+" + minutes;
        }

        return minutes < 0 ? minutes.ToString() : "+0";
    }
}

public static class SimplePdf
{
    public static byte[] Create(IReadOnlyList<string> lines)
    {
        var commands = new StringBuilder("BT /F1 12 Tf 48 800 Td\n");
        foreach (var line in lines.Take(45))
        {
            commands.Append('(').Append(Escape(line)).Append(") Tj\n0 -16 Td\n");
        }

        commands.Append("ET");
        var stream = Encoding.ASCII.GetBytes(commands.ToString());
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {stream.Length} >>\nstream\n{Encoding.ASCII.GetString(stream)}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        builder.Append("trailer<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string Escape(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            if (ch is '(' or ')' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(ch is >= ' ' and <= '~' ? ch : ' ');
        }

        return builder.ToString();
    }
}

public static class FahrUpdate
{
    public const string PackageName = "fahrgeraet-linux.zip";

    private static int _started;
    private static int _installing;

    public static void Start()
    {
        if (!OperatingSystem.IsLinux() || Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _ = Task.Run(Watch);
    }

    public static string Identity(DropboxNamedFileMetadata meta) =>
        !string.IsNullOrWhiteSpace(meta.ContentHash)
            ? meta.ContentHash
            : meta.SizeBytes + ":" + (meta.ServerModifiedUtcMs ?? 0);

    public static void Remember(string id)
    {
        if (id.Length == 0)
        {
            return;
        }

        var values = GeraetEinstellungen.Load();
        values["fahrgeraet_update_id"] = id;
        values["fahrgeraet_update_armed"] = "false";
        GeraetEinstellungen.Save(values);
    }

    private static async Task Watch()
    {
        while (true)
        {
            try
            {
                await Look();
            }
            catch
            {
                // Dropbox kurz nicht erreichbar
            }

            await Task.Delay(TimeSpan.FromSeconds(20));
        }
    }

    private static async Task Look()
    {
        var client = FahrbetriebPages.OpenDropbox();
        if (client is null)
        {
            return;
        }

        var meta = await client.TryGetNamedFileMetadataAsync(PackageName);
        var saved = GeraetEinstellungen.Text("fahrgeraet_update_id", "");
        if (meta is null || meta.Value.SizeBytes <= 0)
        {
            if (saved.Length == 0 && !GeraetEinstellungen.Bool("fahrgeraet_update_armed", false))
            {
                var values = GeraetEinstellungen.Load();
                values["fahrgeraet_update_armed"] = "true";
                GeraetEinstellungen.Save(values);
            }

            return;
        }

        var id = Identity(meta.Value);
        if (id.Length == 0 || id == saved)
        {
            return;
        }

        if (saved.Length == 0 && !GeraetEinstellungen.Bool("fahrgeraet_update_armed", false))
        {
            Remember(id);
            return;
        }

        var bytes = await client.TryDownloadNamedBinaryFileAsync(PackageName);
        if (bytes is null || bytes.Length != meta.Value.SizeBytes || !ContainsProgram(bytes))
        {
            return;
        }

        FahrProtokoll.Add("Neue fahrgeraet-linux.zip in Dropbox. Installation startet.");
        Remember(id);
        Install(bytes);
    }

    public static void Install(byte[] zip)
    {
        if (Interlocked.Exchange(ref _installing, 1) == 1)
        {
            return;
        }

        if (!ContainsProgram(zip))
        {
            throw new InvalidDataException("fahrgeraet-linux.zip enthält das Fahrgerät nicht.");
        }

        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Der Programmpfad fehlt.");
        var root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var staging = Path.Combine(Path.GetTempPath(), "fahrgeraet-update");
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, true);
        }

        Directory.CreateDirectory(staging);
        var archive = Path.Combine(staging, "paket.zip");
        File.WriteAllBytes(archive, zip);
        var extracted = Path.Combine(staging, "inhalt");
        ExtractSafe(archive, extracted);
        var source = Directory.EnumerateFiles(extracted, "SmartOepnv.Fahrgeraet.dll", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault(path => path is not null) ?? extracted;
        var script = Path.Combine(staging, OperatingSystem.IsWindows() ? "update.cmd" : "update.sh");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(script, $"@echo off\r\ntimeout /t 2 /nobreak >nul\r\nxcopy /E /Y \"{source}\\*\" \"{root}\\\"\r\nstart \"\" \"{current}\"\r\n");
            Process.Start(new ProcessStartInfo(script) { UseShellExecute = true, CreateNoWindow = true });
        }
        else
        {
            File.WriteAllText(script, $"#!/bin/sh\nsleep 2\ncp -a \"{source}/.\" \"{root}/\"\nchmod +x \"{current}\"\n\"{current}\" &\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Process.Start(new ProcessStartInfo(script) { UseShellExecute = false });
        }

        Environment.Exit(0);
    }

    private static bool ContainsProgram(byte[] zip)
    {
        try
        {
            using var stream = new MemoryStream(zip);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            return archive.Entries.Any(entry => entry.Name.Equals("SmartOepnv.Fahrgeraet.dll", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static void ExtractSafe(string archive, string extracted)
    {
        Directory.CreateDirectory(extracted);
        var root = Path.GetFullPath(extracted) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(extracted, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Das Update-Paket enthält einen ungültigen Pfad.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }
}
