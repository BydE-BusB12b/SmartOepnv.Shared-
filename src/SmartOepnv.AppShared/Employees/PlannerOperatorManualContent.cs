namespace SmartOepnv.AppShared.Employees;

/// <summary>Vollständige Planer-Unterweisungsanleitung (alle Navigationsbereiche).</summary>
internal static class PlannerOperatorManualContent
{
    public const string AssetSubfolder = "briefing/planner";

    public static OperatorManualDocument Document { get; } = new(
        CoverTitle: "Smart-ÖPNV Planer",
        CoverSubtitle: "Unterweisungsanleitung",
        IntroText: """
            Diese Anleitung beschreibt alle Bereiche des Smart-ÖPNV Planers: Routen, Haltestellen, Ansagen,
            Disposition, Datenversand und Einstellungen. Sie dient der Einweisung von Planern, Disponenten
            und Administratoren.
            """,
        CoverHint: """
            Hinweis zu Abbildungen: Screenshots werden unter Assets/briefing/planner/ abgelegt.
            Fehlende Bilder werden in der PDF als Platzhalter angezeigt – siehe PLANER_LEITSTELLE_SCREENSHOT_GUIDE.md.
            """,
        ClosingNote: """
            Stand: automatisch aus dem Programm erzeugt. Bei Software-Updates kann sich die Oberfläche leicht ändern.
            Technische Fragen: Christopher Lambers / hkx1803@web.de
            """,
        Sections: Sections);

