using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartOepnv.AppShared.Pdf;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Employees;

public static class EmployeeBriefingPdfGenerator
{
    private const string PrimaryBlue = "#0D47A1";
    private const string BorderColor = "#4A5F82";
    private const float PageMargin = 32f;
    private const float SectionGap = 10f;
    private const float BriefingImageMaxWidth = 228f;

    static EmployeeBriefingPdfGenerator() =>
        QuestPDF.Settings.License = LicenseType.Community;

    public static void Generate(string outputPath, EmployeeRosterItem employee)
    {
        var created = DateTime.Now;
        var (firstName, lastName) = SplitName(employee.Name);
        var personnel = employee.PersonnelNumber?.Trim() ?? string.Empty;
        var password = employee.Password?.Trim() ?? string.Empty;
        var (devicePassword, unlockPassword) = LoadBriefingPasswordsFromSettings();
        var figureNumber = 1;

        Document.Create(document =>
        {
            document.Page(page => RenderCoverPage(
                page,
                created,
                firstName,
                lastName,
                personnel,
                password,
                devicePassword,
                unlockPassword));

            document.Page(page => RenderManualPages(
                page,
                EmployeeBriefingManualContent.Sections,
                figureNumber,
                created));

            document.Page(page => RenderSignaturePage(page, created));
        }).GeneratePdf(outputPath);
    }

    private static void RenderManualPages(
        PageDescriptor page,
        IReadOnlyList<BriefingSection> sections,
        int startFigureNumber,
        DateTime created)
    {
        ApplyPageFrame(page, created);
        page.DefaultTextStyle(x => PlanerPdfTextStyles.ApplyLight(x).FontSize(10).FontColor(Colors.Black).LineHeight(1.25f));

        page.Header().Element(header =>
            ComposeDynamicPageHeader(header, "Smart-ÖPNV Einweisung – Fahreranleitung"));

        var figureNumber = startFigureNumber;

        page.Content().PaddingTop(2).Column(column =>
        {
            for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
            {
                var section = sections[sectionIndex];
                var topPadding = sectionIndex == 0 ? 0f : SectionGap;
                var sectionFigureNumber = figureNumber;

                column.Item().PaddingTop(topPadding).ShowEntire().Column(sectionCol =>
                {
                    sectionCol.Item().Text(section.Title)
                        .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                        .FontSize(12).SemiBold().FontColor(PrimaryBlue);
                    sectionCol.Item().PaddingTop(3).Text(NormalizeManualBody(section.Body));

                    if (section.Images is not { Count: > 0 } images)
                    {
                        return;
                    }

                    sectionCol.Item().PaddingTop(5).Element(content =>
                    {
                        if (images.Count == 2)
                        {
                            content.Row(row =>
                            {
                                row.Spacing(8);
                                for (var i = 0; i < 2; i++)
                                {
                                    row.RelativeItem().Element(cell =>
                                        ComposeBriefingImageFigure(
                                            cell,
                                            images[i],
                                            sectionFigureNumber + i));
                                }
                            });
                            return;
                        }

                        content.AlignCenter().Element(cell =>
                            ComposeBriefingImageFigure(cell, images[0], sectionFigureNumber));
                    });
                });

                figureNumber += section.Images?.Count ?? 0;
            }
        });
    }

    public static string BuildDefaultFileName(EmployeeRosterItem employee)
    {
        var personnel = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);
        var safeName = string.Join(
            "_",
            (employee.Name ?? string.Empty).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = string.IsNullOrWhiteSpace(personnel) ? "fahrer" : $"PN_{personnel}";
        }

