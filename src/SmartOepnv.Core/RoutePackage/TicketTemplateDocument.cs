using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Museums-Ticket-Druckvorlage im Route-Paket (<c>ticketTemplate</c>).
/// Positionen in Millimetern – App rendert Bitmap für ESC/POS.
/// </summary>
public sealed class TicketTemplateDocument : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Version { get; set; } = 1;

    private double _pageWidthMm = 58;
    public double PageWidthMm
    {
        get => _pageWidthMm;
        set { if (Math.Abs(_pageWidthMm - value) < 0.001) return; _pageWidthMm = value; OnPropertyChanged(); }
    }

    private double _pageHeightMm = 50;
    public double PageHeightMm
    {
        get => _pageHeightMm;
        set { if (Math.Abs(_pageHeightMm - value) < 0.001) return; _pageHeightMm = value; OnPropertyChanged(); }
    }

    private string _backgroundColor = "#FFFFFF";
    public string BackgroundColor
    {
        get => _backgroundColor;
        set { if (_backgroundColor == value) return; _backgroundColor = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TicketTemplateFieldItem> Fields { get; } = [];

    /// <summary>Dateiname → PNG/JPEG-Bytes (Base64 im JSON unter logoAssets).</summary>
    public Dictionary<string, byte[]> LogoAssets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Standardlayout angelehnt an Verbund-Muster (58×50 mm Thermopapier):
    /// Inhalt links, freier Stempelbereich rechts (nur Trennlinie), Logo, Preis,
    /// Preisstufe, Entwerter-Hinweis, Zahlenkette unten.
    /// </summary>
    public static TicketTemplateDocument CreateDefault()
    {
        var doc = new TicketTemplateDocument
        {
            Version = 1,
            PageWidthMm = 58,
            PageHeightMm = 50,
            BackgroundColor = "#FFFFFF"
        };
        // Stempel-/Entwerterbereich rechts: frei für Entwerter, Trennlinie am linken Feldrand
        doc.Fields.Add(new TicketTemplateFieldItem
        {
            Id = "stampStripes",
            Type = "stampStripes",
            X = 46, Y = 1, Width = 11, Height = 48
        });
        doc.Fields.Add(new TicketTemplateFieldItem
        {
            Id = "logo",
            Type = "logo",
            X = 2, Y = 2, Width = 12, Height = 10,
            AssetKey = "ticket_logo.png"
        });
        doc.Fields.Add(new TicketTemplateFieldItem
        {
            Id = "ticketName",
            Type = "ticketName",
            X = 15, Y = 3, Width = 30, Height = 8,
            FontSize = 14, FontWeight = "bold", Align = "center",
            StaticText = "Ticketname"
        });
        doc.Fields.Add(new TicketTemplateFieldItem
        {
            Id = "price",
            Type = "price",
            X = 4, Y = 14, Width = 40, Height = 14,
            FontSize = 22, FontWeight = "bold", Align = "left",
            StaticText = "0,00 €"
        });
        doc.Fields.Add(new TicketTemplateFieldItem
        {
            Id = "priceLevel",
            Type = "priceLevel",
            X = 4, Y = 28, Width = 24, Height = 5,
            FontSize = 8, FontWeight = "bold", Align = "left",
            StaticText = "Preisstufe"
        });
        doc.Fields.Add(new TicketTemplateFieldItem
        {
            Id = "hint",
            Type = "staticText",
            X = 4, Y = 34, Width = 40, Height = 5,
            FontSize = 7, Align = "left",
            StaticText = "Nur gültig mit Entwerteraufdruck"
        });
        doc.Fields.Add(new TicketTemplateFieldItem
        {
            Id = "numberChain",
            Type = "numberChain",
            X = 4, Y = 42, Width = 40, Height = 5,
            FontSize = 9, Align = "left",
            StaticText = "00001 0000"
        });
        return doc;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
