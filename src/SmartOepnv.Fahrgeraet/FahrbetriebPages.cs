using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Layout;
using Avalonia.Media;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Verkauf;
using System.Text.Json.Nodes;

namespace SmartOepnv.Fahrgeraet;

public static class FahrbetriebPages
{
    public static Control Login(FahrSession session, Action onLoggedIn)
    {
        var personnel = Field("Personalnummer");
        var password = Field("Passwort");
        password.PasswordChar = '•';
        var phone = Field("Gerätetelefon");
        phone.Text = session.DevicePhone;
        var info = Info();
        var button = Touch("Anmelden");
        button.Click += (_, _) =>
        {
            if (session.Package is null)
            {
                info.Text = "Zuerst das Routenpaket laden.";
                return;
            }

            var pn = Digits(personnel.Text);
            var match = session.Package.Employees.FirstOrDefault(e =>
                Digits(e.PersonnelNumber) == pn &&
                string.Equals(e.Password, password.Text ?? "", StringComparison.Ordinal));
            if (match is null)
            {
                info.Text = "Anmeldung abgelehnt.";
                return;
            }

            session.Employee = match;
            session.DevicePhone = Digits(phone.Text);
            session.SaveDevice();
            _ = session.Funk.ConnectAsync(session.DevicePhone);
            info.Text = match.Name;
            onLoggedIn();
        };
        return Column("Anmeldung", personnel, password, phone, button, info);
    }

