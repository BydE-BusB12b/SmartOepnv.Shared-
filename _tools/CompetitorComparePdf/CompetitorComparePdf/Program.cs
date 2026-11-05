using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var outDir = args.FirstOrDefault(a => !a.Equals("matrix", StringComparison.OrdinalIgnoreCase)
                                     && !a.Equals("full", StringComparison.OrdinalIgnoreCase))
             ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs"));
Directory.CreateDirectory(outDir);

var mode = args.Any(a => a.Equals("matrix", StringComparison.OrdinalIgnoreCase)) ? "matrix" : "full";
var created = DateTime.Now;

if (mode == "matrix")
{
    var outPath = Path.Combine(outDir, "SmartOepnv_Vergleich_Matrix_Positionierung.pdf");
    GenerateMatrixPdf(outPath, created);
    Console.WriteLine(outPath);
}
else
{
    var outPath = Path.Combine(outDir, "SmartOepnv_Vergleich_Bordrechner_ITCS.pdf");
    GenerateFullPdf(outPath, created);
    Console.WriteLine(outPath);
}

static void GenerateMatrixPdf(string outPath, DateTime created)
{
    Document.Create(document =>
    {
        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor(Colors.Grey.Darken3));

            page.Header().Column(h =>
            {
                h.Item().Text("Smart ÖPNV").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);
                h.Item().Text("Vergleich: Positionierung & Funktionsmatrix").FontSize(16).SemiBold()
                    .FontColor(Colors.Black);
                h.Item().Text("IVU · INIT · Trapeze/Systemtechnik · SIMA · Smart ÖPNV (Stand " +
                             $"{created:dd.MM.yyyy})")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Blue.Darken2);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span($"Erstellt {created:dd.MM.yyyy HH:mm} · ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontSize(8);
                t.Span(" / ").FontSize(8);
                t.TotalPages().FontSize(8);
            });

            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(8);

                col.Item().Text("1. Positionierung").FontSize(13).SemiBold().FontColor(Colors.Blue.Darken3);

                SimpleTable(col,
                    ["Anbieter", "Typisches Paket", "Position zu Smart ÖPNV"],
                    [
                        ["IVU", "IVU.suite: Planung → Dispo → ITCS → Ticketing → Abrechnung; Hardware IVU.box / ticket.box + Gateway", "Vollsuite aus einer Hand; Ticket + LSA/CAN/ITxPT stark"],
                        ["INIT", "MOBILE-ITCS + COPILOTpc/EVENDpc: Leitsystem, Zählung, Ansagen, Ticketdrucker, Incident/CEP", "Klassischer Groß-ITCS, Hardware-OEM, Stadtverkehre"],
                        ["Trapeze (+ Systemtechnik / Modaxo)", "LIO/ITCS, IDR-Bordrechner/Router, FGI, V2X/LSA, BI, E-Bus", "Stark Fahrzeugnetz + Infrastruktur (LSA/Weiche), Bahn+Bus"],
                        ["SIMA / kleinere", "Android-Box + Verbundtarif + VDV 453/454", "Näher am Tablet-Ansatz, aber mit Verkauf/Verbund"],
                        ["Smart ÖPNV", "Tablet-ITCS + Dropbox-Sync + Planer/Leitstelle; Dual-USB IBIS+Mobitec; Bondrucker-Ticketing im Aufbau", "Stark FGI/IBIS/Mobitec/Navi/Dispo; Ticket rudimentär; Standards/Aktorik dünn"]
                    ],
                    fontSize: 8);

                col.Item().PaddingTop(10).Text("2. Funktionsmatrix (vereinfacht)")
                    .FontSize(13).SemiBold().FontColor(Colors.Blue.Darken3);
                col.Item().Text("● = Kernprodukt · ○ = je nach Modul / im Aufbau · – = praktisch fehlend · P = Partner/optional")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);

                SimpleTable(col,
                    ["Thema", "IVU", "INIT", "Trapeze", "Smart"],
                    [
                        ["ITCS Live / Leitstelle", "●", "●", "●", "●*"],
                        ["Planung / Umlauf / Dispo", "●", "●", "●", "●"],
                        ["Außen-/Innenanzeige IBIS", "●", "●", "●", "●"],
                        ["Mobitec RS485 / Dual-USB", "○", "○", "○", "●"],
                        ["IBIS über WLAN (Serial-over-IP)", "○", "○", "○", "●"],
                        ["GPS-Ansagen / ELA", "●", "●", "●", "●"],
                        ["Fahrer-Navi Linienweg", "○/●", "○", "●", "●"],
                        ["Entwerter", "●", "●", "●", "●"],
                        ["Ticketverkauf Papier/Bondrucker", "●", "●", "●/P", "○"],
                        ["Ticketverkauf / VDV-KA", "●", "●", "●/P", "–"],
                        ["LSA-Priorität / V2X", "●", "●", "●", "–"],
                        ["Fahrgastzählung", "●", "●", "●", "–"],
                        ["VDV 453/454 → Verbund-DFI", "●", "●", "●", "–"],
                        ["ITxPT / IBIS-IP (VDV 301)", "●", "●", "●", "Teil"],
                        ["VoIP / Funk Leitstelle", "●", "●", "●", "●"],
                        ["Anschlusssicherung", "●", "●", "●", "–"],
                        ["E-Bus Lade-/Depot-Mgmt", "●", "●", "●", "–"],
                        ["Zertifizierte Einbau-HW", "●", "●", "●", "Tablet"],
                        ["Preis / Time-to-Market KMU", "hoch", "hoch", "hoch", "günstig"]
                    ],
                    fontSize: 8f);

                col.Item().PaddingTop(4).Text(
                        "* Smart ÖPNV: Live über Dropbox, nicht als VDV-RBL-Ausgang (453/454). " +
                        "Ticket ○ = Bondrucker + Planer-Vorlagen, kein VDV-KA.")
                    .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);

                col.Item().PaddingTop(10).Background(Colors.Blue.Lighten5).Padding(8).Text(t =>
                {
                    t.Span("Einordnung: ").SemiBold();
                    t.Span("IVU, INIT und Trapeze verkaufen eine zertifizierte Gesamtarchitektur " +
                           "(Hardware + Ticket + Infrastruktur + Verbund). Smart ÖPNV liefert ein " +
                           "schlankes ITCS+FGI+Planer-Paket auf Android – nah an „COPILOT/IVU.box ohne VDV-KA und ohne Gateway“, " +
                           "dafür mit starker Navi-, Dual-Bus-Anzeigen- und Planungsseite; Bondrucker-Ticketing im Aufbau.");
                });
            });
        });
    }).GeneratePdf(outPath);
}

