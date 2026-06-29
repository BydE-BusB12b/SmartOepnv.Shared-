using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Employees;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public enum EmployeeManagementButtonState
{
    Idle,
    Success
}

public partial class EmployeesViewModel : ObservableObject, IEditorAreaViewModel
{
    private const int SuccessFeedbackMs = 5000;

    private readonly EditorAreaSyncState _sync = new();
    private string? _loadedFingerprint;
    private CancellationTokenSource? _saveFeedbackCts;

    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private EmployeeRosterItem? selectedEmployee;
    [ObservableProperty] private EmployeeManagementButtonState saveButtonState = EmployeeManagementButtonState.Idle;

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
            if (BuiltinAdminEmployee.IsBuiltinAdmin(employee))
            {
                continue;
            }

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

        CommitChanges(showSuccessFeedback: false);
    }

    public void CommitChanges(bool showSuccessFeedback = true)
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return;
        }

        ApplyLastEditedTimestamps(editor.Employees);
        editor.ReplaceEmployees(
            Employees
                .Where(e => !BuiltinAdminEmployee.IsBuiltinAdmin(e))
                .Select(Clone)
                .ToList());
        AppServices.Routes.ApplyEditorChanges("fahrer");
        AppServices.PlannerLocal?.PersistFromEditor(editor);
        StatusMessage =
            $"{Employees.Count} Mitarbeiter im Planer gespeichert (lokal, höchste Priorität) – werden mit Routen-Export/Dropbox übertragen.";
        _sync.AfterCommit();
        _loadedFingerprint = ComputeFingerprint();

        if (showSuccessFeedback)
        {
            _ = ShowSaveSuccessFeedbackAsync();
        }
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

        var label = string.IsNullOrWhiteSpace(SelectedEmployee.Name)
            ? SelectedEmployee.DisplayLabel
            : SelectedEmployee.Name.Trim();

        var confirmed = SmartConfirmDialog.ShowConfirm(
            Application.Current.MainWindow,
            "Mitarbeiter löschen",
            $"„{label}“ wirklich aus der Personalverwaltung entfernen?\n\n" +
            "Der Mitarbeiter wird auch aus der Fahrerdisposition entfernt.\n" +
            "Zeitwirtschaft-Daten bleiben erhalten und müssen dort bei Bedarf separat gelöscht werden.",
            confirmButton: "Löschen",
            cancelButton: "Abbrechen");

        if (!confirmed)
        {
            return;
        }

        var employee = SelectedEmployee;

        if (AppServices.PlannerLocal is not null)
        {
            AppServices.PlannerLocal.RecordEmployeeDeleted(employee);
        }

        var idx = Employees.IndexOf(employee);
        Employees.Remove(employee);
        SelectedEmployee = Employees.Count == 0
            ? null
            : Employees[Math.Clamp(idx, 0, Employees.Count - 1)];
        _sync.MarkDirty();
        StatusMessage =
            $"„{label}“ entfernt (Fahrerdisposition bereinigt) – bitte „Speichern“. Zeitwirtschaft unverändert.";
    }

    [RelayCommand]
    private void SaveChanges() => CommitChanges();

    private async Task ShowSaveSuccessFeedbackAsync()
    {
        _saveFeedbackCts?.Cancel();
        _saveFeedbackCts?.Dispose();
        _saveFeedbackCts = new CancellationTokenSource();
        var token = _saveFeedbackCts.Token;

        SaveButtonState = EmployeeManagementButtonState.Success;
        try
        {
            await Task.Delay(SuccessFeedbackMs, token).ConfigureAwait(true);
            SaveButtonState = EmployeeManagementButtonState.Idle;
        }
        catch (TaskCanceledException)
        {
            // neuer Klick hat Feedback zurückgesetzt
        }
    }

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
        JsonSerializer.Serialize(Employees.Select(EmployeeDataFingerprint));

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
        DriverCardCheckConfirmedAtUtcMs = e.DriverCardCheckConfirmedAtUtcMs,
        LastEditedAtUtcMs = e.LastEditedAtUtcMs
    };

    private void ApplyLastEditedTimestamps(IList<EmployeeRosterItem> previousEmployees)
    {
        var previousFingerprints = previousEmployees
            .GroupBy(EmployeeDispoKeys.FromEmployee, StringComparer.Ordinal)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => EmployeeDataFingerprint(g.First()), StringComparer.Ordinal);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var employee in Employees)
        {
            var key = EmployeeDispoKeys.FromEmployee(employee);
            var fingerprint = EmployeeDataFingerprint(employee);
            if (string.IsNullOrEmpty(key) ||
                !previousFingerprints.TryGetValue(key, out var previousFingerprint) ||
                !string.Equals(previousFingerprint, fingerprint, StringComparison.Ordinal))
            {
                employee.LastEditedAtUtcMs = now;
            }
        }
    }

    private static string EmployeeDataFingerprint(EmployeeRosterItem e) =>
        JsonSerializer.Serialize(new
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
        });
}
