namespace SmartOepnv.AppShared.Employees;

/// <summary>Vollständige Leitstelle-Unterweisungsanleitung (alle Navigationsbereiche).</summary>
internal static class LeitstelleOperatorManualContent
{
    public const string AssetSubfolder = "briefing/leitstelle";

    public static OperatorManualDocument Document { get; } = new(
        CoverTitle: "Smart-ÖPNV Leitstelle",
        CoverSubtitle: "Unterweisungsanleitung",
        IntroText: """
            Diese Anleitung beschreibt alle Bereiche der Smart-ÖPNV Leitstelle: Live-Überwachung, Fernsteuerung,
            Nachrichten, Funk (VoIP) und Datenversand. Sie dient der Einweisung von Disponenten und Leitstellenpersonal.
            """,
        CoverHint: """
            Hinweis zu Abbildungen: Screenshots unter Assets/briefing/leitstelle/ – siehe PLANER_LEITSTELLE_SCREENSHOT_GUIDE.md.
            """,
        ClosingNote: """
            Stand: automatisch aus dem Programm erzeugt. Bei Software-Updates kann sich die Oberfläche leicht ändern.
            Technische Fragen: Christopher Lambers / hkx1803@web.de
            """,
        Sections: Sections);

    private static readonly BriefingSection[] Sections =
    [
        Section(
            "1. Start, Dropbox und Hintergrundsync",
            """
            • Die Leitstelle startet ohne Planer-Login – direkter Zugriff nach Programmstart.
            • Dropbox-Verbindung unter Einstellungen einrichten; Standardordner /smart öpnv.
            • Beim Start: routes_export.json und leitstelle_stand.json werden geladen (Auto-Sync alle 15 Min.).
            • VoIP/Funk startet automatisch, sofern konfiguriert.
            • Software-Updates werden über software_versions.json in Dropbox angeboten.
            """,
            "leitstelle_start.png", "Leitstelle Startansicht"),
        Section(
            "2. Übersicht (Dashboard)",
            """
            • Warnungen zu HU/SP, Führerschein/FQN/Fahrerkarte und 3-Monats-Kontrollen.
            • Schnellzugriff auf Daten importieren/exportieren.
            • Kein Planer-Workspace-Sync – nur Lesen/Senden von Fahrzeugdaten.
            """,
            "leitstelle_dashboard.png", "Übersicht Leitstelle"),
        Section(
            "3. Fahrzeuge – Live-Karte",
            """
            • Live-Karte mit allen online Fahrzeugen (location_chat_*.json aus Dropbox).
            • Statusfarben: grün = online, rot = veraltet, lila = offline.
            • Auto-Aktualisierung ca. alle 8–15 Sekunden.
            • Einfachklick: Karte fokussieren; Doppelklick: KOM-Detail; Rechtsklick: Fernsteuerung.
            • Routen-Fahrweg kann auf der Karte hervorgehoben werden.
            • Kartenansicht (Zoom/Position) speicherbar.
            """,
            "leitstelle_map.png", "Live-Karte Fahrzeuge"),
        Section(
            "4. KOM-Detail und Fahrzeugstatus",
            """
            • Overlay zeigt: Fahrer, Linie/Kurs, Route, Haltestelle, Ziel, Verspätung, Geschwindigkeit, Akku.
            • Letztes GPS-Update und Straßenposition.
            • Grundlage für Fernentscheidungen (Umleitung, Zielwechsel, Durchsage).
            """,
            "leitstelle_kom_detail.png", "KOM-Fahrzeugdetail"),
        Section(
            "5. Fernsteuerung",
            """
            • Rechtsklick auf Fahrzeug → Fernsteuerung öffnen.
            • Fernziel: Außenanzeigen-Ziel setzen.
            • Fernroute: Route remote aktivieren (Pas.Info bleibt erhalten).
            • Fahrgastraum-Durchsage: Mikrofon oder Text-to-Speech (max. 3 Min.).
            • Meldung: Einzelnachricht an Fahrzeug senden.
            • Jede Aktion wird per Dropbox-KOM an das Tablet gesendet; Bestätigung im Status.
            """,
            "leitstelle_remote.png", "Fernsteuerungsdialog"),
        Section(
            "6. Funk (VoIP)",
            """
            • Funk-Button in der Fernsteuerung oder bei Sprechwunsch aus Nachrichten.
            • Modi: Cloud, Managed, Betriebshof, Mobil, Dual, Funnel – Konfiguration unter Einstellungen.
            • VoIP-Config nach Dropbox publizieren, damit Tablets die Verbindung aufbauen können.
            • Leertaste: Sprechen; Anruf automatisch bei SOS/Sprechwunsch möglich.
            • Status in der Leitstellen-Statusleiste (Verbunden / Klingelt / Gespräch).
            """,
            "leitstelle_voip.png", "Funk-Dialog VoIP"),
        Section(
            "7. Personalverwaltung",
            """
            • Mitarbeiterstammdaten, Dokumente und 3-Monats-Kontrollen (wie im Planer).
            • PDF erstellen: persönliche Fahrer-Einweisung für Tablets.
            • Badge bei fälligen Kontrollen.
            • Kein Planer-Login-Verwaltung – nur Stammdaten für Betrieb und Einweisung.
            """,
            "leitstelle_employees.png", "Personalverwaltung Leitstelle"),
        Section(
            "8. Fahrzeugverwaltung",
            """
            • KOM-Fahrzeuge: Name und Telefonnummer für Tracking und Fernsteuerung.
            • HU/SP-Fristen und Fahrzeugdaten (wenn im Export enthalten).
            • Änderungen speichern und über Dropbox an Tablets verteilen.
            """,
            "leitstelle_vehicles.png", "Fahrzeugverwaltung"),
        Section(
            "9. Mängelkarte",
            """
            • Mängelmeldungen aus der Fahrer-App (maengelkarte.json).
            • Laden aus Dropbox, Status bearbeiten, Filter nach Fahrzeug.
            • Erledigte Einträge ausblenden – bleiben im Planer erhalten.
            """,
            "leitstelle_maengel.png", "Mängelkarte Leitstelle"),
        Section(
            "10. Nachrichten – Posteingang",
            """
            • MailChat und SOS aus Dropbox (mailchat, soschat).
            • Typen: Mail, SOS, Sprechwunsch – mit Tonbenachrichtigung.
            • Header-Alerts in der App-Leiste (SOS rot hervorgehoben).
            • Klick auf SOS/Sprechwunsch → springt zur Live-Karte (+ Funk bei Sprechwunsch).
            • Badge für ungelesene Mails; Verlauf lokal gespeichert.
            """,
            "leitstelle_inbox.png", "Nachrichten-Posteingang"),
        Section(
            "11. Nachricht senden",
            """
            • Vorlagen aus messageTemplates (vom Planer).
            • Text bearbeiten, Empfänger wählen (einzelne Fahrzeuge oder alle).
            • Versand als zbl_message pro Telefonnummer über Dropbox.
            """,
            "leitstelle_send_message.png", "Nachricht an Fahrzeuge senden"),
        Section(
            "12. Versand und Datenabgleich",
            """
            • routes_export.json laden/senden – Vollbackup mit Audio.
            • routes_update.json – leichtes Update ohne Tondateien.
            • Senden + Fernupdate: Dialog mit Wahl Vollbackup oder Update, dann Fahrzeug auswählen.
            • Routen an Fahrzeuge: Update (Merge) oder Senden (mit Löschung nicht ausgewählter Routen).
            • Kein Planer-Workspace – nur Fahrzeug- und Leitstellen-relevante Dateien.
            """,
            "leitstelle_data_transfer.png", "Versand Leitstelle"),
        Section(
            "13. Einstellungen",
            """
            • Dropbox verbinden, Ordnerpfad, Verbindungstest.
            • Funk/VoIP: Server, Modus, TURN, Config nach Dropbox.
            • Unterweisungsanleitung Leitstelle als PDF exportieren (diese Anleitung).
            • Keine Firmenlogos / Planer-Ordner (nur Planer).
            """,
            "leitstelle_settings.png", "Einstellungen Leitstelle"),
        Section(
            "14. Abgrenzung zum Planer",
            """
            • Nur in der Leitstelle: Live-Karte, Fernsteuerung, Funk, Nachrichten-Posteingang, Nachricht senden.
            • Nur im Planer: Routen, Haltestellen, Ansagen, Navidaten, Disposition, Dienstvorlagen, SEV, Zeitwirtschaft-Editor.
            • Datenbasis gemeinsam: routes_export.json, leitstelle_stand.json, Dropbox-Ordner /smart öpnv.
            """,
            "leitstelle_overview_menu.png", "Leitstellen-Navigation"),
    ];

    private static BriefingSection Section(string title, string body, string imageFile, string caption) =>
        new(title, body, [new BriefingImage(caption, imageFile)]);
}
