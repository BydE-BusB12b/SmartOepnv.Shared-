using System.Text.Json;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

public sealed class FahrSession
{
    public EditableRoutePackage? Package { get; set; }
    public EmployeeRosterItem? Employee { get; set; }
    public string? RouteName { get; set; }
    public string DevicePhone { get; set; } = "";
    public string IbisPort { get; set; } = "";
    public string Rs485Port { get; set; } = "";
    public string GpsPort { get; set; } = "";
    public string OutsideProtocol { get; set; } = "DS021T";
    public string InteriorProtocol { get; set; } = "DS009";
    public int Ds009Length { get; set; } = 20;
    public bool Ds009OnlyStop { get; set; }
    public bool Ds009Additional { get; set; }
    public bool Ds003cVerlauf { get; set; }
    public bool TftTime { get; set; } = true;
    public bool TftDate2030 { get; set; }
    public int TftSwsAddress { get; set; } = 1;
    public int BustecAddress { get; set; } = 1;
    public int AnnaxAddress { get; set; } = 1;
    public int GorbaAddress { get; set; } = 1;
    public string IbisIpMode { get; set; } = "HTTP";
    public string IbisIpUrl { get; set; } = "";
    public string IbisIpHost { get; set; } = "";
    public int IbisIpPort { get; set; } = 8080;
    public int IbisIpListenPort { get; set; } = 8080;
    public string DisplayControl { get; set; } = "Klartext";
    public Action<string>? Status { get; set; }
    public FahrVoip Funk { get; } = new();
    public FahrTrip Trip { get; } = new();

    public string WorkspaceDir { get; } =
        Path.Combine(AppPaths.GetRoamingDataDirectory(FahrgeraetRouteSync.AppFolder), "workspace");

    public void LoadDevice()
    {
        Directory.CreateDirectory(WorkspaceDir);
        var path = Path.Combine(WorkspaceDir, "geraet.json");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var doc = JsonSerializer.Deserialize<DeviceFile>(File.ReadAllText(path));
            if (doc is null)
            {
                return;
            }

            DevicePhone = doc.DevicePhone ?? "";
            IbisPort = doc.IbisPort ?? "";
            Rs485Port = doc.Rs485Port ?? "";
            GpsPort = doc.GpsPort ?? "";
            OutsideProtocol = doc.OutsideProtocol ?? "DS021T";
            InteriorProtocol = doc.InteriorProtocol ?? "DS009";
            Ds009Length = doc.Ds009Length is 16 or 20 ? doc.Ds009Length : 20;
            Ds009OnlyStop = doc.Ds009OnlyStop;
            Ds009Additional = doc.Ds009Additional;
            Ds003cVerlauf = doc.Ds003cVerlauf;
            TftTime = doc.TftTime;
            TftDate2030 = doc.TftDate2030;
            TftSwsAddress = doc.TftSwsAddress <= 0 ? 1 : doc.TftSwsAddress;
            BustecAddress = doc.BustecAddress <= 0 ? 1 : doc.BustecAddress;
            AnnaxAddress = doc.AnnaxAddress <= 0 ? 1 : doc.AnnaxAddress;
            GorbaAddress = doc.GorbaAddress <= 0 ? 1 : doc.GorbaAddress;
            IbisIpMode = doc.IbisIpMode ?? "HTTP";
            IbisIpUrl = doc.IbisIpUrl ?? "";
            IbisIpHost = doc.IbisIpHost ?? "";
            IbisIpPort = doc.IbisIpPort <= 0 ? 8080 : doc.IbisIpPort;
            IbisIpListenPort = doc.IbisIpListenPort <= 0 ? 8080 : doc.IbisIpListenPort;
            DisplayControl = doc.DisplayControl ?? "Klartext";
        }
        catch
        {
            // defekte lokale Datei bleibt leer
        }
    }

    public void SaveDevice()
    {
        Directory.CreateDirectory(WorkspaceDir);
        var json = JsonSerializer.Serialize(new DeviceFile
        {
            DevicePhone = DevicePhone,
            IbisPort = IbisPort,
            Rs485Port = Rs485Port,
            GpsPort = GpsPort,
            OutsideProtocol = OutsideProtocol,
            InteriorProtocol = InteriorProtocol,
            Ds009Length = Ds009Length,
            Ds009OnlyStop = Ds009OnlyStop,
            Ds009Additional = Ds009Additional,
            Ds003cVerlauf = Ds003cVerlauf,
            TftTime = TftTime,
            TftDate2030 = TftDate2030,
            TftSwsAddress = TftSwsAddress,
            BustecAddress = BustecAddress,
            AnnaxAddress = AnnaxAddress,
            GorbaAddress = GorbaAddress,
            IbisIpMode = IbisIpMode,
            IbisIpUrl = IbisIpUrl,
            IbisIpHost = IbisIpHost,
            IbisIpPort = IbisIpPort,
            IbisIpListenPort = IbisIpListenPort,
            DisplayControl = DisplayControl
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(WorkspaceDir, "geraet.json"), json);
    }

    private sealed class DeviceFile
    {
        public string? DevicePhone { get; set; }
        public string? IbisPort { get; set; }
        public string? Rs485Port { get; set; }
        public string? GpsPort { get; set; }
        public string? OutsideProtocol { get; set; }
        public string? InteriorProtocol { get; set; }
        public int Ds009Length { get; set; }
        public bool Ds009OnlyStop { get; set; }
        public bool Ds009Additional { get; set; }
        public bool Ds003cVerlauf { get; set; }
        public bool TftTime { get; set; } = true;
        public bool TftDate2030 { get; set; }
        public int TftSwsAddress { get; set; }
        public int BustecAddress { get; set; }
        public int AnnaxAddress { get; set; }
        public int GorbaAddress { get; set; }
        public string? IbisIpMode { get; set; }
        public string? IbisIpUrl { get; set; }
        public string? IbisIpHost { get; set; }
        public int IbisIpPort { get; set; }
        public int IbisIpListenPort { get; set; }
        public string? DisplayControl { get; set; }
    }
}