    private static readonly BriefingSection[] Sections =
    [
        Section(
            "1. Anmeldung, Sitzung und Arbeitsstand",
            """
            • Starten Sie den Planer und melden Sie sich mit Ihrer Planer-Personalnummer und Ihrem Planer-Passwort an.
            • Nach der Anmeldung lädt der Planer den gemeinsamen Arbeitsstand aus Dropbox (planer_workspace.json).
            • Die Sitzungssperre verhindert gleichzeitiges Bearbeiten durch mehrere Planer – „Sperre freigeben“ nur nach Rücksprache.
            • Beim Beenden: Daten werden nach Dropbox gesichert; lokale Kopien liegen unter %AppData%\\Smart-OEPNV\\Planer\\betriebe\\…\\workspace\\.
            • Versionen/Snapshots können unter Versand → Versionen gesichert werden.
            """,
            "planner_login.png", "Anmeldung Planer"),
        Section(
            "2. Übersicht (Dashboard)",
            """
            • Die Übersicht zeigt Warnungen zu HU/SP-Fristen, Führerschein/FQN/Fahrerkarte und 3-Monats-Kontrollen.
            • Klick auf eine Warnung springt zur Personalverwaltung oder Fahrzeugverwaltung.
            • Im unteren Bereich: Kurzstatistik (Routen, Haltestellen, Fahrer) und Schnellzugriff auf Import/Export.
            • Prüfen Sie die Übersicht zu Schichtbeginn, bevor Sie Daten an Fahrzeuge senden.
            """,
            "planner_dashboard.png", "Übersicht mit Warnungen"),
        Section(
            "3. Routen",
            """
            • Routen anlegen, bearbeiten, löschen und suchen.
            • Pro Route: Linie/Kurs, Fahrtnummer, Verkehrstage, ITCS-Sichtbarkeit, Hauptnutzer-Route.
            • Haltestellenfolge bearbeiten: Reihenfolge, Starthaltestelle, Ziele, Ansagen, GPS, Radius.
            • Kopfzeile in der App nach Routenwahl: Linie/Kurs, Routenname, Ziel: (ohne Ziel: „Ziel: Kein Ziel“).
            • DS009-Wechselanzeige und datumgesteuerte Hinweise können pro Route konfiguriert werden.
            """,
            "planner_routes.png", "Routenliste und Bearbeitung"),
        Section(
            "4. Haltestellenbibliothek",
            """
            • Zentrale Haltestellenvorlagen (managedStopTemplates) für alle Routen.
            • VRR-Stopp-IDs, Koordinaten, Zieltexte und Stammdaten pflegen.
            • Vorlagen in Routen einfügen oder aktualisieren (Merge).
            • Karten-Picker für GPS-Koordinaten; Merge-Pause bei großen Aktualisierungen.
            """,
            "planner_stops_library.png", "Haltestellenbibliothek"),
        Section(
            "5. Ansagen",
            """
            • Ansagen-Kartei mit 4-stelliger ID, Beschreibung und Tonzuordnung.
            • Eingebettete Töne, Roh-Ansagen (ansagen_roh), Tonsequenzen, Sonderansagen (★ mit „S“).
            • Endhaltestellen-Ansagen und Standard-Pausen konfigurieren.
            • Änderungen fließen in routes_export.json bzw. routes_update.json beim Versand ein.
            """,
            "planner_announcements.png", "Ansagen-Kartei"),
        Section(
            "6. Navidaten (Fahrweg)",
            """
            • Fahrweg pro Route auf der Karte bearbeiten (WebView2).
            • Segmente und Knoten für präzise Entfernungsberechnung und Kartenanzeige.
            • Speichern synchronisiert den Fahrweg mit dem Routenpaket (handysynchron).
            • Undo und Route-import aus bestehenden Haltestellen unterstützt.
            """,
            "planner_route_path.png", "Navidaten-Editor"),
        Section(
            "7. Personalverwaltung",
            """
            • Mitarbeiterregister: Name, Personalnummer, Bus-Passwort, Telefon.
            • Dokumente: Führerschein, FQN, Fahrerkarte – Ablauf und 3-Monats-Kontrollen.
            • Planer-Login pro Mitarbeiter (nur Planer, nicht in App-Export).
            • PDF erstellen: persönliche Fahrer-Einweisung mit Zugangsdaten (separat von dieser Planer-Anleitung).
            """,
            "planner_employees.png", "Personalverwaltung"),
        Section(
            "8. Fahrerdisposition",
            """
            • Kalenderansicht: Fahrer zeitlich Linien/Fahrten zuordnen.
            • FPersV-Hinweise bei fehlenden Qualifikationen.
            • Springt zur Personalverwaltung, wenn ein Fahrer noch nicht angelegt ist.
            • Daten werden in planner_local_roster.json gespeichert und mit Dropbox synchronisiert.
            """,
            "planner_fahrerdispo.png", "Fahrerdisposition"),
        Section(
            "9. Fahrzeugdisposition",
            """
            • Kalenderansicht: Fahrzeuge zeitlich Linien/Fahrten zuordnen.
            • Dispo-Aktiv-Schalter und Zeilenfarben pro Fahrzeug in der Fahrzeugverwaltung.
            • Zusammen mit Fahrerdisposition Grundlage für den Linienbetrieb.
            """,
            "planner_fahrzeugdispo.png", "Fahrzeugdisposition"),
        Section(
            "10. Fahrzeugverwaltung und Mängelkarte",
            """
            • Tab Fahrzeuge: KOM-Name, Telefon, Typ, VIN, HU/SP, Gurte, Klima, Dispo-Farbe.
            • Registrierte Fahrzeuge erscheinen in der Leitstelle und für Fernsteuerung.
            • In der Fahrzeugkarte (wie Leitstelle): Fernziel, Fernroute, Fern-Fahreranmeldung, Gerät sperren/entsperren, Durchsage, Meldungen.
            • Tab Mängelkarte: Meldungen aus der App laden, Status setzen (Neu / In Bearbeitung / Erledigt).
            • Badge bei neuen Mängeln in der Navigation.
            """,
            "planner_vehicles.png", "Fahrzeugverwaltung"),
        Section(
            "11. Dienstvorlagen",
            """
            • Dienstschablonen erstellen, aus Fahrplan importieren (Excel, PDF, CSV).
            • Teildienste, Pausen, Kennzahlen (Dienstlänge, Lohnstunden).
            • PDF-Export Teil 1/2/3 mit Firmenlogo aus Einstellungen.
            • Vorlagen in der Vorlagen-Bibliothek speichern.
            """,
            "planner_duty_templates.png", "Dienstvorlagen-Editor"),
        Section(
            "12. Vorlagen-Bibliothek",
            """
            • Gespeicherte Dienstvorlagen (301, 302, …) anzeigen und als PDF exportieren.
            • Read-only-Ansicht – Bearbeitung über Dienstvorlagen-Menü.
            """,
            "planner_duty_library.png", "Vorlagen-Bibliothek"),
        Section(
            "13. Nachrichten (Vorlagen)",
            """
            • KOM-Nachrichtenvorlagen (messageTemplates) für die Fahrer-App.
            • Mail-Vorlagen (mailTemplates) für Leitstellen-Meldungen.
            • Werden mit routes_export.json bzw. leitstelle_stand.json verteilt.
            """,
            "planner_messages.png", "Nachrichtenvorlagen"),
        Section(
            "14. Zeitwirtschaft",
            """
            • Tablet-Stempelungen aus Dropbox laden und auswerten.
            • Korrekturen und Stornierungen mit Begründung.
            • Monats-PDF pro Mitarbeiter exportieren.
            """,
            "planner_zeitwirtschaft.png", "Zeitwirtschaft Planer"),
        Section(
            "15. SEV-Schilder",
            """
            • NRW-SEV-Schild A3 quer: Linie, Ziel, Haltestellen, Betreiber-Logos.
            • Route importieren, Entwürfe speichern/laden, PDF exportieren.
            """,
            "planner_sev.png", "SEV-Schild-Editor"),
        Section(
            "16. Anzeigen & Hinweise",
            """
            • Zielliste / Außenanzeigen: Wechseltexte und Programme (DS003a, DS021T, DS021neu, …).
            • Datumgesteuerte Hinweise mit Start- und Enddatum.
            • Daten werden an Fahrzeuge mit dem Routenpaket übertragen.
            """,
            "planner_displays.png", "Anzeigen und Hinweise"),
        Section(
            "17. Versand und Dropbox",
            """
            • JSON importieren/exportieren (lokal und Dropbox).
            • routes_export.json = Vollbackup mit Audio; routes_update.json = leichtes Update ohne Tondateien.
            • Routen an Fahrzeuge: Update (Merge), Senden (mit Löschung), Kleines Fahrzeugupdate, Senden + Fernupdate.
            • Fernupdate-Dialog: Wahl zwischen routes_export (Vollbackup) und routes_update (ohne Audio).
            • Planer-Arbeitsstand: planer_workspace.json, Snapshots, planer_ansagen_roh/.
            • Für Leitstelle speichern: leitstelle_stand.json + leitstelle_routes.json (Routen/Fahrwege). routes_update.json nur bei bewusstem Fahrzeug-Versand.
            """,
            "planner_data_transfer.png", "Versand und Dropbox"),
        Section(
            "18. Einstellungen",
            """
            • Dropbox verbinden, Ordnerpfad (/smart öpnv), Verbindungstest.
            • Betrieb wechseln: vorhandenen Betrieb wählen oder neuen anlegen (leerer Stand + neuer Dropbox-Ordner); Planer startet neu.
            • Firmenlogos für Dienstvorlagen-PDF.
            • Einweisungs-PDF-Passwörter (Gerät + Entsperr) für Fahrer-Einweisungen.
            • Planer-Ordner initialisieren (planer_workspace.json / planer_session.json).
            • Unterweisungsanleitung Planer als PDF exportieren (diese Anleitung).
            """,
            "planner_settings.png", "Einstellungen Planer"),
    ];

    private static BriefingSection Section(string title, string body, string imageFile, string caption) =>
        new(title, body, [new BriefingImage(caption, imageFile)]);
}