        return $"einweisung_{safeName}.pdf";
    }

    private static void ApplyPageFrame(PageDescriptor page, DateTime created)
    {
        page.Size(PageSizes.A4);
        page.Margin(PageMargin);
        page.DefaultTextStyle(x => PlanerPdfTextStyles.ApplyLight(x).FontSize(10).FontColor(Colors.Black));
        page.Footer().Element(c => PlanerPdfBranding.ComposeStandardFooter(c, created));
    }

    private static void ComposeDynamicPageHeader(IContainer header, string titleLine)
    {
        PlanerPdfBranding.ComposeHeaderWithSmartLogo(header, left =>
        {
            left.Column(column =>
            {
                column.Item().Text(titleLine)
                    .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                    .FontSize(9).SemiBold().FontColor(PrimaryBlue);
                column.Item().PaddingTop(1).Text(text =>
                {
                    text.DefaultTextStyle(PlanerPdfTextStyles.ApplyLight(TextStyle.Default)
                        .FontSize(8)
                        .FontColor(Colors.Grey.Darken1));
                    text.Span("Seite ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });
    }

    private static void RenderCoverPage(
        PageDescriptor page,
        DateTime created,
        string firstName,
        string lastName,
        string personnel,
        string password,
        string devicePassword,
        string unlockPassword)
    {
        ApplyPageFrame(page, created);
        page.Header().Element(header =>
            PlanerPdfBranding.ComposeHeaderWithSmartLogo(header, left =>
            {
                left.AlignMiddle().Column(column =>
                {
                    column.Item().Text("Smart-ÖPNV Einweisung")
                        .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                        .FontSize(20).SemiBold().FontColor(PrimaryBlue);
                    column.Item().PaddingTop(2)
                        .Text($"Erstellt am {created:dd.MM.yyyy}")
                        .FontFamily(PlanerPdfTextStyles.BahnschriftLight)
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            }));

        page.Content().PaddingTop(8).Column(column =>
        {
            column.Item().Border(1).BorderColor(BorderColor).Padding(14).Column(box =>
            {
                box.Item().Text("Mitarbeiterdaten")
                    .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                    .FontSize(13).SemiBold().FontColor(PrimaryBlue);
                box.Item().PaddingTop(8).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(148);
                        columns.RelativeColumn();
                    });

                    AddDataRow(table, "Vorname:", firstName);
                    AddDataRow(table, "Nachname:", lastName);
                    AddDataRow(table, "Personalnummer:", personnel);
                    AddDataRow(table, "App-Passwort:", password);
                    AddDataRow(table, "Gerätepasswort:", devicePassword);
                    AddDataRow(table, "Entsperrpasswort:", unlockPassword);
                });
            });

            column.Item().PaddingTop(16).Text("Fahreranleitung")
                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                .FontSize(14).SemiBold().FontColor(PrimaryBlue);
            column.Item().PaddingTop(6).Text(
                "Die folgenden Seiten beschreiben die sichere und betriebsgerechte Nutzung der Smart-ÖPNV-App " +
                "auf den Fahrzeuggeräten. Bitte lesen Sie alle Abschnitte aufmerksam durch.")
                .LineHeight(1.3f);
        });
    }

    private static void RenderSignaturePage(PageDescriptor page, DateTime created)
    {
        ApplyPageFrame(page, created);

        page.Header().Element(header =>
            ComposeDynamicPageHeader(header, "Smart-ÖPNV Einweisung"));

        page.Content().PaddingTop(12).Border(1.5f).BorderColor(BorderColor).Padding(16).Column(column =>
        {
            column.Item().Text(
                    "Hiermit erkläre ich, dass ich die Unterweisung in allen Punkten der Fahreranleitung verstanden habe.")
                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                .FontSize(11)
                .SemiBold()
                .LineHeight(1.35f);

            column.Item().PaddingTop(20).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().BorderBottom(1).BorderColor(Colors.Black).Height(28);
                    left.Item().PaddingTop(4).Text("Datum / Unterschrift Mitarbeiter")
                        .FontFamily(PlanerPdfTextStyles.BahnschriftLight).FontSize(9);
                });
                row.ConstantItem(20);
                row.RelativeItem().Column(right =>
                {
                    right.Item().BorderBottom(1).BorderColor(Colors.Black).Height(28);
                    right.Item().PaddingTop(4).Text("Datum / Unterschrift Unterweiser")
                        .FontFamily(PlanerPdfTextStyles.BahnschriftLight).FontSize(9);
                });
            });

            column.Item().PaddingTop(18).Text("Datenschutzhinweis")
                .FontFamily(PlanerPdfTextStyles.Bahnschrift)
                .FontSize(10)
                .SemiBold()
                .FontColor(PrimaryBlue);
            column.Item().PaddingTop(6).Text(NormalizeManualBody(PrivacyNoticeText))
                .FontFamily(PlanerPdfTextStyles.BahnschriftLight)
                .FontSize(8.5f)
                .LineHeight(1.3f)
                .FontColor(Colors.Grey.Darken2);
        });
    }

    private static void ComposeBriefingImageFigure(
        IContainer container,
        BriefingImage image,
        int figureNumber)
    {
        container.Column(block =>
        {
            var path = ResolveBriefingAssetPath(image.AssetFileName);
            if (path is null)
            {
                block.Item().Text($"Abb. {figureNumber}: Abbildung nicht verfügbar.")
                    .FontFamily(PlanerPdfTextStyles.BahnschriftLight)
                    .FontSize(7).FontColor(Colors.Grey.Darken1);
                return;
            }

            block.Item()
                .AlignCenter()
                .MaxWidth(BriefingImageMaxWidth)
                .Image(File.ReadAllBytes(path))
                .FitWidth();

            block.Item().PaddingTop(0).AlignCenter()
                .Text($"Abb. {figureNumber}: {image.Caption}")
                .FontFamily(PlanerPdfTextStyles.BahnschriftLight)
                .FontSize(7)
                .FontColor(Colors.Grey.Darken2);
        });
    }

    private static string? ResolveBriefingAssetPath(string fileName)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", "briefing", fileName),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Assets", "briefing", fileName))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string NormalizeManualBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            body.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
    }

    private const string PrivacyNoticeText =
        """
        Die im Rahmen dieser Einweisung und der Nutzung der Smart-ÖPNV-App verarbeiteten personenbezogenen Daten
        (z. B. Name, Personalnummer, Arbeitszeiten, Fahrzeug- und Einsatzdaten) werden ausschließlich zum Betrieb
        des Linienverkehrs, zur Zeiterfassung, zur Disposition und zur technischen Bereitstellung der App verwendet.

        Eine Weitergabe Ihrer Daten an unbefugte Dritte zu Werbe- oder anderen fremden Zwecken erfolgt nicht.
        Zugriff haben nur befugte Stellen Ihres Arbeitgebers bzw. der beauftragten Planung, Werkstatt und Leitstelle,
        soweit dies für den jeweiligen Arbeits- oder Einsatzzweck erforderlich ist.

        Technische Übertragungen (z. B. Synchronisation über Dropbox) dienen nur dem sicheren Austausch betrieblicher
        Fahrzeug- und Einsatzdaten innerhalb des vorgesehenen Systems.

        Sie haben im Rahmen der gesetzlichen Vorgaben das Recht auf Auskunft über die zu Ihrer Person gespeicherten
        Daten sowie auf Berichtigung unrichtiger Angaben. Wenden Sie sich dazu an Ihre Personalverwaltung bzw. Disposition.
        """;

    private static (string DevicePassword, string UnlockPassword) LoadBriefingPasswordsFromSettings()
    {
        if (!AppServices.IsInitialized || AppServices.PlanerAppSettings is null)
        {
            return (string.Empty, string.Empty);
        }

        var settings = AppServices.PlanerAppSettings.Load();
        return (settings.DevicePassword.Trim(), settings.UnlockPassword.Trim());
    }

    private static void AddDataRow(TableDescriptor table, string label, string value)
    {
        table.Cell().PaddingVertical(2).Text(label)
            .FontFamily(PlanerPdfTextStyles.BahnschriftLight).FontSize(9);
        table.Cell().PaddingVertical(2).Text(string.IsNullOrWhiteSpace(value) ? "–" : value)
            .FontFamily(PlanerPdfTextStyles.Bahnschrift).FontSize(9);
    }

    private static (string FirstName, string LastName) SplitName(string? fullName)
    {
        var trimmed = (fullName ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0)
        {
            return (trimmed, string.Empty);
        }

        return (trimmed[..space].Trim(), trimmed[(space + 1)..].Trim());
    }
}
