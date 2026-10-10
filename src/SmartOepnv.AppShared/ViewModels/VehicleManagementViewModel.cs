using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public enum VehicleManagementButtonState
{
    Idle,
    Success
}

public sealed record DispoRowColorOption(string Hex, string Label);

public partial class VehicleManagementViewModel : ObservableObject, IEditorAreaViewModel
{
    private const int SuccessFeedbackMs = 5000;

    private readonly EditorAreaSyncState _sync = new();
    private string? _loadedFingerprint;
    private CancellationTokenSource? _saveFeedbackCts;
    private CancellationTokenSource? _deleteFeedbackCts;

    public static IReadOnlyList<DispoRowColorOption> DispoRowColorOptions { get; } =
    [
        new(string.Empty, "Standard"),
        new("#FFF9C4", "Gelb (Test)"),
        new("#FFCDD2", "Rosa (Test)"),
        new("#ECEFF1", "Grau"),
        new("#C8E6C9", "Hellgrün"),
        new("#BBDEFB", "Hellblau")
    ];

    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private RegisteredVehicleItem? selectedVehicle;
    [ObservableProperty] private VehicleManagementButtonState saveButtonState = VehicleManagementButtonState.Idle;
    [ObservableProperty] private VehicleManagementButtonState deleteButtonState = VehicleManagementButtonState.Idle;

    public IReadOnlyList<DispoRowColorOption> RowColorOptions => DispoRowColorOptions;

    public ObservableCollection<RegisteredVehicleItem> Vehicles { get; } = [];

    public MaengelkartePlannerViewModel Maengelkarte { get; } = new();

    public VehicleManagementViewModel()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChangesIfDirty);
        }
    }

    public bool HasPendingChanges => _sync.HasPendingChanges;

    public void RefreshFromEditorIfNeeded()
    {
        if (!_sync.ShouldRefresh(Vehicles.Count > 0))
        {
            return;
        }

        RefreshFromEditorCore();
    }

    public void RefreshFromEditor() => RefreshFromEditorCore();

    private void RefreshFromEditorCore()
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
        Maengelkarte.RefreshVehicleFilterOptions();
        var redirectCount = editor.RegisteredVehiclePhoneRedirects.Count;
        StatusMessage =
            $"{Vehicles.Count} Fahrzeuge – lokal bearbeiten; Dropbox-Upload erst beim Abmelden oder „Planer-Arbeitsstand speichern“.";
        _sync.AfterRefresh();
        _loadedFingerprint = ComputeFingerprint();
    }

    public void CommitChangesIfDirty()
    {
        var fingerprint = ComputeFingerprint();
        if (!_sync.ShouldCommit(fingerprint, _loadedFingerprint))
        {
            return;
        }

        CommitChanges(showSuccessFeedback: false);
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

    public void CommitChanges(bool showSuccessFeedback = true)
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        var savedSelectionKey = BuildVehicleSelectionKey(SelectedVehicle);

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
            vehicle.NotifyDisplayLabelChanged();
        }

        editor.ReplaceRegisteredVehicles(Vehicles.Select(CloneForSave).ToList());
        editor.ReplaceRegisteredVehiclePhoneRedirects(redirects);
        AppServices.Routes.ApplyEditorChanges("fahrzeugverwaltung", rebuildEmbeddedMedia: false);
        AppServices.PlannerLocal?.PersistFromEditor(editor);
        Maengelkarte.RefreshVehicleFilterOptions();

        SelectedVehicle = FindVehicleBySelectionKey(savedSelectionKey) ?? Vehicles.FirstOrDefault();

        StatusMessage =
            $"{Vehicles.Count} Fahrzeuge lokal gespeichert – Dropbox-Upload erst beim Abmelden oder „Planer-Arbeitsstand speichern“.";
        _sync.AfterCommit();
        _loadedFingerprint = ComputeFingerprint();

        if (showSuccessFeedback)
        {
            _ = ShowSaveSuccessFeedbackAsync();
        }
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
        _sync.MarkDirty();
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
        _sync.MarkDirty();
        StatusMessage = "Fahrzeug entfernt – „Speichern“ nicht vergessen.";

        _ = ShowDeleteSuccessFeedbackAsync();
    }

    [RelayCommand]
    private void SaveChanges() => CommitChanges();

    private static string? BuildVehicleSelectionKey(RegisteredVehicleItem? vehicle)
    {
        if (vehicle is null)
        {
            return null;
        }

        var phone = RegisteredVehiclesEditor.NormalizePhoneKey(vehicle.PhoneNumber);
        if (phone.Length > 0)
        {
            return $"phone:{phone}";
        }

        var name = vehicle.Name?.Trim() ?? string.Empty;
        return name.Length > 0 ? $"name:{name}" : null;
    }

    private RegisteredVehicleItem? FindVehicleBySelectionKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        if (key.StartsWith("phone:", StringComparison.Ordinal))
        {
            var phone = key["phone:".Length..];
            return Vehicles.FirstOrDefault(
                v => RegisteredVehiclesEditor.NormalizePhoneKey(v.PhoneNumber) == phone);
        }

        if (key.StartsWith("name:", StringComparison.Ordinal))
        {
            var name = key["name:".Length..];
            return Vehicles.FirstOrDefault(
                v => string.Equals(v.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private async Task ShowSaveSuccessFeedbackAsync()
    {
        _saveFeedbackCts?.Cancel();
        _saveFeedbackCts?.Dispose();
        _saveFeedbackCts = new CancellationTokenSource();
        var token = _saveFeedbackCts.Token;

        SaveButtonState = VehicleManagementButtonState.Success;
        try
        {
            await Task.Delay(SuccessFeedbackMs, token).ConfigureAwait(true);
            SaveButtonState = VehicleManagementButtonState.Idle;
        }
        catch (TaskCanceledException)
        {
            // neuer Klick hat Feedback zurückgesetzt
        }
    }

    private async Task ShowDeleteSuccessFeedbackAsync()
    {
        _deleteFeedbackCts?.Cancel();
        _deleteFeedbackCts?.Dispose();
        _deleteFeedbackCts = new CancellationTokenSource();
        var token = _deleteFeedbackCts.Token;

        DeleteButtonState = VehicleManagementButtonState.Success;
        try
        {
            await Task.Delay(SuccessFeedbackMs, token).ConfigureAwait(true);
            DeleteButtonState = VehicleManagementButtonState.Idle;
        }
        catch (TaskCanceledException)
        {
            // neuer Klick hat Feedback zurückgesetzt
        }
    }

    private string ComputeFingerprint() =>
        JsonSerializer.Serialize(Vehicles.Select(v => new
        {
            v.Name,
            v.PhoneNumber,
            v.PersonnelNumber,
            v.Password,
            v.LicenseExpiry,
            v.FqnExpiry,
            v.DriverCardExpiry,
            v.LoginAsMainDevice,
            v.PlannerDetails
        }));

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
        PlannerDetails = v.PlannerDetails?.Clone() ?? new RegisteredVehiclePlannerDetails()
    };
}
