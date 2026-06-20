using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Employees;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class EmployeesViewModel : ObservableObject, IEditorAreaViewModel
{
    private readonly EditorAreaSyncState _sync = new();
    private string? _loadedFingerprint;

    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private EmployeeRosterItem? selectedEmployee;

    public ObservableCollection<EmployeeRosterItem> Employees { get; } = [];

    public EmployeesViewModel()
    {
        if (AppServices.IsInitialized)
        {
            AppServices.RegisterFlushBeforeExport(CommitChangesIfDirty);
        }
    }

    public bool HasPendingChanges => _sync.HasPendingChanges;

    public void TrySelectEmployeeByPersonnelNumber(string? personnelDigits)
    {
        if (string.IsNullOrWhiteSpace(personnelDigits))
        {
            return;
        }

        var norm = EmployeeRosterItem.NormalizePersonnelDigits(personnelDigits);
        var match = Employees.FirstOrDefault(e =>
            EmployeeRosterItem.NormalizePersonnelDigits(e.PersonnelNumber) == norm);
        if (match is not null)
        {
            SelectedEmployee = match;
        }
    }

    public void TrySelectEmployeeByDispoKey(string? driverKey)
    {
        if (string.IsNullOrWhiteSpace(driverKey))
        {
            return;
        }

        var match = Employees.FirstOrDefault(e => EmployeeDispoKeys.KeysMatch(driverKey, e));
        if (match is not null)
        {
            SelectedEmployee = match;
        }
    }

    public void RefreshFromEditorIfNeeded()
    {
        if (!_sync.ShouldRefresh(Employees.Count > 0))
        {
            return;
        }

        RefreshFromEditorCore();
    }

    public void RefreshFromEditor() => RefreshFromEditorCore();

    private void RefreshFromEditorCore()
    {
        Employees.Clear();
        SelectedEmployee = null;

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen – bitte unter Übersicht importieren.";
            return;
        }

        foreach (var employee in editor.Employees)
        {
            Employees.Add(Clone(employee));
        }

        SelectedEmployee = Employees.FirstOrDefault();
        StatusMessage = $"{Employees.Count} Mitarbeiter im Register (employeeRoster).";
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

        CommitChanges();
    }

    public void CommitChanges()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        editor.ReplaceEmployees(Employees.Select(Clone).ToList());
        AppServices.Routes.ApplyEditorChanges("fahrer");
        AppServices.PlannerLocal?.PersistFromEditor(editor);
        StatusMessage =
            $"{Employees.Count} Mitarbeiter im Planer gespeichert (lokal, höchste Priorität) – werden mit Routen-Export/Dropbox übertragen.";
        _sync.AfterCommit();
        _loadedFingerprint = ComputeFingerprint();
    }

    [RelayCommand]
    private void AddEmployee()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            StatusMessage = "Kein Route-Paket geladen.";
            return;
        }

        var item = new EmployeeRosterItem
        {
            Name = "Neuer Mitarbeiter",
            PhoneNumber = $"+49{DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 1_000_000_000:D9}",
            PersonnelNumber = string.Empty,
            Password = string.Empty
        };
        Employees.Add(item);
        SelectedEmployee = item;
        _sync.MarkDirty();
        StatusMessage = "Neuer Mitarbeiter – bitte Daten ergänzen und speichern.";
    }

    [RelayCommand]
    private void RemoveEmployee()
    {
        if (SelectedEmployee is null)
        {
            StatusMessage = "Bitte zuerst einen Mitarbeiter auswählen.";
            return;
        }

        if (AppServices.PlannerLocal is not null)
        {
            AppServices.PlannerLocal.RecordEmployeeDeleted(SelectedEmployee);
        }

        var idx = Employees.IndexOf(SelectedEmployee);
        Employees.Remove(SelectedEmployee);
        SelectedEmployee = Employees.Count == 0
            ? null
            : Employees[Math.Clamp(idx, 0, Employees.Count - 1)];
        _sync.MarkDirty();
        StatusMessage = "Mitarbeiter aus der Liste entfernt – „Speichern“ nicht vergessen.";
    }

    [RelayCommand]
    private void SaveChanges() => CommitChanges();

    [RelayCommand(CanExecute = nameof(CanExportBriefingPdf))]
    private void ExportBriefingPdf()
    {
        if (SelectedEmployee is null)
        {
            StatusMessage = "Bitte zuerst einen Mitarbeiter auswählen.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedEmployee.Name))
        {
            StatusMessage = "Name fehlt – PDF kann nicht erstellt werden.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = EmployeeBriefingPdfGenerator.BuildDefaultFileName(SelectedEmployee),
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            EmployeeBriefingPdfGenerator.Generate(dialog.FileName, SelectedEmployee);
            StatusMessage = $"Einweisungs-PDF erstellt: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF-Erstellung fehlgeschlagen: {ex.Message}";
        }
    }

    private bool CanExportBriefingPdf() => SelectedEmployee is not null;

    partial void OnSelectedEmployeeChanged(EmployeeRosterItem? value)
    {
        ExportBriefingPdfCommand.NotifyCanExecuteChanged();
        if (value is null)
        {
            return;
        }

        ValidateSelectedEmployee(value, notify: false);
    }

    public void NotifyDocumentCheckChanged()
    {
        _sync.MarkDirty();
        if (SelectedEmployee is not null)
        {
            StatusMessage = $"„{SelectedEmployee.Name}“ – Kontrolle bestätigt, bitte „Speichern“.";
        }
    }

    private void ValidateSelectedEmployee(EmployeeRosterItem employee, bool notify)
    {
        if (string.IsNullOrWhiteSpace(employee.Name))
        {
            if (notify) StatusMessage = "Name fehlt.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(employee.PhoneNumber) &&
            Employees.Count(e => e != employee && e.PhoneNumber == employee.PhoneNumber) > 0)
        {
            if (notify) StatusMessage = "Telefonnummer bereits vergeben.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(employee.PersonnelNumber) &&
            Employees.Count(e => e != employee && e.PersonnelNumber == employee.PersonnelNumber) > 0)
        {
            if (notify) StatusMessage = "Personalnummer bereits vergeben.";
            return;
        }

        if (notify)
        {
            StatusMessage = $"„{employee.Name}“ – Eingaben ok, bitte „Speichern“.";
        }
    }

    private string ComputeFingerprint() =>
        JsonSerializer.Serialize(Employees.Select(e => new
        {
            e.Name,
            e.PhoneNumber,
            e.PersonnelNumber,
            e.Password,
            e.LicenseExpiry,
            e.FqnExpiry,
            e.DriverCardExpiry,
            e.LoginAsMainDevice,
            e.PlannerLoginEnabled,
            e.PlannerPassword,
            e.LicenseCheckConfirmedAtUtcMs,
            e.FqnCheckConfirmedAtUtcMs,
            e.DriverCardCheckConfirmedAtUtcMs
        }));

    private static EmployeeRosterItem Clone(EmployeeRosterItem e) => new()
    {
        Name = e.Name,
        PhoneNumber = e.PhoneNumber,
        PersonnelNumber = e.PersonnelNumber,
        Password = e.Password,
        LicenseExpiry = e.LicenseExpiry,
        FqnExpiry = e.FqnExpiry,
        DriverCardExpiry = e.DriverCardExpiry,
        LoginAsMainDevice = e.LoginAsMainDevice,
        PlannerLoginEnabled = e.PlannerLoginEnabled,
        PlannerPassword = e.PlannerPassword,
        LicenseCheckConfirmedAtUtcMs = e.LicenseCheckConfirmedAtUtcMs,
        FqnCheckConfirmedAtUtcMs = e.FqnCheckConfirmedAtUtcMs,
        DriverCardCheckConfirmedAtUtcMs = e.DriverCardCheckConfirmedAtUtcMs
    };
}