static void GenerateFullPdf(string outPath, DateTime created)
{
    Document.Create(document =>
    {
        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

            page.Header().Column(h =>
            {
                h.Item().Text("Smart ÖPNV").FontSize(11).SemiBold().FontColor(Colors.Blue.Darken2);
                h.Item().Text("Vergleich Bordrechner / ITCS").FontSize(18).SemiBold().FontColor(Colors.Black);
                h.Item().Text($"Fehlende Funktionen · Abgleich IVU · INIT · Trapeze/Systemtechnik · SIMA · Stand {created:dd.MM.yyyy}")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor(Colors.Blue.Darken2);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span($"Erstellt {created:dd.MM.yyyy HH:mm} · ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontSize(8);
                t.Span(" / ").FontSize(8);
                t.TotalPages().FontSize(8);
            });

            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(10);

                Section(col, "1. Was käufliche Bordrechner oft komplett mitbringen – und bei Smart ÖPNV fehlt bzw. nur teilweise da ist");

                col.Item().Text(
                    "Kommerzielle Bordrechner (INIT, IVU, Mentz, SIMA, Trapeze u. a.) decken häufig ganze Fahrzeug- und Verbund-Stacks ab. " +
                    "Bei Smart ÖPNV fehlen bzw. sind nur teilweise abgedeckt:");

                BulletBlock(col, "Erlös / Ticketing",
                [
                    "Im Aufbau: Bondrucker + Planer-Ticketvorlagen/Produkte/Logo (Papierbeleg am Fahrerarbeitsplatz)",
                    "Noch fehlend: VDV-KA / E-Ticketing (Ausgabe und Kontrolle, SAM/Sicherheitsmodule)",
                    "Noch fehlend: Schicht-/Fahrgeldabrechnung, Rückgeld, Erstattung, Verspätungsbeleg als Kassensystem",
                    "Vorhanden parallel: Entwerter/Wabe/Razzia – getrennt vom Verkauf"
                ]);

                BulletBlock(col, "Fahrzeugperipherie / Betrieb",
                [
                    "LSA-Priorität (Lichtsignalbeeinflussung)",
                    "Automatische Fahrgastzählung (AFZS, Türsensorik)",
                    "Kneeling / Rampe / Türfreigabe als echte Fahrzeugsteuerung",
                    "Video- / Kamerasystem-Anbindung",
                    "CAN-/Fahrzeugdiagnose, Odometer, formelle Türkontakte als Systemkern"
                ]);

                BulletBlock(col, "Standards / Außenwelt",
                [
                    "Volle VDV 301 / ITxPT-Dienstewelt (vorhanden: IBIS-IP-Provider-Teile, nicht Vollsuite)",
                    "VDV 453/454-Echtzeitlieferung an Verbund-DFI / HAFAS / DEFAS als eigener RBL-Ausgang",
                    "Formelle Anschlusssicherung (Wartezeit, Anschlussbruch an Leitstelle/Fahrzeug)"
                ]);

                BulletBlock(col, "Weitere typische Suite-Module",
                [
                    "Onboard-Infotainment (Werbung, News, Fahrgast-WLAN-Portal)",
                    "Rufbus / Bedarfsverkehr / AST-Buchung am Gerät",
                    "Robustes Industrieterminal-Ökosystem (Kartenleser, festes Tastenfeld) statt Tablet-only"
                ]);

                col.Item().Background(Colors.Blue.Lighten5).Padding(8).Text(t =>
                {
                    t.Span("Kurzfassung: ").SemiBold();
                    t.Span("Smart ÖPNV ist stark bei FGI, IBIS (USB dual + WLAN Serial-over-IP + IBIS-IP-Teile), Mobitec-RS485, " +
                           "Ansagen, Entwerter, Navi, Leitstelle, Dispo und Prüfung. Bondrucker-Ticketing ist im Aufbau. " +
                           "Was käufliche Systeme als komplettes Bordrechner-Paket verkaufen und hier noch fehlt, ist vor allem " +
                           "VDV-KA, LSA, Zählung, Fahrzeugaktorik und Verbund-Echtzeit-Standards (453/454).");
                });
            });
        });

        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

            page.Header().Column(h =>
            {
                h.Item().Text("Smart ÖPNV · Vergleich Bordrechner / ITCS").FontSize(11).SemiBold()
                    .FontColor(Colors.Blue.Darken2);
                h.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span($"Erstellt {created:dd.MM.yyyy HH:mm} · ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontSize(8);
                t.Span(" / ").FontSize(8);
                t.TotalPages().FontSize(8);
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(10);

                Section(col, "2. Vergleich mit IVU, INIT, Trapeze / Systemtechnik, SIMA");

                col.Item().Text("Positionierung").SemiBold().FontSize(11).FontColor(Colors.Black);

                SimpleTable(col,
                    ["Anbieter", "Typisches Paket", "Position zu Smart ÖPNV"],
                    [
                        ["IVU", "IVU.suite: Planung → Dispo → ITCS → Ticketing → Abrechnung; Hardware IVU.box / ticket.box + Gateway", "Vollsuite aus einer Hand; Ticket + LSA/CAN/ITxPT stark"],
                        ["INIT", "MOBILE-ITCS + COPILOTpc/EVENDpc: Leitsystem, Zählung, Ansagen, Ticketdrucker, Incident/CEP", "Klassischer Groß-ITCS, Hardware-OEM, Stadtverkehre"],
                        ["Trapeze (+ Systemtechnik / Modaxo)", "LIO/ITCS, IDR-Bordrechner/Router, FGI, V2X/LSA, BI, E-Bus", "Stark Fahrzeugnetz + Infrastruktur (LSA/Weiche), Bahn+Bus"],
                        ["SIMA / kleinere", "Android-Box + Verbundtarif + VDV 453/454", "Näher am Tablet-Ansatz, aber mit Verkauf/Verbund"],
                        ["Smart ÖPNV", "Tablet-ITCS + Dropbox + Planer/Leitstelle; Dual-USB IBIS|RS485; WLAN-IBIS; Bondrucker ○", "Stark FGI/Anzeigen/Navi/Dispo; Ticket/Standards/Aktorik dünn"]
                    ]);

                col.Item().PaddingTop(6).Text("Funktionsmatrix (vereinfacht)").SemiBold().FontSize(11).FontColor(Colors.Black);
                col.Item().Text("● = Kernprodukt · ○ = Modul / im Aufbau · – = bei Smart ÖPNV praktisch fehlend · P = Partner")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);

                SimpleTable(col,
                    ["Thema", "IVU", "INIT", "Trapeze", "Smart"],
                    [
                        ["ITCS Live / Leitstelle", "●", "●", "●", "●*"],
                        ["Planung / Umlauf / Dispo", "●", "●", "●", "●"],
                        ["Außen-/Innenanzeige IBIS", "●", "●", "●", "●"],
                        ["Mobitec RS485 / Dual-USB", "○", "○", "○", "●"],
                        ["IBIS über WLAN (Serial-over-IP)", "○", "○", "○", "●"],
                        ["GPS-Ansagen / ELA", "●", "●", "●", "●"],
                        ["Fahrer-Navi Linienweg", "○/●", "○", "●", "●"],
                        ["Entwerter", "●", "●", "●", "●"],
                        ["Ticketverkauf Papier/Bondrucker", "●", "●", "●/P", "○"],
                        ["Ticketverkauf / VDV-KA", "●", "●", "●/P", "–"],
                        ["LSA-Priorität / V2X", "●", "●", "●", "–"],
                        ["Fahrgastzählung", "●", "●", "●", "–"],
                        ["VDV 453/454 → Verbund-DFI", "●", "●", "●", "–"],
                        ["ITxPT / IBIS-IP (VDV 301)", "●", "●", "●", "Teil"],
                        ["VoIP / Funk Leitstelle", "●", "●", "●", "●"],
                        ["Anschlusssicherung", "●", "●", "●", "–"],
                        ["E-Bus Lade-/Depot-Mgmt", "●", "●", "●", "–"],
                        ["Zertifizierte Einbau-HW", "●", "●", "●", "Tablet"],
                        ["Preis / Time-to-Market KMU", "hoch", "hoch", "hoch", "günstig"]
                    ],
                    fontSize: 8);

                col.Item().Text("* Live über Dropbox, nicht VDV-RBL. Ticket ○ = Bondrucker + Vorlagen, kein VDV-KA.")
                    .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
            });
        });

        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

            page.Header().Column(h =>
            {
                h.Item().Text("Smart ÖPNV · Vergleich Bordrechner / ITCS").FontSize(11).SemiBold()
                    .FontColor(Colors.Blue.Darken2);
                h.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span($"Erstellt {created:dd.MM.yyyy HH:mm} · ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontSize(8);
                t.Span(" / ").FontSize(8);
                t.TotalPages().FontSize(8);
            });

            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(10);

                Section(col, "3. Stärken der großen Anbieter");
                Numbered(col,
                [
                    "Erlös: Verkauf, Kontrolle, Verbundabrechnung, EMV/Account-based (IVU.ticket, EVENDpc, SIMA).",
                    "Fahrzeug als System: Gateway/CAN/Türen/Odometer, LSA, Zählung, Weiche (IVU.box.gateway, Trapeze IDR, INIT COPILOT).",
                    "Verbund-Welt: VDV 453/454 an DFI/HAFAS/DEFAS – nicht nur eigene Leitstellenkarte.",
                    "ITCS-Tiefe: Anschlusssicherung, Incident/CEP, mandantenfähige Großleitstellen, Bahn-Zertifizierung.",
                    "Hardware-Lebenszyklus: 7–15 Jahre Einbau, Mischbetrieb Generationen, Serviceverträge."
                ]);

                Section(col, "4. Relative Stärken von Smart ÖPNV (aktueller Stand)");
                Numbered(col,
                [
                    "Linien-Navi und Offline-Guidance oft besser/flexibler als reine ITCS-Ortung.",
                    "Anzeigen-Vielfalt pragmatisch: IBIS (USB-CH340), Mobitec (eigener USB-RS485, 4800 8N1), Annax/Gorba u. a.; Dual-Bus-Status in der App.",
                    "IBIS-Transport flexibel: klassisch USB, Serial-over-IP über WLAN (z. B. Teltonika), IBIS-IP VDV-301-Provider-Teile.",
                    "Eigener Planer (Umlauf-PDF, Bildfahrplan, Dispo, Zeitwirtschaft, Ticketvorlagen) ohne Suite-Lizenz.",
                    "Fernsteuerung, Fahrtenprüfung und VoIP bereits Leitstellen-nah, ohne Großprojekt.",
                    "Kosten und Iterationsgeschwindigkeit – für Mittelbetriebe oft entscheidender als ITxPT-Vollausbau."
                ]);

                Section(col, "5. Einordnung");
                col.Item().Background(Colors.Grey.Lighten3).Padding(10).Text(
                    "IVU, INIT und Trapeze verkaufen eine zertifizierte Gesamtarchitektur " +
                    "(Hardware + Ticket + Infrastruktur + Verbund). Smart ÖPNV liefert praktisch ein " +
                    "schlankes ITCS+FGI+Planer-Paket auf Android – nah an „COPILOT/IVU.box ohne VDV-KA und ohne Gateway“, " +
                    "dafür mit starker Navi-, Dual-Bus-Anzeigen- und Planungsseite; Bondrucker-Ticketing im Aufbau.");

                col.Item().PaddingTop(8).Text("Nächste sinnvolle Prioritäten für Ausschreibungen (Orientierung)")
                    .SemiBold().FontSize(11).FontColor(Colors.Black);
                Numbered(col,
                [
                    "VDV 453/454-Ausgang (Verbund-/DFI-Anbindung) – oft Pflicht in Ausschreibungen.",
                    "Bondrucker-Ticketing ausbauen (Abrechnung/Schicht) bzw. VDV-KA über Partner – hoher Erlösbedarf.",
                    "LSA-Priorität – regional sehr wertig, aber infrastrukturell abhängig.",
                    "Fahrgastzählung – Auswertung und Förderfähigkeit; Hardware-Partner nötig.",
                    "ITxPT / volleres IBIS-IP – Zukunftssicherheit und Ausschreibungs-Punkte."
                ]);

                col.Item().PaddingTop(12).Text("Quellenhinweis").SemiBold().FontSize(9);
                col.Item().Text(
                    "Abgleich basiert auf öffentlich verfügbaren Produktbeschreibungen (IVU.suite/IVU.box, INIT MOBILE-ITCS/COPILOTpc, " +
                    "Trapeze LIO/IDR, SIMA.BORDRECHNER) und dem Funktionsinventar von Smart ÖPNV (GPSAnsagen + Planer/Leitstelle), " +
                    $"Stand {created:dd.MM.yyyy}. Modulumfänge der Anbieter variieren je Projekt und Vertrag.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
    }).GeneratePdf(outPath);
}

