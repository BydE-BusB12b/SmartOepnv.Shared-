using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Museums-Ticketprodukt im Route-Paket (<c>ticketProducts</c>).
/// </summary>
public sealed class TicketProductItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; OnPropertyChanged(); }
    }

    private int _priceCents;
    public int PriceCents
    {
        get => _priceCents;
        set { if (_priceCents == value) return; _priceCents = value; OnPropertyChanged(); OnPropertyChanged(nameof(PriceEuro)); }
    }

    public decimal PriceEuro
    {
        get => PriceCents / 100m;
        set => PriceCents = (int)Math.Round(value * 100m, MidpointRounding.AwayFromZero);
    }

    private bool _isDefault;
    public bool IsDefault
    {
        get => _isDefault;
        set { if (_isDefault == value) return; _isDefault = value; OnPropertyChanged(); }
    }

    private bool _active = true;
    public bool Active
    {
        get => _active;
        set { if (_active == value) return; _active = value; OnPropertyChanged(); }
    }

    private int _sortOrder;
    public int SortOrder
    {
        get => _sortOrder;
        set { if (_sortOrder == value) return; _sortOrder = value; OnPropertyChanged(); }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
