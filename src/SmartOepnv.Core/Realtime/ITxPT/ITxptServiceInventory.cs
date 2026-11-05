namespace SmartOepnv.Core.Realtime.ITxPT;

/// <summary>
/// Pragmatische ITxPT-Landkarte für Smart ÖPNV: welche Fahrzeugdienste wir abdecken
/// bzw. anstreben – ohne ITxPT-Lab-Zertifikat. Bezug: VDV 301 / IBIS-IP auf dem Tablet.
/// </summary>
public static class ITxptServiceInventory
{
    public enum Coverage
    {
        None,
        Partial,
        Implemented
    }

    public sealed record ServiceEntry(
        string ServiceId,
        string Title,
        Coverage Coverage,
        string Notes);

    /// <summary>Aktueller Stand der Onboard-Dienste (Android IBIS-IP + USB/WLAN-IBIS).</summary>
    public static IReadOnlyList<ServiceEntry> CurrentServices { get; } =
    [
        new("CustomerInformationService", "Fahrgastinfo / Ziele / Halte", Coverage.Partial,
            "IBIS-IP CIS XML + klassisches IBIS DS021T/DS009; StopRef=Planercode, StopName=Anzeigename."),
        new("GNSSLocationService", "GNSS-Position", Coverage.Partial,
            "IBIS-IP GNSS XML aus App-Ortung; kein zertifiziertes ITxPT-GNSS-Modul."),
        new("JourneyInformation / Trip", "Fahrt-/Lauf-Metadaten", Coverage.Partial,
            "tripRef/runNumber/Delay live aus Kurswahl + Delay-Tracker an IBIS-IP; Umlauf/BlockRef noch TripRef-Alias."),
        new("DeviceManagementService", "Geräteverwaltung", Coverage.Partial,
            "IBIS-IP DeviceManagement XML vorhanden."),
        new("TimeService", "Zeit", Coverage.None,
            "Tablet-Systemzeit; kein eigener ITxPT-Time-Dienst."),
        new("InventoryService", "Fahrzeuginventar", Coverage.None,
            "Kein ITxPT-Inventory; Roster nur Leitstelle/Dropbox."),
        new("AVMS / APC", "Zählung", Coverage.None, "Nicht im Scope."),
        new("Priority / LSA", "Lichtsignal", Coverage.None, "Nicht im Scope.")
    ];

    public static string Summarize() =>
        string.Join(Environment.NewLine,
            CurrentServices.Select(s =>
                $"[{s.Coverage}] {s.ServiceId}: {s.Title} – {s.Notes}"));
}
