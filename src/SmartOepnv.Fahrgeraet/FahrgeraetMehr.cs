using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.Maengelkarte;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.Fahrgeraet;

public static class FahrgeraetMehr
{
    public static Control Einstellungen(FahrSession session, Action? openDiagnose = null)
    {
        if (OperatingSystem.IsLinux())
        {
            LinuxUsbBus.Apply(session);
        }

        var values = GeraetEinstellungen.Load();
        GeraetEinstellungen.CopyFromSession(session);
        values = GeraetEinstellungen.Load();
        var boxes = new List<(string Key, Control Input, string Kind)>();
        var children = new List<Control>();
        if (openDiagnose is not null)
        {
            var diagnose = FahrbetriebPages.Touch("Fahrzeugdiagnose");
            diagnose.Click += (_, _) => openDiagnose();
            children.Add(diagnose);
        }
        void Section(string title) => children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 20,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        });

        void Bool(string key, string label, bool fallback)
        {
            var box = FahrbetriebPages.Check(label, GeraetEinstellungen.Bool(values, key, fallback));
            boxes.Add((key, box, "bool"));
            children.Add(box);
        }

        void Text(string key, string label, string fallback)
        {
            var box = FahrbetriebPages.Field(label);
            box.Text = GeraetEinstellungen.Text(values, key, fallback);
            boxes.Add((key, box, "text"));
            children.Add(box);
        }

        void Choice(string key, string label, string fallback, params string[] items)
        {
            var current = GeraetEinstellungen.Text(values, key, fallback);
            var box = FahrbetriebPages.Choice(label, items, items.Contains(current) ? current : fallback);
            boxes.Add((key, box, "choice"));
            children.Add(box);
        }

        Section("Gerät");
        Bool("button_sounds_enabled", "Tastentöne", true);
        Bool("bluetooth_force_pairing", "Bluetooth-Zwangskopplung", false);
        Bool("is_main_device", "Hauptgerät-Modus", false);
        Bool("auto_open_next_route", "Automatische Routen-Öffnung", true);
        Bool("night_mode_enabled", "Nachtmodus", false);
        Bool("museum_verkauf_enabled", "Verkauf aktiv", false);
        Bool("direction_detection_enabled", "Richtungserkennung", true);
        Bool("hide_navigation_bar_enabled", "Status- und Navigationsleiste ausblenden", false);
        Bool("auto_hide_navigation_on_gps", "Leiste automatisch bei GPS-Betrieb ausblenden", false);
        Choice("language_selected_index", "Sprache: 0 Deutsch, 1 Niederländisch, 2 Englisch", "0", "0", "1", "2");
        Text("device_display_name", "Anzeigename", "");
        Text("company_name", "Unternehmername", "");
        Text("device_phone_number", "Gerätetelefon", session.DevicePhone);
        Text("zbl_phone_number", "ZBL-Telefon", "");
        Choice("zbl_contact_mode", "ZBL-Button", "call", "call", "sprechwunsch");

        Section("Sound");
        Bool("announcement_stereo_zones_enabled", "Stereo-Zonen (links innen, rechts außen)", true);
        Choice("announcement_delivery", "Ansagenversand", "bluetooth", "bluetooth", "ibis");
        Choice("fahrgastraum_ptt_gain_step", "Fahrgastraum Verstärkung 0–9", "3", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9");

        Section("Anzeigen");
        Bool("destination_display_enabled", "Zielanzeige", true);
        Bool("line_display_enabled", "Linienanzeige", true);
        Bool("outdoor_display_enabled", "Außenanzeige", true);
        Choice("selected_protocol", "Außenprotokoll", session.OutsideProtocol, DisplayCatalog.Outside);
        Choice("interior_protocol", "Innenprotokoll", session.InteriorProtocol, DisplayCatalog.Interior);
        Choice("ibis_display_control_mode", "Anzeigensteuerung", "DIRECT_CLEARTEXT", "DIRECT_CLEARTEXT", "SICMA_ZD_TARGET_NUMBER");
        Choice("ds009_text_length", "DS009 Länge", session.Ds009Length.ToString(), "16", "20");
        Bool("ds009_only_stop_text", "DS009 nur Haltestelle", session.Ds009OnlyStop);
        Bool("interior_ds009_additional", "DS009 zusätzlich", session.Ds009Additional);
        Bool("interior_ds003c_verlauf", "DS003c Verlauf", session.Ds003cVerlauf);
        Bool("tft_time_enabled", "TFT Zeit", session.TftTime);
        Bool("tft_date_2030_test_enabled", "TFT Datumstest 2030", session.TftDate2030);
        Bool("interior_alt_protocol_enabled", "Alternativprotokoll", false);
        Bool("tft_sws_enabled", "TFT SWS", false);
        Text("tft_sws_display_address", "Adresse TFT SWS", session.TftSwsAddress.ToString());
        Bool("bustec_tft_enabled", "Bustec", false);
        Text("bustec_tft_display_address", "Adresse Bustec", session.BustecAddress.ToString());
        Bool("annax_ibis_enabled", "ANNAX", false);
        Text("annax_ibis_display_address", "Adresse ANNAX", session.AnnaxAddress.ToString());
        Bool("gorba_infomedia_enabled", "Gorba Infomedia", false);
        Text("gorba_infomedia_display_address", "Adresse Gorba", session.GorbaAddress.ToString());
        Bool("tft_lawo_enabled", "TFT Lawo", false);
        Choice("ibis_exterior_transport", "IBIS außen", "usb", "usb", "wlan");
        Choice("ibis_interior_transport", "IBIS innen", "usb", "usb", "wlan");
        Bool("tcp_socket_client_enabled", "IBIS-Bridge als Client", false);
        Choice("tcp_socket_protocol", "Bridge-Protokoll", "TCP", "TCP", "UDP");
        Text("tcp_socket_host", "Bridge-Host", "");
        Text("tcp_socket_port", "Bridge-Port", "5000");
        Choice("tcp_socket_scope", "Bridge-Bereich", "both", "both", "interior", "exterior");
        Bool("tft_tcp_enabled", "TFT über TCP", false);
        Text("tft_tcp_port", "TFT-TCP-Port", "51001");
        Choice("ibis_ip_connection_type", "IBIS-IP", session.IbisIpMode, "HTTP", "TCP");
        Text("ibis_ip_server_url", "IBIS-IP URL", session.IbisIpUrl);
        Text("ibis_ip_server_host", "IBIS-IP Host", session.IbisIpHost);
        Text("ibis_ip_server_port", "IBIS-IP Port", session.IbisIpPort.ToString());
        Text("ibis_ip_listen_port", "IBIS-IP Lauschport", session.IbisIpListenPort.ToString());
        Text("ibis_port", "IBIS-Anschluss", session.IbisPort);
        Text("rs485_port", "RS485-Anschluss", session.Rs485Port);
        Text("gps_port", "GPS-Anschluss", session.GpsPort);

        Section("SEV-TFT");
        Bool("sev_tft_enabled", "SEV-TFT aktiv", false);
        Bool("sev_tft_auto_send", "Beim Öffnen einer Route senden", true);
        Text("sev_tft_host", "SEV-TFT Host", "");
        Text("sev_tft_port", "SEV-TFT Port", "80");
        Text("sev_tft_api_token", "SEV-TFT Token", "");

        Section("Entwerter");
        Bool("itcs_razzia_button_enabled", "Razziabutton", false);

        var info = FahrbetriebPages.Info();
        var save = FahrbetriebPages.Touch("Speichern");
        save.Click += (_, _) =>
        {
            foreach (var (key, input, kind) in boxes)
            {
                values[key] = kind switch
                {
                    "bool" => ((CheckBox)input).IsChecked == true ? "true" : "false",
                    "choice" => FahrbetriebPages.Selected(input, values.GetValueOrDefault(key) ?? ""),
                    _ => ((TextBox)input).Text?.Trim() ?? ""
                };
            }

            GeraetEinstellungen.Save(values);
            GeraetEinstellungen.ApplyTo(session);
            session.SaveDevice();
            StandortSend.Refresh(session);
            info.Text = "Einstellungen gespeichert.";
        };
        var remote = FahrbetriebPages.Touch("Von der Leitstelle holen");
        remote.Click += async (_, _) =>
        {
            try
            {
                info.Text = await ApplyRemoteAsync(session, values);
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        children.Add(save);
        children.Add(remote);
        children.Add(info);
        return FahrbetriebPages.Column("Einstellungen", children.ToArray());
    }

    public static Control Fahrzeugdiagnose(FahrSession session)
    {
        var status = FahrbetriebPages.Info(FmsLive.Enabled ? FmsLive.Status : "Aus");
        var master = new ToggleSwitch
        {
            Content = "Aktiv",
            IsChecked = FmsLive.Enabled,
            Foreground = Brushes.White,
            FontSize = 18,
            OnContent = "",
            OffContent = ""
        };
        master.IsCheckedChanged += (_, _) =>
        {
            FmsLive.SetEnabled(master.IsChecked == true);
            status.Text = FmsLive.Enabled ? FmsLive.Status : "Aus";
        };
        var rows = new List<(string Key, TextBlock Value)>();
        var children = new List<Control> { master, status };
        var keys = new List<string>(FmsSignals.All.Select(s => s.Key));
        foreach (var extra in FmsLive.Snapshot().Keys)
        {
            if (!keys.Contains(extra))
            {
                keys.Add(extra);
            }
        }

        var valueBrush = new SolidColorBrush(Color.Parse("#0A3A6B"));
        var valueBack = new SolidColorBrush(Color.Parse("#D6F0FF"));
        foreach (var key in keys)
        {
            var check = FahrbetriebPages.Check(FmsSignals.Label(key), FmsLive.IsForwarded(key));
            check.IsCheckedChanged += (_, _) => FmsLive.SetForwarded(key, check.IsChecked == true);
            check.VerticalAlignment = VerticalAlignment.Center;
            var value = new TextBlock
            {
                Text = "—",
                Foreground = valueBrush,
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var box = new Border
            {
                Background = valueBack,
                CornerRadius = new Avalonia.CornerRadius(8),
                Padding = new Avalonia.Thickness(16, 8),
                MinWidth = 140,
                Child = value
            };
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Avalonia.Thickness(0, 4)
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(check);
            row.Children.Add(box);
            rows.Add((key, value));
            children.Add(row);
        }

        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            status.Text = FmsLive.Status;
            var snap = FmsLive.Snapshot();
            foreach (var (key, value) in rows)
            {
                value.Text = snap.TryGetValue(key, out var raw) ? FmsSignals.FormatRaw(key, raw) : "—";
            }
        };
        timer.Start();
        var page = FahrbetriebPages.Column("Fahrzeugdiagnose", children.ToArray());
        page.DetachedFromVisualTree += (_, _) => timer.Stop();
        return page;
    }

    public static Control Entwerter(FahrSession session)
    {
        var line = FahrbetriebPages.Field("Linie, 3 Ziffern");
        line.Text = GeraetEinstellungen.Text("entwerter_line", "");
        var code = FahrbetriebPages.Field("DS004 Linie+Wabe, 6 Ziffern");
        code.Text = GeraetEinstellungen.Text("entwerter_ds004", "");
        var extra = FahrbetriebPages.Field("DS004a, 4 Ziffern");
        extra.Text = GeraetEinstellungen.Text("entwerter_ds004a", "");
        var razzia = FahrbetriebPages.Check("Razzia (DS004a Stelle 4 = 0)", GeraetEinstellungen.Bool("itcs_razzia_button_enabled", false));
        var info = FahrbetriebPages.Info("Sendereihenfolge: Linie, Uhrzeit, Datum, DS004, DS004a.");
        var send = FahrbetriebPages.Touch("Senden");
        send.Click += (_, _) =>
        {
            try
            {
                var now = DateTime.Now;
                var lineDigits = Digits(line.Text, 3);
                var six = Digits(code.Text, 6);
                var four = Digits(extra.Text, 4);
                if (razzia.IsChecked == true && four.Length == 4)
                {
                    four = four[..3] + "0";
                }

                var values = GeraetEinstellungen.Load();
                values["entwerter_line"] = lineDigits;
                values["entwerter_ds004"] = six;
                values["entwerter_ds004a"] = four;
                GeraetEinstellungen.Save(values);
                var port = session.IbisPort;
                VehicleLink.Send(port, DisplayTelegrams.Ibis("l" + lineDigits), 9600);
                VehicleLink.Send(port, DisplayTelegrams.Ibis($"u{now:HHmm}"), 9600);
                VehicleLink.Send(port, DisplayTelegrams.Ibis($"d{now:ddMM}{now.Year % 10}"), 9600);
                VehicleLink.Send(port, DisplayTelegrams.Ibis("e" + six), 9600);
                VehicleLink.Send(port, DisplayTelegrams.Ibis("eA" + four), 9600);
                info.Text = $"Entwerter an {port}: l{lineDigits} u{now:HHmm} e{six} eA{four}";
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        return FahrbetriebPages.Column("Entwerter", line, code, extra, razzia, send, info);
    }

    public static Control Maengelkarte(FahrSession session)
    {
        var list = new ListBox { FontSize = 16, Foreground = Brushes.White, Background = Brushes.Transparent };
        var input = FahrbetriebPages.Field("Mangel, höchstens 60 Zeichen");
        var info = FahrbetriebPages.Info();
        void Show(MaengelkarteDocument? document)
        {
            list.Items.Clear();
            foreach (var entry in document?.Entries ?? [])
            {
                if (entry.Status == MaengelkarteStatus.Resolved)
                {
                    continue;
                }

                list.Items.Add($"{entry.CreatedDisplay}  {entry.AuthorDisplay}: {entry.Text} ({entry.StatusLabel})");
            }
        }

        var sync = FahrbetriebPages.Touch("Abgleichen");
        sync.Click += async (_, _) =>
        {
            try
            {
                Show(await SyncAsync(session, null));
                info.Text = "Mängelkarte abgeglichen.";
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        var save = FahrbetriebPages.Touch("Eintragen");
        save.Click += async (_, _) =>
        {
            var text = (input.Text ?? "").Trim();
            if (text.Length == 0)
            {
                info.Text = "Text fehlt.";
                return;
            }

            if (text.Length > 60)
            {
                text = text[..60];
            }

            var now = DateTimeOffset.Now;
            var entry = new MaengelkarteEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Text = text,
                CreatedAtMs = now.ToUnixTimeMilliseconds(),
                CreatedAtIso = now.ToString("yyyy-MM-dd'T'HH:mm:sszzz"),
                AuthorPersonnel = new string((session.Employee?.PersonnelNumber ?? "").Where(char.IsDigit).ToArray()),
                AuthorName = session.Employee?.Name ?? "",
                AuthorDevicePhone = session.DevicePhone,
                Status = MaengelkarteStatus.New,
                UpdatedAtMs = now.ToUnixTimeMilliseconds()
            };
            try
            {
                Show(await SyncAsync(session, entry));
                input.Text = "";
                info.Text = "Eintrag gespeichert.";
            }
            catch (Exception ex)
            {
                info.Text = ex.Message;
            }
        };
        return FahrbetriebPages.Column("Mängelkarte", input, save, sync, info, list);
    }

    private static async Task<string> ApplyRemoteAsync(FahrSession session, Dictionary<string, string> values)
    {
        var client = OpenDropbox();
        if (client is null)
        {
            return "Dropbox ist nicht verbunden.";
        }

        var digits = new string(session.DevicePhone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            return "Gerätetelefon fehlt.";
        }

        string? json = null;
        foreach (var name in new[] { $"remote_settings_({digits}).json", $"remote_settings_{digits}.json" })
        {
            json = await client.TryDownloadNamedFileAsync(name);
            if (!string.IsNullOrWhiteSpace(json))
            {
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return "Keine Einstellungen von der Leitstelle.";
        }

        var settings = JsonNode.Parse(json)?["settings"] as JsonObject;
        if (settings is null)
        {
            return "Einstellungsdatei ohne Inhalt.";
        }

        foreach (var (jsonName, key) in RemoteKeys)
        {
            if (settings[jsonName] is not JsonNode node)
            {
                continue;
            }

            values[key] = node is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : node.ToString().Trim('"');
        }

        GeraetEinstellungen.Save(values);
        GeraetEinstellungen.ApplyTo(session);
        session.SaveDevice();
        return "Einstellungen der Leitstelle übernommen. Seite neu öffnen.";
    }

    private static async Task<MaengelkarteDocument> SyncAsync(FahrSession session, MaengelkarteEntry? extra)
    {
        var local = MaengelkarteMergeService.TryLoadLocal(FahrgeraetRouteSync.AppFolder) ?? MaengelkarteMergeService.EmptyDocument();
        MaengelkarteDocument? remote = null;
        var client = OpenDropbox();
        if (client is not null)
        {
            var json = await client.TryDownloadNamedFileAsync(DropboxConstants.MaengelkarteFileName);
            remote = MaengelkarteMergeService.TryParse(json);
        }

        var merged = MaengelkarteMergeService.Merge(local, remote);
        if (extra is not null)
        {
            merged.Entries.Insert(0, extra);
        }

        MaengelkarteMergeService.SaveLocal(FahrgeraetRouteSync.AppFolder, merged);
        if (client is not null)
        {
            await client.UploadNamedFileAsync(DropboxConstants.MaengelkarteFileName, MaengelkarteMergeService.Serialize(merged));
        }

        return merged;
    }

    private static DropboxApiClient? OpenDropbox()
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

    private static string Digits(string? raw, int length)
    {
        var digits = new string((raw ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
        {
            digits = "0";
        }

        return digits.Length >= length ? digits[..length] : digits.PadLeft(length, '0');
    }

    private static readonly (string Json, string Key)[] RemoteKeys =
    [
        ("buttonSoundsEnabled", "button_sounds_enabled"),
        ("autoOpenNextRoute", "auto_open_next_route"),
        ("nightModeEnabled", "night_mode_enabled"),
        ("fahrgastraumGainPercent", "fahrgastraum_gain_percent"),
        ("tftTimeEnabled", "tft_time_enabled"),
        ("interiorAltProtocolEnabled", "interior_alt_protocol_enabled"),
        ("tftLawoEnabled", "tft_lawo_enabled"),
        ("hideNavigationBarEnabled", "hide_navigation_bar_enabled"),
        ("autoHideNavigationOnGps", "auto_hide_navigation_on_gps"),
        ("zblContactMode", "zbl_contact_mode"),
        ("tftTcpEnabled", "tft_tcp_enabled"),
        ("tftTcpPort", "tft_tcp_port"),
        ("tcpSocketClientEnabled", "tcp_socket_client_enabled"),
        ("tcpSocketProtocol", "tcp_socket_protocol"),
        ("tcpSocketHost", "tcp_socket_host"),
        ("tcpSocketPort", "tcp_socket_port"),
        ("tcpSocketScope", "tcp_socket_scope"),
        ("ibisExteriorTransport", "ibis_exterior_transport"),
        ("ibisInteriorTransport", "ibis_interior_transport"),
        ("selectedProtocol", "selected_protocol"),
        ("ibisDisplayControlMode", "ibis_display_control_mode"),
        ("sevTftEnabled", "sev_tft_enabled"),
        ("sevTftAutoSend", "sev_tft_auto_send"),
        ("sevTftHost", "sev_tft_host"),
        ("sevTftPort", "sev_tft_port"),
        ("sevTftApiToken", "sev_tft_api_token"),
        ("annaxIbisEnabled", "annax_ibis_enabled"),
        ("annaxIbisDisplayAddress", "annax_ibis_display_address")
    ];
}
