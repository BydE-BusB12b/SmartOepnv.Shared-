using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var outDir = args.FirstOrDefault()
             ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "hardware"));
Directory.CreateDirectory(outDir);
var outPath = Path.Combine(outDir, "SmartOepnv_RaspberryPi_Einkaufsliste.pdf");
var created = new DateTime(2026, 11, 5);

Document.Create(document =>
{
    document.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3).LineHeight(1.25f));

        page.Header().Column(h =>
        {
            h.Item().Text("Smart ÖPNV · Fahrerarbeitsplatz").FontSize(10).SemiBold().FontColor(Colors.Blue.Darken2);
            h.Item().Text("Einkaufsliste Raspberry Pi").FontSize(16).SemiBold();
            h.Item().Text("Linux-Gerät im Fahrzeug · Stand 05.11.2026")
                .FontSize(9).FontColor(Colors.Grey.Darken1);
            h.Item().PaddingTop(4).LineHorizontal(1.2f).LineColor(Colors.Blue.Darken2);
        });

        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("docs/hardware/SmartOepnv_RaspberryPi_Einkaufsliste.pdf · ").FontSize(8).FontColor(Colors.Grey.Medium);
            t.Span($"{created:dd.MM.yyyy} · ").FontSize(8).FontColor(Colors.Grey.Medium);
            t.CurrentPageNumber().FontSize(8);
            t.Span(" / ").FontSize(8);
            t.TotalPages().FontSize(8);
        });

        page.Content().PaddingTop(8).Column(col =>
        {
            col.Spacing(6);

            P(col, "Hardware für ein Linux-Gerät im Fahrzeug. Die Android-App auf dem Tablet bleibt davon unberührt. " +
                   "Dieselbe Liste liegt als Textdatei unter docs/hardware/SmartOepnv_RaspberryPi_Einkaufsliste.md.");

            H(col, "Festgelegte Anschlüsse");
            Bullet(col, "Amp2 links = Ansage innen. Amp2 rechts = Ansage außen.");
            Bullet(col, "Bluetooth des Pi = Fahrer (Tastentöne, Eincheckton, VoIP). Ein Bluetooth-Stick ist nicht nötig.");
            Bullet(col, "Mobilfunk und GPS über den PCIe-Anschluss, nicht über USB.");
            Bullet(col, "ANT0 = Mobilfunk-Dachantenne. ANT3 = passive GPS-Antenne.");
            Bullet(col, "Sprache nur per VoIP über die Datenverbindung.");
            Bullet(col, "USB: eine Buchse IBIS-Wandler, eine Buchse RS485-Wandler. Der Pi 5 hat vier USB-A-Buchsen, zwei bleiben frei.");
            Bullet(col, "System auf microSD. Der PCIe-Anschluss ist durch das 5G-HAT belegt.");

            H(col, "1. Rechner und Bildschirm");
            ShopTable(col,
            [
                ["1", "Raspberry Pi 5, 8 GB", "Rechner"],
                ["1", "Offizieller Active Cooler für den Pi 5", "Kühlung im Fahrzeug"],
                ["1", "microSD 64 GB, Klasse A2", "System und Programm"],
                ["1", "Touchdisplay per DSI, Querformat, möglichst groß", "Bedienung ohne USB-Port. Sichere kleine Variante: offizielles Touch Display 2, 7 Zoll."],
                ["1", "RTC-Batterie für den Pi 5", "Uhrzeit nach Spannungsverlust, für die Zeitwirtschaft"]
            ]);
            P(col, "Das offizielle Touch Display 2 braucht neben dem Flachbandkabel ein kurzes Kabel an die Stiftleiste. " +
                   "Der Stapel aus 5G-HAT und Amp2 muss diese Pins noch erreichbar lassen. " +
                   "Ein HDMI-Touchdisplay scheidet aus, wenn der Touch eine USB-Buchse braucht.");

            H(col, "2. Mobilfunk und GPS");
            ShopTable(col,
            [
                ["1", "Waveshare PCIe to 5G HAT+ für den Pi 5, mit Quectel RM520N-GL", "Daten-SIM und GPS über das flache PCIe-Kabel. Europa-Bänder."],
                ["1", "Nano-SIM, nur Daten", "Leitstelle, Dropbox, ITCS, VoIP"],
                ["2", "Adapter IPEX-4 (MHF4) auf SMA, kurz", "ANT0 und ANT3. Bleiben im Gehäuse. Oft im HAT-Set enthalten."],
                ["1", "Dachantenne Mobilfunk, SMA, 50 Ω", "an ANT0"],
                ["1", "Dachantenne GPS, passiv, SMA", "an ANT3, Sicht nach oben"],
                ["2", "Antennenkabel SMA, Länge nach Fahrzeug", "Leitung zum Dach. Zug nur am SMA, nicht am IPEX."]
            ]);
            P(col, "ANT1 und ANT2 sind weitere Mobilfunkantennen und für die Datenverbindung nicht nötig. " +
                   "GPS im RM520N-GL ist vorhanden und wird in der Software eingeschaltet.");

            H(col, "3. Ton");
            ShopTable(col,
            [
                ["1", "HiFiBerry Amp2 (HB-AMP2)", "Ansagen. Schraubklemmen links und rechts. Shop-Orientierung 64,90 €."],
                ["1", "Passiver Lautsprecher 4–8 Ω, innen", "Fahrgastraum, linke Klemme"],
                ["1", "Passiver Lautsprecher 4–8 Ω, außen", "Außenseite, rechte Klemme"],
                ["1", "Lautsprecherlitze, zweiadrig", "Zu beiden Lautsprechern. Links und rechts nicht brücken."],
                ["1", "Bluetooth-Lautsprecher, fest am Fahrerplatz, eigene Stromversorgung", "Tastentöne, Eincheckton, VoIP"]
            ]);
            P(col, "Eine Lautsprecherklemme am Amp2 ist gegenüber dem älteren Amp+ umgepolt. Es gilt der Aufdruck auf der Platine.");

            H(col, "4. Strom aus dem 24-V-Bordnetz");
            ShopTable(col,
            [
                ["1", "DC/DC 24 V auf 18 V, mindestens 4 A", "nur der Amp2. Bei 4-Ω-Lautsprechern 18 V, nicht das rohe Bordnetz. Darüber laufen Pi, Display und die USB-Wandler."],
                ["1", "Sicherungshalter mit Sicherung auf der 24-V-Seite", "vor dem 18-V-Wandler"]
            ]);
            Callout(col, "Am Amp2 gibt es keine 5-V-Brücke zum Ziehen. Die Platine versorgt den Pi fest über die Stiftleiste. " +
                         "Display und USB-Wandler hängen an derselben 5-V-Leitung. Ein zweites 5-V-Netzteil darf dort nicht hin: " +
                         "nicht an den USB-C-Anschluss des Pi, nicht an die 5-V-Pins und nicht ans Displaykabel. " +
                         "Ein Wandler 24 V auf 5 V kommt nur dazu, wenn der zusätzliche Stromeingang der 5G-Platine von diesen Pins getrennt ist. " +
                         "Sonst läuft das Mobilfunkmodul mit über den Amp2. Dessen 5 V sind für Pi, Display und Mobilfunk zusammen knapp.");

            H(col, "5. Nur für den Prüfplatz an 230 V");
            ShopTable(col,
            [
                ["1", "Mean Well GST60A18-P1J, 18 V", "Amp2 am Schreibtisch, damit läuft auch der Pi. Shop-Orientierung 19,90 €. Nicht das Fahrzeugnetzteil."],
                ["1", "Offizielles Netzteil Raspberry Pi 5, 27 W, USB-C", "Pi am Schreibtisch, nur solange der Amp2 nicht aufgesteckt ist"]
            ]);

            H(col, "6. Fahrzeugbus");
            ShopTable(col,
            [
                ["1", "USB-IBIS-Wandler", "IBIS. Belegt eine USB-Buchse."],
                ["1", "USB-RS485-Wandler", "RS485. Belegt eine USB-Buchse."]
            ]);

            H(col, "7. Mechanik");
            ShopTable(col,
            [
                ["1", "GPIO-Stacking-Header 40-polig, extralang", "Amp2 sitzt über dem 5G-HAT. Abstand vor dem Einbau prüfen."],
                ["1", "Montageplatte oder Fahrzeuggehäuse mit Lüftung", "Pi, beide Aufsteckplatinen, Klemmen, SMA-Anschlüsse"],
                ["1", "Abstandshalter und Schrauben passend zum Stapel", "Befestigung"]
            ]);
            P(col, "Ein geschlossenes Standardgehäuse für den nackten Pi passt nicht über 5G-HAT und Amp2.");

            H(col, "Nicht kaufen");
            Bullet(col, "kein USB-LTE-Stick und kein USB-GPS");
            Bullet(col, "kein GSM-Modul für Telefonate");
            Bullet(col, "kein zweites Audio-HAT und kein NVMe-HAT");
            Bullet(col, "kein HDMI-Touch, der einen USB-Port für den Touch braucht");
            Bullet(col, "kein Bluetooth-Stick");
        });
    });
}).GeneratePdf(outPath);