    public static Control Itcs(FahrSession session, FahrgeraetRouteSync sync, Action refresh)
    {
        var list = new ListBox { FontSize = 18, Foreground = Brushes.White, Background = Brushes.Transparent };
        var info = Info();
        void ShowRoutes()
        {
            list.Items.Clear();
            if (session.Package is null)
            {
                list.Items.Add("Kein Routenpaket.");
                return;
            }

            foreach (var name in session.Package.RouteNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                list.Items.Add(name);
            }
        }

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is string route)
            {
                session.RouteName = route;
                session.Trip.Reset();
                info.Text = route;
                refresh();
            }
        };
        var open = Touch("Routenpaket öffnen");
        open.Click += async (_, _) =>
        {
            var window = TopLevel.GetTopLevel(list) as Window;
            if (window is null)
            {
                return;
            }

            var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });
            var path = files.Count == 0 ? null : files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            session.Package = EditableRoutePackage.FromJson(await File.ReadAllTextAsync(path));
            ShowRoutes();
            refresh();
        };
        var dropbox = Touch("Dropbox abgleichen");
        dropbox.Click += async (_, _) =>
        {
            info.Text = "Dropbox…";
            var result = await sync.SyncAsync();
            info.Text = result.Message;
            if (result.Package is not null)
            {
                session.Package = result.Package;
                ShowRoutes();
                refresh();
            }
        };
        ShowRoutes();
        return ItcsPause.Cover(session, Column("Route wählen", open, dropbox, info, list));
    }

    public static Control Gps(FahrSession session)
    {
        var port = Field("GPS-Anschluss");
        port.Text = session.GpsPort;
        var info = Info();
        GpsNmea? nmea = null;
        var start = Touch("GPS starten");
        start.Click += (_, _) =>
        {
            session.GpsPort = port.Text?.Trim() ?? "";
            session.SaveDevice();
            nmea?.Dispose();
            nmea = new GpsNmea();
            nmea.Fix += (lat, lon) =>
            {
                var text = $"{lat:0.00000}, {lon:0.00000}";
                if (session.Package is not null &&
                    session.RouteName is not null &&
                    session.Package.StopsByRoute.TryGetValue(session.RouteName, out var stops))
                {
                    var near = StopMatch.Nearest(stops, lat, lon);
                    if (near is not null)
                    {
                        var radius = near.Value.Stop.Radius <= 0 ? 50 : near.Value.Stop.Radius;
                        var reached = near.Value.Meters <= radius ? "erreicht" : $"{near.Value.Meters:0} m";
                        text += $"\n{near.Value.Stop.Name} · {reached}";
                    }
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() => info.Text = text);
            };
            try
            {
                nmea.Start(session.GpsPort);
                info.Text = "Warte auf NMEA.";
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        var stop = Touch("GPS stoppen");
        stop.Click += (_, _) =>
        {
            nmea?.Stop();
            info.Text = "GPS gestoppt.";
        };
        return Column("GPS-Fahrt", port, start, stop, info);
    }

    public static Control Ansagen(FahrSession session)
    {
        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        var templates = session.Package?.AnnouncementTemplates ?? [];
        foreach (var item in templates)
        {
            list.Items.Add(item);
        }

        list.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ManagedAnnouncementTemplateItem.DisplayName));
        var info = Info();
        void Play(bool inside)
        {
            if (list.SelectedItem is not ManagedAnnouncementTemplateItem item || session.Package is null)
            {
                info.Text = "Ansage wählen.";
                return;
            }

            var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(session.Package.PackageRoot);
            var key = sounds.Keys.FirstOrDefault(k => string.Equals(k, item.EmbeddedSoundFileName, StringComparison.OrdinalIgnoreCase));
            if (key is null)
            {
                info.Text = string.IsNullOrWhiteSpace(item.IbisAnnouncementId)
                    ? "Keine Tondatei im Paket."
                    : $"IBIS {item.IbisAnnouncementId}, keine Tondatei im Paket.";
                return;
            }

            try
            {
                if (GeraetEinstellungen.Text("announcement_delivery", "bluetooth") == "ibis")
                {
                    var id = IbisAnnouncementIds.Normalize(item.IbisAnnouncementId);
                    if (id.Length == 0)
                    {
                        info.Text = "Keine IBIS-Ansagenummer.";
                        return;
                    }

                    VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("hP" + id), 9600);
                    info.Text = $"IBIS DS036 {id}";
                    return;
                }

                var bytes = Convert.FromBase64String(sounds[key].Base64);
                StereoWav.Play(bytes, inside);
                info.Text = inside ? "Innen, links, Bluetooth." : "Außen, rechts, Bluetooth.";
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        }

        var innen = Touch("Innen abspielen");
        innen.Click += (_, _) => Play(true);
        var aussen = Touch("Außen abspielen");
        aussen.Click += (_, _) => Play(false);
        return Column("Ansagen", innen, aussen, info, list);
    }

    public static Control Ziel(FahrSession session)
    {
        var line = Field("Linie");
        var destination = Field("Ziel");
        var stop = Field("Haltestelle innen");
        if (session.Package is not null &&
            session.RouteName is not null &&
            session.Package.StopsByRoute.TryGetValue(session.RouteName, out var stops))
        {
            line.Text = stops.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.LineNumber))?.LineNumber ?? "";
            destination.Text = stops.LastOrDefault()?.Destination ?? "";
            stop.Text = stops.FirstOrDefault()?.Name ?? "";
        }

        var outside = Choice("Außenprotokoll", DisplayCatalog.Outside, session.OutsideProtocol);
        var interior = Choice("Innenprotokoll", DisplayCatalog.Interior, session.InteriorProtocol);
        var control = Choice("Anzeigensteuerung", ["Klartext", "SICMA"], session.DisplayControl);
        var ibisMode = Choice("IBIS-IP", ["HTTP", "TCP"], session.IbisIpMode);
        var ds009Length = Choice("DS009 Länge", ["16", "20"], session.Ds009Length.ToString());
        var onlyStop = Check("DS009 nur Haltestelle", session.Ds009OnlyStop);
        var ds009Extra = Check("DS009 zusätzlich", session.Ds009Additional);
        var verlauf = Check("DS003c Verlauf", session.Ds003cVerlauf);
        var tftTime = Check("TFT Zeit", session.TftTime);
        var tft2030 = Check("TFT Datumstest 2030", session.TftDate2030);
        if (OperatingSystem.IsLinux())
        {
            LinuxUsbBus.Apply(session);
        }

        var ibis = Field("IBIS-Anschluss");
        ibis.Text = session.IbisPort;
        var rs = Field("RS485-Anschluss");
        rs.Text = session.Rs485Port;
        var info = Info();
        var swapPorts = Touch("IBIS und RS485 tauschen");
        swapPorts.IsVisible = OperatingSystem.IsLinux();
        swapPorts.Click += (_, _) =>
        {
            LinuxUsbBus.Toggle(session);
            ibis.Text = session.IbisPort;
            rs.Text = session.Rs485Port;
            info.Text = $"IBIS {session.IbisPort} · RS485 {session.Rs485Port}";
        };
        var ibisUrl = Field("IBIS-IP URL");
        ibisUrl.Text = session.IbisIpUrl;
        var ibisHost = Field("IBIS-IP Host");
        ibisHost.Text = session.IbisIpHost;
        var ibisPort = Field("IBIS-IP Port");
        ibisPort.Text = session.IbisIpPort.ToString();
        var listenPort = Field("IBIS-IP Lauschport");
        listenPort.Text = session.IbisIpListenPort.ToString();
        var sws = Field("TFT-SWS-Adresse");
        sws.Text = session.TftSwsAddress.ToString();
        var bustec = Field("Bustec-Adresse");
        bustec.Text = session.BustecAddress.ToString();
        var annax = Field("ANNAX-Adresse");
        annax.Text = session.AnnaxAddress.ToString();
        var gorba = Field("Gorba-Adresse");
        gorba.Text = session.GorbaAddress.ToString();
        var send = Touch("Senden");
        send.Click += async (_, _) =>
        {
            session.OutsideProtocol = Selected(outside, session.OutsideProtocol);
            session.InteriorProtocol = Selected(interior, session.InteriorProtocol);
            session.DisplayControl = Selected(control, session.DisplayControl);
            session.IbisIpMode = Selected(ibisMode, session.IbisIpMode);
            session.Ds009Length = Selected(ds009Length, "20") == "16" ? 16 : 20;
            session.Ds009OnlyStop = onlyStop.IsChecked == true;
            session.Ds009Additional = ds009Extra.IsChecked == true;
            session.Ds003cVerlauf = verlauf.IsChecked == true;
            session.TftTime = tftTime.IsChecked == true;
            session.TftDate2030 = tft2030.IsChecked == true;
            if (OperatingSystem.IsLinux())
            {
                LinuxUsbBus.Apply(session);
                ibis.Text = session.IbisPort;
                rs.Text = session.Rs485Port;
            }

            session.IbisPort = ibis.Text?.Trim() ?? "";
            session.Rs485Port = rs.Text?.Trim() ?? "";
            session.IbisIpUrl = ibisUrl.Text?.Trim() ?? "";
            session.IbisIpHost = ibisHost.Text?.Trim() ?? "";
            session.IbisIpPort = int.TryParse(ibisPort.Text, out var port) ? port : 8080;
            session.IbisIpListenPort = int.TryParse(listenPort.Text, out var listen) ? listen : 8080;
            session.TftSwsAddress = int.TryParse(sws.Text, out var a1) ? a1 : 1;
            session.BustecAddress = int.TryParse(bustec.Text, out var a2) ? a2 : 1;
            session.AnnaxAddress = int.TryParse(annax.Text, out var a3) ? a3 : 1;
            session.GorbaAddress = int.TryParse(gorba.Text, out var a4) ? a4 : 1;
            session.SaveDevice();
            GeraetEinstellungen.CopyFromSession(session);
            try
            {
                info.Text = await DisplayTelegrams.SendAsync(session, line.Text ?? "", destination.Text ?? "", stop.Text ?? "");
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        return Column(
            "Ziel, IBIS, RS485",
            line, destination, stop, outside, interior, control, ds009Length, onlyStop, ds009Extra, verlauf,
            tftTime, tft2030, sws, bustec, annax, gorba, ibis, rs, swapPorts, ibisMode, ibisUrl, ibisHost, ibisPort, listenPort, send, info);
    }

    public static Control Voip(FahrSession session)
    {
        var voip = session.Funk;
        var info = Info(voip.IncomingFrom is null ? voip.Status : $"{voip.Status}\n{voip.IncomingFrom}");
        voip.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            info.Text = voip.IncomingFrom is null ? voip.Status : $"{voip.Status}\n{voip.IncomingFrom}");
        _ = voip.ConnectAsync(session.DevicePhone);
        return Column(
            "VoIP",
            Info("Die Leitstelle ruft an. Die Sprechverbindung öffnet sich von selbst und wird nur von der Leitstelle beendet."),
            info);
    }

    public static Control Zeitwirtschaft(FahrSession session)
    {
        var info = Info();
        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        var path = ZeitPath(session);
        void Refresh()
        {
            list.Items.Clear();
            if (!File.Exists(path))
            {
                return;
            }

            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject();
            var pn = session.Employee?.PersonnelNumber ?? "";
            if (root?["drivers"]?[pn]?["entries"] is JsonArray entries)
            {
                foreach (var node in entries.OfType<JsonObject>())
                {
                    var start = node["startEpochMs"]?.GetValue<long>() ?? 0;
                    var end = node["endEpochMs"]?.GetValue<long?>();
                    list.Items.Add($"{Clock(start)} – {(end is null or 0 ? "offen" : Clock(end.Value))}");
                }
            }
        }

        void Mutate(Action<JsonObject, JsonArray> change)
        {
            if (session.Employee is null || string.IsNullOrWhiteSpace(session.DevicePhone))
            {
                info.Text = "Anmeldung und Gerätetelefon fehlen.";
                return;
            }

            var root = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? NewZeit(session)
                : NewZeit(session);
            var pn = session.Employee.PersonnelNumber;
            if (root["drivers"] is not JsonObject drivers)
            {
                drivers = new JsonObject();
                root["drivers"] = drivers;
            }

            if (drivers[pn] is not JsonObject driver)
            {
                driver = new JsonObject
                {
                    ["personnelNumber"] = pn,
                    ["name"] = session.Employee.Name,
                    ["entries"] = new JsonArray()
                };
                drivers[pn] = driver;
            }

            var entries = driver["entries"] as JsonArray ?? new JsonArray();
            driver["entries"] = entries;
            change(root, entries);
            root["updatedAtMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            root["devicePhone"] = session.DevicePhone;
            File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Refresh();
        }

        var begin = Touch("Arbeitsbeginn");
        begin.Click += (_, _) => Mutate((_, entries) =>
        {
            if (OpenShift(entries) is not null)
            {
                info.Text = "Arbeitszeit läuft bereits.";
                return;
            }

            entries.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["startEpochMs"] = Now(),
                ["recordedOnDevice"] = session.DevicePhone,
                ["pauses"] = new JsonArray()
            });
            info.Text = "Arbeitsbeginn gespeichert.";
        });
        var pause = Touch("Pause");
        pause.Click += (_, _) => Mutate((_, entries) =>
        {
            var open = OpenShift(entries);
            if (open is null)
            {
                info.Text = "Keine offene Schicht.";
                return;
            }

            var pauses = open["pauses"] as JsonArray ?? new JsonArray();
            open["pauses"] = pauses;
            var running = pauses.OfType<JsonObject>().LastOrDefault(p => (p["endEpochMs"]?.GetValue<long>() ?? 0) == 0);
            if (running is null)
            {
                pauses.Add(new JsonObject { ["startEpochMs"] = Now() });
                info.Text = "Pause begonnen.";
            }
            else
            {
                running["endEpochMs"] = Now();
                info.Text = "Pause beendet.";
            }
        });
        var end = Touch("Arbeitsende");
        end.Click += (_, _) => Mutate((_, entries) =>
        {
            var open = OpenShift(entries);
            if (open is null)
            {
                info.Text = "Keine offene Schicht.";
                return;
            }

            open["endEpochMs"] = Now();
            info.Text = "Arbeitsende gespeichert.";
        });
        var upload = Touch("Nach Dropbox");
        upload.Click += async (_, _) =>
        {
            var client = OpenDropbox();
            if (client is null || !File.Exists(path))
            {
                info.Text = "Dropbox oder Einträge fehlen.";
                return;
            }

            var name = $"zeitwirtschaft_{Digits(session.DevicePhone)}.json";
            await client.UploadNamedFileAsync(name, await File.ReadAllTextAsync(path));
            info.Text = name;
        };
        Refresh();
        return Column("Zeitwirtschaft", begin, pause, end, upload, info, list);
    }

    internal static JsonObject? OpenShift(JsonArray entries)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i] is not JsonObject entry || entry["voided"] is JsonValue { } voided &&
                voided.TryGetValue<bool>(out var isVoided) && isVoided)
            {
                continue;
            }

            var correctedEnd = LongOrZero(entry, "correctedEndEpochMs");
            var end = LongOrZero(entry, "endEpochMs");
            if (correctedEnd <= 0 && end <= 0)
            {
                return entry;
            }
        }

        return null;
    }

    private static long LongOrZero(JsonObject entry, string name) =>
        entry[name] is JsonValue value && value.TryGetValue<long>(out var number) ? number : 0;

    public static Control Verkauf(FahrSession session)
    {
        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        foreach (var product in session.Package?.TicketProducts.Where(p => p.Active) ?? [])
        {
            list.Items.Add(product);
        }

        list.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(TicketProductItem.Name));
        var info = Info();
        var sell = Touch("Drucken und buchen");
        sell.Click += async (_, _) =>
        {
            if (session.Employee is null || list.SelectedItem is not TicketProductItem product)
            {
                info.Text = "Anmeldung und Ticket wählen.";
                return;
            }

            var fileName = VerkaufDocumentParser.BuildFileName(session.Employee.PersonnelNumber);
            var path = Path.Combine(session.WorkspaceDir, fileName);
            var existing = File.Exists(path)
                ? VerkaufDocumentParser.TryParse(fileName, await File.ReadAllTextAsync(path))
                : null;
            if (existing?.IsPeriodExpired == true)
            {
                info.Text = "Verkauf gesperrt. Freischaltung im Planer.";
                return;
            }

            var now = Now();
            var root = existing is null
                ? JsonNode.Parse(VerkaufDocumentParser.BuildEmptyJson(session.Employee.PersonnelNumber, session.Employee.Name))!.AsObject()
                : JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            var sales = root["sales"] as JsonArray ?? new JsonArray();
            root["sales"] = sales;
            var cents = product.PriceCents;
            sales.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString("N"),
                ["soldAtMs"] = now,
                ["soldAtIso"] = DateTimeOffset.Now.ToString("o"),
                ["vehiclePhone"] = session.DevicePhone,
                ["totalCents"] = cents,
                ["voided"] = false,
                ["lines"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["productId"] = product.Id,
                        ["name"] = product.Name,
                        ["unitPriceCents"] = cents,
                        ["qty"] = 1
                    }
                }
            });
            root["updatedAtMs"] = now;
            if (root["firstSaleAtMs"] is null)
            {
                root["firstSaleAtMs"] = now;
            }

            await File.WriteAllTextAsync(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var client = OpenDropbox();
            if (client is not null)
            {
                await client.UploadNamedFileAsync(fileName, await File.ReadAllTextAsync(path));
            }

            info.Text = $"{product.Name} · {cents / 100m:0.00} €";
        };
        return Column("Verkauf", sell, info, list);
    }

    public static Control Postfach(FahrSession session)
    {
        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        var info = Info();
        var load = Touch("Postfach laden");
        load.Click += async (_, _) =>
        {
            var client = OpenDropbox();
            if (client is null)
            {
                info.Text = "Dropbox ist nicht verbunden.";
                return;
            }

            list.Items.Clear();
            var mail = await client.ListMailAndSosChatFilesAsync();
            var zbl = await client.ListZblMessageFilesAsync();
            foreach (var name in mail.Concat(zbl).Take(40))
            {
                var json = await client.TryDownloadNamedFileAsync(name);
                list.Items.Add(string.IsNullOrWhiteSpace(json) ? name : $"{name}\n{Excerpt(json)}");
            }

            info.Text = $"{list.Items.Count} Nachrichten";
        };
        return Column("Postfach", load, info, list);
    }

    internal static JsonObject NewZeit(FahrSession session) => new()
    {
        ["updatedAtMs"] = Now(),
        ["devicePhone"] = session.DevicePhone,
        ["drivers"] = new JsonObject()
    };

    internal static string ZeitPath(FahrSession session) =>
        Path.Combine(session.WorkspaceDir, $"zeitwirtschaft_{Digits(session.DevicePhone)}.json");

    internal static DropboxApiClient? OpenDropbox()
    {
        foreach (var folder in new[] { FahrgeraetRouteSync.AppFolder, "Planer" })
        {
            var store = new DropboxSettingsStore(folder);
            if (store.Load().IsConnected)
            {
                return new DropboxApiClient(store);
            }
        }

        return null;
    }

    private static string Excerpt(string json)
    {
        try
        {
            var root = JsonNode.Parse(json)?.AsObject();
            if (root is null)
            {
                return json[..Math.Min(180, json.Length)];
            }

            foreach (var key in new[] { "text", "message", "body", "subject" })
            {
                var value = root[key]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch
        {
            // Rohtext
        }

        return json[..Math.Min(180, json.Length)];
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Clock(long epoch) =>
        DateTimeOffset.FromUnixTimeMilliseconds(epoch).ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    internal static string Digits(string? raw) => new((raw ?? "").Where(char.IsDigit).ToArray());

    internal static TextBox Field(string watermark) => new()
    {
        Watermark = watermark,
        FontSize = 18,
        MinHeight = 48
    };

    internal static TextBlock Info(string text = "") => new()
    {
        Text = text,
        Foreground = Brushes.White,
        FontSize = 16,
        TextWrapping = TextWrapping.Wrap
    };

    internal static Control Choice(string caption, IEnumerable<string> items, string selected)
    {
        var box = new ComboBox
        {
            ItemsSource = items.ToList(),
            SelectedItem = selected,
            MinHeight = 48,
            FontSize = 16
        };
        var panel = new StackPanel { Spacing = 2, Tag = box };
        panel.Children.Add(new TextBlock
        {
            Text = caption,
            Foreground = Brushes.White,
            FontSize = 14
        });
        panel.Children.Add(box);
        return panel;
    }

    internal static string Selected(Control choice, string fallback) =>
        (choice.Tag as ComboBox)?.SelectedItem as string ?? fallback;

    internal static CheckBox Check(string caption, bool value) => new()
    {
        Content = caption,
        IsChecked = value,
        Foreground = Brushes.White,
        FontSize = 16
    };

    internal static Button Touch(string caption)
    {
        var button = new Button
        {
            Content = caption,
            Classes = { "touch" }
        };
        button.Click += (_, _) => TastenTon.Play();
        return button;
    }

    internal static Control Column(string title, params Control[] children)
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(8) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeight.Bold
        });
        foreach (var child in children)
        {
            if (child is ListBox)
            {
                child.Height = 280;
            }

            panel.Children.Add(child);
        }

        return new ScrollViewer { Content = panel };
    }
}
