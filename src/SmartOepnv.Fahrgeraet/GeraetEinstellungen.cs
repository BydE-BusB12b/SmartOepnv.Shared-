using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartOepnv.Fahrgeraet;

public static class GeraetEinstellungen
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath(FahrSession session) =>
        Path.Combine(session.WorkspaceDir, "einstellungen.json");

    public static Dictionary<string, string> Load()
    {
        var session = new FahrSession();
        var path = FilePath(session);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (node is null)
            {
                return values;
            }

            foreach (var pair in node)
            {
                values[pair.Key] = pair.Value is JsonValue json && json.TryGetValue<string>(out var text)
                    ? text
                    : pair.Value?.ToString() ?? "";
            }

            return values;
        }
        catch
        {
            return [];
        }
    }

    public static void Save(Dictionary<string, string> values)
    {
        var session = new FahrSession();
        Directory.CreateDirectory(session.WorkspaceDir);
        var obj = new JsonObject();
        foreach (var pair in values.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            obj[pair.Key] = pair.Value;
        }

        File.WriteAllText(FilePath(session), obj.ToJsonString(Json));
    }

    public static void ApplyTo(FahrSession session)
    {
        var values = Load();
        if (values.Count == 0)
        {
            return;
        }

        session.OutsideProtocol = Text(values, "selected_protocol", session.OutsideProtocol);
        session.InteriorProtocol = Text(values, "interior_protocol", session.InteriorProtocol);
        session.Ds009Length = Int(values, "ds009_text_length", session.Ds009Length) <= 16 ? 16 : 20;
        session.Ds009OnlyStop = Bool(values, "ds009_only_stop_text", session.Ds009OnlyStop);
        session.Ds009Additional = Bool(values, "interior_ds009_additional", session.Ds009Additional);
        session.Ds003cVerlauf = Bool(values, "interior_ds003c_verlauf", session.Ds003cVerlauf);
        session.TftTime = Bool(values, "tft_time_enabled", session.TftTime);
        session.TftDate2030 = Bool(values, "tft_date_2030_test_enabled", session.TftDate2030);
        session.TftSwsAddress = Int(values, "tft_sws_display_address", session.TftSwsAddress);
        session.BustecAddress = Int(values, "bustec_tft_display_address", session.BustecAddress);
        session.AnnaxAddress = Int(values, "annax_ibis_display_address", session.AnnaxAddress);
        session.GorbaAddress = Int(values, "gorba_infomedia_display_address", session.GorbaAddress);
        session.IbisIpMode = Text(values, "ibis_ip_connection_type", session.IbisIpMode);
        session.IbisIpUrl = Text(values, "ibis_ip_server_url", session.IbisIpUrl);
        session.IbisIpHost = Text(values, "ibis_ip_server_host", session.IbisIpHost);
        session.IbisIpPort = Int(values, "ibis_ip_server_port", session.IbisIpPort);
        session.IbisIpListenPort = Int(values, "ibis_ip_listen_port", session.IbisIpListenPort);
        var control = Text(values, "ibis_display_control_mode", session.DisplayControl);
        session.DisplayControl = control.Contains("SICMA", StringComparison.OrdinalIgnoreCase) ? "SICMA" : "Klartext";
        if (values.TryGetValue("device_phone_number", out var phone) && phone.Length > 0)
        {
            session.DevicePhone = phone;
        }

        if (values.TryGetValue("gps_port", out var gps))
        {
            session.GpsPort = gps;
        }

        if (values.TryGetValue("ibis_port", out var ibis))
        {
            session.IbisPort = ibis;
        }

        if (values.TryGetValue("rs485_port", out var rs))
        {
            session.Rs485Port = rs;
        }
    }

    public static void CopyFromSession(FahrSession session)
    {
        var values = Load();
        values["selected_protocol"] = session.OutsideProtocol;
        values["interior_protocol"] = session.InteriorProtocol;
        values["ds009_text_length"] = session.Ds009Length.ToString();
        values["ds009_only_stop_text"] = session.Ds009OnlyStop ? "true" : "false";
        values["interior_ds009_additional"] = session.Ds009Additional ? "true" : "false";
        values["interior_ds003c_verlauf"] = session.Ds003cVerlauf ? "true" : "false";
        values["tft_time_enabled"] = session.TftTime ? "true" : "false";
        values["tft_date_2030_test_enabled"] = session.TftDate2030 ? "true" : "false";
        values["tft_sws_display_address"] = session.TftSwsAddress.ToString();
        values["bustec_tft_display_address"] = session.BustecAddress.ToString();
        values["annax_ibis_display_address"] = session.AnnaxAddress.ToString();
        values["gorba_infomedia_display_address"] = session.GorbaAddress.ToString();
        values["ibis_ip_connection_type"] = session.IbisIpMode;
        values["ibis_ip_server_url"] = session.IbisIpUrl;
        values["ibis_ip_server_host"] = session.IbisIpHost;
        values["ibis_ip_server_port"] = session.IbisIpPort.ToString();
        values["ibis_ip_listen_port"] = session.IbisIpListenPort.ToString();
        values["ibis_display_control_mode"] = session.DisplayControl == "SICMA" ? "SICMA_ZD_TARGET_NUMBER" : "DIRECT_CLEARTEXT";
        values["device_phone_number"] = session.DevicePhone;
        values["gps_port"] = session.GpsPort;
        values["ibis_port"] = session.IbisPort;
        values["rs485_port"] = session.Rs485Port;
        Save(values);
    }

    public static string Text(Dictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && value.Length > 0 ? value : fallback;

    public static string Text(string key, string fallback) => Text(Load(), key, fallback);

    public static bool Bool(Dictionary<string, string> values, string key, bool fallback)
    {
        if (!values.TryGetValue(key, out var value) || value.Length == 0)
        {
            return fallback;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
    }

    public static bool Bool(string key, bool fallback) => Bool(Load(), key, fallback);

    public static int Int(Dictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && int.TryParse(value, out var number) ? number : fallback;

    public static int Int(string key, int fallback) => Int(Load(), key, fallback);
}