static void Section(ColumnDescriptor col, string title)
{
    col.Item().PaddingTop(4).Text(title).FontSize(13).SemiBold().FontColor(Colors.Blue.Darken3);
}

static void BulletBlock(ColumnDescriptor col, string title, string[] items)
{
    col.Item().Text(title).SemiBold().FontColor(Colors.Black);
    foreach (var item in items)
    {
        col.Item().PaddingLeft(8).Text($"•  {item}");
    }
}

static void Numbered(ColumnDescriptor col, string[] items)
{
    for (var i = 0; i < items.Length; i++)
    {
        col.Item().PaddingLeft(4).Text($"{i + 1}.  {items[i]}");
    }
}

static void SimpleTable(ColumnDescriptor col, string[] headers, string[][] rows, float fontSize = 8.5f)
{
    col.Item().Table(table =>
    {
        table.ColumnsDefinition(columns =>
        {
            for (var i = 0; i < headers.Length; i++)
            {
                if (i == 0)
                    columns.RelativeColumn(headers.Length == 3 ? 1.2f : 1.6f);
                else if (headers.Length == 3 && i == 1)
                    columns.RelativeColumn(1.6f);
                else
                    columns.RelativeColumn(1f);
            }
        });

        table.Header(header =>
        {
            foreach (var h in headers)
            {
                header.Cell().Background(Colors.Blue.Darken2).Padding(4)
                    .Text(h).FontSize(fontSize).SemiBold().FontColor(Colors.White);
            }
        });

        var alt = false;
        foreach (var row in rows)
        {
            var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
            alt = !alt;
            foreach (var cell in row)
            {
                table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                    .Padding(3).Text(cell).FontSize(fontSize);
            }
        }
    });
}
