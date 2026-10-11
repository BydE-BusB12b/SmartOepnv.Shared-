using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>Ein Wechseltext-Ziel (Front/Seite je 2 Zeilen) wie Slider „Ziel 1–4“ in GPSAnsagen.</summary>
public sealed class OutsideDisplayTextCycle : INotifyPropertyChanged
{
    private string _line1 = string.Empty;
    private string _line2 = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Line1
    {
        get => _line1;
        set => SetField(ref _line1, value);
    }

    public string Line2
    {
        get => _line2;
        set => SetField(ref _line2, value);
    }

    public bool HasContent =>
        !string.IsNullOrWhiteSpace(Line1) || !string.IsNullOrWhiteSpace(Line2);

    public string Preview =>
        string.IsNullOrWhiteSpace(Line2) ? Line1 : $"{Line1} · {Line2}";

    public (string Line1, string Line2) ToGoalPair() => (Line1.Trim(), Line2.Trim());

    public void SetFromPair(string line1, string line2)
    {
        Line1 = line1;
        Line2 = line2;
    }

    public void Clear()
    {
        Line1 = string.Empty;
        Line2 = string.Empty;
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasContent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
    }
}
