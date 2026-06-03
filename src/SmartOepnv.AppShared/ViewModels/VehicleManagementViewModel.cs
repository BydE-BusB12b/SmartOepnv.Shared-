using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class VehicleManagementViewModel : ObservableObject
{
    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private RegisteredVehicleItem? selectedVehicle;

    public ObservableCollection<RegisteredVehicleItem> Vehicles { get; } = [];

    public VehicleManagementViewModel()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChanges);
        }
    }

    public void RefreshFromEditor()
    {
        Vehicles.Clear();
        SelectedVehicle = null;

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        foreach (var vehicle in editor.RegisteredVehicles)
        {
            Vehicles.Add(Clone(vehicle));
        }

        SelectedVehicle = Vehicles.FirstOrDefault();
        var redirectCount = editor.RegisteredVehiclePhoneRedirects.Count;
        StatusMessage =
            $"{Vehicles.Count} Fahrzeuge – KOM für die App; Planer-Zusatzdaten und Nummern-Historie ({redirectCount} Umleitung/en) im Paket (Dropbox).";
    }

    /// <summary>Wählt das Fahrzeug mit passender Telefonnummer (nur Ziffern). Keine Aktion, wenn keine Übereinstimmung.</summary>
    public void TrySelectVehicleByNormalizedPhone(string? phoneNormalizedDigitsOnly)
    {
        var key = RegisteredVehiclesEditor.NormalizePhoneKey(phoneNormalizedDigitsOnly);
        if (key.Length == 0)
        {
            return;
        }

        var match = Vehicles.FirstOrDefault(
            v => RegisteredVehiclesEditor.NormalizePhoneKey(v.PhoneNumber) == key);
        if (match is not null)
        {
            SelectedVehicle = match;
        }
    }

    public void CommitChanges()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        var redirects = editor.RegisteredVehiclePhoneRedirects.ToList();
        foreach (var vehicle in Vehicles)
        {
            var baseline = string.IsNullOrWhiteSpace(vehicle.LoadedPhoneNumber)
                ? vehicle.PhoneNumber
                : vehicle.LoadedPhoneNumber;
            RegisteredVehiclesEditor.TryAppendPhoneRedirect(
                redirects,
                baseline,
                vehicle.PhoneNumber);
            vehicle.LoadedPhoneNumber = vehicle.PhoneNumber;
        }

        editor.ReplaceRegisteredVehicles(Vehicles.Select(CloneForSave).ToList());
        editor.ReplaceRegisteredVehiclePhoneRedirects(redirects);
        AppServices.Routes.ApplyEditorChanges("fahrzeugverwaltung");
        AppServices.PlannerLocal?.PersistFromEditor(editor);
        StatusMessage =
            $"{Vehicles.Count} Fahrzeuge im Planer gespeichert (lokal, höchste Priorität) – KOM und Planer-Daten gehen mit Routen-Export/Dropbox.";
    }

    [RelayCommand]
    private void AddVehicle()
    {
        if (AppServices.Routes.Editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var item = new RegisteredVehicleItem
        {
            Name = "Neues Fahrzeug",
            PhoneNumber = string.Empty,
            LoadedPhoneNumber = string.Empty
        };
        Vehicles.Add(item);
        SelectedVehicle = item;
        StatusMessage = "Neues Fahrzeug – Name und Telefonnummer ergänzen, dann speichern.";
    }

    [RelayCommand]
    private void RemoveVehicle()
    {
        if (SelectedVehicle is null)
        {
            StatusMessage = "Bitte zuerst ein Fahrzeug auswählen.";
            return;
        }

        if (AppServices.PlannerLocal is not null)
        {
            AppServices.PlannerLocal.RecordVehicleDeleted(SelectedVehicle);
        }

        var idx = Vehicles.IndexOf(SelectedVehicle);
        Vehicles.Remove(SelectedVehicle);
        SelectedVehicle = Vehicles.Count == 0
            ? null
            : Vehicles[Math.Clamp(idx, 0, Vehicles.Count - 1)];
        StatusMessage = "Fahrzeug entfernt – „Speichern“ nicht vergessen.";
    }

    [RelayCommand]
    private void SaveChanges() => CommitChanges();

    private static RegisteredVehicleItem Clone(RegisteredVehicleItem v)
    {
        var item = CloneCore(v);
        item.LoadedPhoneNumber = string.IsNullOrWhiteSpace(v.LoadedPhoneNumber)
            ? v.PhoneNumber
            : v.LoadedPhoneNumber;
        return item;
    }

    private static RegisteredVehicleItem CloneForSave(RegisteredVehicleItem v)
    {
        var item = CloneCore(v);
        item.LoadedPhoneNumber = v.PhoneNumber;
        return item;
    }

    private static RegisteredVehicleItem CloneCore(RegisteredVehicleItem v) => new()
    {
        Name = v.Name,
        PhoneNumber = v.PhoneNumber,
        PersonnelNumber = v.PersonnelNumber,
        Password = v.Password,
        LicenseExpiry = v.LicenseExpiry,
        FqnExpiry = v.FqnExpiry,
        DriverCardExpiry = v.DriverCardExpiry,
        LoginAsMainDevice = v.LoginAsMainDevice,
        PlannerDetails = v.PlannerDetails.Clone()
    };
}
