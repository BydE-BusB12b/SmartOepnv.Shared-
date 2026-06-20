using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartOepnv.AppShared.Pdf;

/// <summary>
/// Schrift-Einstellungen für Planer-PDFs mit zuverlässigem Kopieren in gängigen Viewern (Edge, Chrome, Acrobat).
/// </summary>
public static class PlanerPdfTextStyles
{
    public const string Bahnschrift = "Bahnschrift";
    public const string BahnschriftLight = "Bahnschrift Light";

    public static TextStyle ApplyRegular(TextStyle style) =>
        style
            .FontFamily(Bahnschrift)
            .DisableFontFeature("liga")
            .DisableFontFeature("clig");

    public static TextStyle ApplyLight(TextStyle style) =>
        style
            .FontFamily(BahnschriftLight)
            .DisableFontFeature("liga")
            .DisableFontFeature("clig");

    public static TextStyle Apply(TextStyle style) => ApplyRegular(style);

    public static TextStyle Body(float fontSize = 11f) =>
        ApplyRegular(TextStyle.Default.FontSize(fontSize).FontColor(Colors.Black));

    public static TextStyle BodyLight(float fontSize = 11f) =>
        ApplyLight(TextStyle.Default.FontSize(fontSize).FontColor(Colors.Black));
}