Console.WriteLine(outPath);

static void H(ColumnDescriptor col, string title) =>
    col.Item().PaddingTop(6).Text(title).FontSize(13).SemiBold().FontColor(Colors.Blue.Darken3);

static void P(ColumnDescriptor col, string text) =>
    col.Item().Text(text).FontSize(9.5f);

static void Bullet(ColumnDescriptor col, string text) =>
    col.Item().PaddingLeft(6).Text("•  " + text).FontSize(9.5f);

static void Callout(ColumnDescriptor col, string text) =>
    col.Item().Background(Colors.Blue.Lighten5).Border(0.5f).BorderColor(Colors.Blue.Lighten2)
        .Padding(8).Text(text).FontSize(9).FontColor(Colors.Blue.Darken4);

static void ShopTable(ColumnDescriptor col, string[][] rows)
{
    col.Item().Table(t =>
    {
        t.ColumnsDefinition(c =>
        {
            c.ConstantColumn(36);
            c.RelativeColumn(5);
            c.RelativeColumn(6);
        });
        t.Header(h =>
        {
            foreach (var header in new[] { "Anz.", "Teil", "Wofür" })
                h.Cell().Background(Colors.Blue.Darken3).Padding(3)
                    .Text(header).FontSize(8).SemiBold().FontColor(Colors.White);
        });
        var alt = false;
        foreach (var row in rows)
        {
            var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
            alt = !alt;
            for (var i = 0; i < row.Length; i++)
                t.Cell().Background(bg).BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten2)
                    .Padding(3).Text(row[i]).FontSize(8);
        }
    });
}
