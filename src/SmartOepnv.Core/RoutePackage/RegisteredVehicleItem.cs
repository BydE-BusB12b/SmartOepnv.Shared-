using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>KOM-/Fahrzeug-Gerät (registeredVehicles) – kompatibel zur Android-App.</summary>
public sealed class RegisteredVehicleItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            var next = value ?? string.Empty;
            if (_name == next)
            {
                return;
            }

            _name = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    private string _phoneNumber = string.Empty;
    public string PhoneNumber
    {
        get => _phoneNumber;
        set
        {
            var next = value ?? string.Empty;
            if (_phoneNumber == next)
            {
                return;
            }

            _phoneNumber = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    public string PersonnelNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string LicenseExpiry { get; set; } = string.Empty;
    public string FqnExpiry { get; set; } = string.Empty;
    public string DriverCardExpiry { get; set; } = string.Empty;
    public bool LoginAsMainDevice { get; set; }

    /// <summary>Nur Planer – wird nicht in registeredVehicles an die App übertragen.</summary>
    public RegisteredVehiclePlannerDetails PlannerDetails { get; set; } = new();

    /// <summary>Telefonnummer beim letzten Laden/Speichern (Planer: Erkennung von Nummernwechsel).</summary>
    public string LoadedPhoneNumber { get; set; } = string.Empty;

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Name)
            ? PhoneNumber
            : string.IsNullOrWhiteSpace(PhoneNumber)
                ? Name
                : $"{Name} – {PhoneNumber}";

    public void NotifyDisplayLabelChanged() => OnPropertyChanged(nameof(DisplayLabel));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
