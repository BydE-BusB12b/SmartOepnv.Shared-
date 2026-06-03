using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class EmployeesViewModel : ObservableObject
{
    [ObservableProperty] private string statusMessage = "Bitte zuerst ein Route-Paket importieren.";
    [ObservableProperty] private EmployeeRosterItem? selectedEmployee;

    public ObservableCollection<EmployeeRosterItem> Employees { get; } = [];

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

    public void RefreshFromEditor()
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
        StatusMessage = "Mitarbeiter aus der Liste entfernt – „Speichern“ nicht vergessen.";
    }

    [RelayCommand]
    private void SaveChanges() => CommitChanges();

    partial void OnSelectedEmployeeChanged(EmployeeRosterItem? value)
    {
        if (value is null)
        {
            return;
        }

        ValidateSelectedEmployee(value, notify: false);
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

    private static EmployeeRosterItem Clone(EmployeeRosterItem e) => new()
    {
        Name = e.Name,
        PhoneNumber = e.PhoneNumber,
        PersonnelNumber = e.PersonnelNumber,
        Password = e.Password,
        LicenseExpiry = e.LicenseExpiry,
        FqnExpiry = e.FqnExpiry,
        DriverCardExpiry = e.DriverCardExpiry,
        LoginAsMainDevice = e.LoginAsMainDevice
    };
}
