using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Ein Feld der Museums-Ticket-Druckvorlage (mm-Koordinaten).
/// </summary>
public sealed class TicketTemplateFieldItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>logo | ticketName | price | priceLevel | numberChain | staticText | stampStripes</summary>
    private string _type = "staticText";
    public string Type
    {
        get => _type;
        set { if (_type == value) return; _type = value; OnPropertyChanged(); OnPropertyChanged(nameof(TypeLabel)); OnPropertyChanged(nameof(PreviewDisplayText)); OnPropertyChanged(nameof(IsStampStripes)); }
    }

    public bool IsStampStripes =>
        string.Equals(Type, "stampStripes", StringComparison.OrdinalIgnoreCase);

    public string TypeLabel => Type switch
    {
        "logo" => "Logo",
        "ticketName" => "Ticketname",
        "price" => "Preis",
        "priceLevel" => "Preisstufe-Label",
        "numberChain" => "Zahlenkette",
        "staticText" => "Fester Text",
        "stampStripes" => "Stempelstreifen",
        _ => Type
    };

    /// <summary>Text für Planer-Vorschau (Fallback je Typ).</summary>
    public string PreviewDisplayText
    {
        get
        {
            if (IsStampStripes) return string.Empty;
            if (Type == "logo") return "[Logo]";
            if (!string.IsNullOrWhiteSpace(StaticText)) return StaticText;
            return Type switch
            {
                "ticketName" => "Ticketname",
                "price" => "8,00 €",
                "priceLevel" => "Preisstufe",
                "numberChain" => "00001 1234",
                _ => TypeLabel
            };
        }
    }

    private double _x;
    public double X
    {
        get => _x;
        set { if (Math.Abs(_x - value) < 0.001) return; _x = value; OnPropertyChanged(); }
    }

    private double _y;
    public double Y
    {
        get => _y;
        set { if (Math.Abs(_y - value) < 0.001) return; _y = value; OnPropertyChanged(); }
    }

    private double _width = 20;
    public double Width
    {
        get => _width;
        set { if (Math.Abs(_width - value) < 0.001) return; _width = value; OnPropertyChanged(); }
    }

    private double _height = 8;
    public double Height
    {
        get => _height;
        set { if (Math.Abs(_height - value) < 0.001) return; _height = value; OnPropertyChanged(); }
    }

    private double _fontSize = 12;
    public double FontSize
    {
        get => _fontSize;
        set { if (Math.Abs(_fontSize - value) < 0.001) return; _fontSize = value; OnPropertyChanged(); }
    }

    private string _fontWeight = "normal";
    public string FontWeight
    {
        get => _fontWeight;
        set { if (_fontWeight == value) return; _fontWeight = value; OnPropertyChanged(); }
    }

    private string _align = "left";
    public string Align
    {
        get => _align;
        set { if (_align == value) return; _align = value; OnPropertyChanged(); }
    }

    /// <summary>Nur bei staticText / Fallback-Vorschau.</summary>
    private string _staticText = string.Empty;
    public string StaticText
    {
        get => _staticText;
        set { if (_staticText == value) return; _staticText = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewDisplayText)); }
    }

    /// <summary>Dateiname für Logo im Paket (optional).</summary>
    private string _assetKey = string.Empty;
    public string AssetKey
    {
        get => _assetKey;
        set { if (_assetKey == value) return; _assetKey = value; OnPropertyChanged(); }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
