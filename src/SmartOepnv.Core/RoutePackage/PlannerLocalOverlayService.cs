using System.IO;
using System.Text.Json.Nodes;
using SmartOepnv.Core;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Planer: Fahrer- und Fahrzeugverwaltung lokal mit höchster Priorität gegenüber Import/Versionen/Dropbox.
/// </summary>
public sealed class PlannerLocalOverlayService
{
    private readonly PlannerLocalOverlayStore _store;

    public PlannerLocalOverlayService(string appSubfolder)
    {
        _store = new PlannerLocalOverlayStore(appSubfolder);
    }

    public string OverlayFilePath => _store.OverlayFilePath;

    public bool HasOverlayFile => _store.Exists;

    /// <summary>Nach jedem Laden eines Route-Pakets (Datei, Dropbox, Version).</summary>
    public void ApplyAfterPackageLoad(EditableRoutePackage editor)
    {
        var overlay = _store.LoadOrEmpty();
        if (!overlay.HasContent && !_store.Exists)
        {
            overlay = CaptureFromEditor(editor);
            _store.Save(overlay);
            return;
        }

        if (overlay.HasContent || overlay.DeletedEmployeePersonnel.Count > 0 ||
            overlay.DeletedEmployeePhones.Count > 0 || overlay.DeletedVehiclePhoneKeys.Count > 0)
        {
            ApplyToEditor(editor, overlay);
        }
    }

    /// <summary>Übernimmt Fahrer/Fahrzeuge aus dem lokalen Overlay in ein beliebiges Paket (z. B. vor App-Upload einer Version).</summary>
    public void ApplyOverlayToEditor(EditableRoutePackage editor)
    {
        var overlay = _store.LoadOrEmpty();
        if (!overlay.HasContent &&
            overlay.DeletedEmployeePersonnel.Count == 0 &&
            overlay.DeletedEmployeePhones.Count == 0 &&
            overlay.DeletedVehiclePhoneKeys.Count == 0)
        {
            return;
        }

        ApplyToEditor(editor, overlay);
    }

    /// <summary>Nach Speichern in Fahrer- oder Fahrzeugverwaltung.</summary>
    public void PersistFromEditor(EditableRoutePackage editor)
    {
        var overlay = CaptureFromEditor(editor);
        var previous = _store.LoadOrEmpty();
        overlay.DeletedEmployeePersonnel = MergeUnique(
            previous.DeletedEmployeePersonnel,
            overlay.DeletedEmployeePersonnel);
        overlay.DeletedEmployeePhones = MergeUnique(
            previous.DeletedEmployeePhones,
            overlay.DeletedEmployeePhones);
        overlay.DeletedVehiclePhoneKeys = MergeUnique(
            previous.DeletedVehiclePhoneKeys,
            overlay.DeletedVehiclePhoneKeys);
        overlay.VehicleDispositionAssignments = previous.VehicleDispositionAssignments
            .Select(a => a.Clone())
            .ToList();
        _store.Save(overlay);
    }

    public void RecordEmployeeDeleted(EmployeeRosterItem employee)
    {
        var overlay = _store.LoadOrEmpty();
        var personnel = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);
        if (personnel.Length > 0)
        {
            AddUnique(overlay.DeletedEmployeePersonnel, personnel);
        }
        else
        {
            var phone = NormalizePhone(employee.PhoneNumber);
            if (phone.Length > 0)
            {
                AddUnique(overlay.DeletedEmployeePhones, phone);
            }
        }

        _store.Save(overlay);
    }

    public void RecordVehicleDeleted(RegisteredVehicleItem vehicle)
    {
        var overlay = _store.LoadOrEmpty();
        foreach (var key in CollectVehiclePhoneKeys(vehicle))
        {
            AddUnique(overlay.DeletedVehiclePhoneKeys, key);
        }

        var phoneKeys = CollectVehiclePhoneKeys(vehicle).ToHashSet(StringComparer.Ordinal);
        overlay.VehicleDispositionAssignments.RemoveAll(a =>
            phoneKeys.Contains(NormalizePhone(a.VehiclePhone)));

        _store.Save(overlay);
    }

    public IReadOnlyList<VehicleDispositionAssignment> LoadVehicleDisposition()
    {
        return _store.LoadOrEmpty()
            .VehicleDispositionAssignments
            .Select(a => a.Clone())
            .ToList();
    }

    public void SaveVehicleDisposition(IEnumerable<VehicleDispositionAssignment> assignments)
    {
        var list = assignments.Select(a => a.Clone()).ToList();
        var overlay = _store.LoadOrEmpty();
        overlay.VehicleDispositionAssignments = list;
        _store.Save(overlay);

        if (AppServices.IsPlannerApp)
        {
            SmartOepnvDataBackupService.BackupAppData(AppServices.SettingsSubfolder, "vehicle-dispo");
        }
    }

    /// <summary>
    /// Entfernt aus einem importierten JSON Fahrer/Fahrzeuge, die im Planer als gelöscht markiert sind
    /// (z. B. vor dem Anlegen eines ersten Overlays aus älteren Daten).
    /// </summary>
    public string StripDeletedFromPackageJson(string json)
    {
        var overlay = _store.LoadOrEmpty();
        if (overlay.DeletedEmployeePersonnel.Count == 0 &&
            overlay.DeletedEmployeePhones.Count == 0 &&
            overlay.DeletedVehiclePhoneKeys.Count == 0)
        {
            return json;
        }

        var node = JsonNode.Parse(json);
        if (node is not JsonObject root)
        {
            return json;
        }

        StripEmployeesFromRoot(root, overlay);
        StripVehiclesFromRoot(root, overlay);
        return root.ToJsonString();
    }

    private static void StripEmployeesFromRoot(JsonObject root, PlannerLocalOverlayData overlay)
    {
        if (root["employeeRoster"] is not JsonArray arr)
        {
            return;
        }

        var kept = new JsonArray();
        foreach (var node in arr.OfType<JsonObject>())
        {
            var item = new EmployeeRosterItem
            {
                Name = node["name"]?.GetValue<string>() ?? string.Empty,
                PhoneNumber = node["phoneNumber"]?.GetValue<string>() ?? string.Empty,
                PersonnelNumber = node["personnelNumber"]?.GetValue<string>() ?? string.Empty,
                Password = node["password"]?.GetValue<string>() ?? string.Empty
            };
            if (IsEmployeeDeleted(item, overlay))
            {
                continue;
            }

            kept.Add(node.DeepClone());
        }

        root["employeeRoster"] = kept;
    }

    private static void StripVehiclesFromRoot(JsonObject root, PlannerLocalOverlayData overlay)
    {
        if (root["registeredVehicles"] is not JsonArray arr)
        {
            return;
        }

        var kept = new JsonArray();
        foreach (var node in arr.OfType<JsonObject>())
        {
            var phone = node["phoneNumber"]?.GetValue<string>() ?? string.Empty;
            if (overlay.DeletedVehiclePhoneKeys.Contains(NormalizePhone(phone)))
            {
                continue;
            }

            kept.Add(node.DeepClone());
        }

        root["registeredVehicles"] = kept;

        if (root["registeredVehiclesPlannerMeta"] is JsonArray metaArr)
        {
            var metaKept = new JsonArray();
            foreach (var node in metaArr.OfType<JsonObject>())
            {
                var phone = node["phoneNumber"]?.GetValue<string>() ?? string.Empty;
                if (!overlay.DeletedVehiclePhoneKeys.Contains(NormalizePhone(phone)))
                {
                    metaKept.Add(node.DeepClone());
                }
            }

            if (metaKept.Count == 0)
            {
                root.Remove("registeredVehiclesPlannerMeta");
            }
            else
            {
                root["registeredVehiclesPlannerMeta"] = metaKept;
            }
        }
    }

    private static bool IsEmployeeDeleted(EmployeeRosterItem item, PlannerLocalOverlayData overlay)
    {
        if (item.IsDeprecatedDefaultCredential())
        {
            return true;
        }

        var personnel = EmployeeRosterItem.NormalizePersonnelDigits(item.PersonnelNumber);
        if (personnel.Length > 0 && overlay.DeletedEmployeePersonnel.Contains(personnel))
        {
            return true;
        }

        var phone = NormalizePhone(item.PhoneNumber);
        return phone.Length > 0 && overlay.DeletedEmployeePhones.Contains(phone);
    }

    private static void ApplyToEditor(EditableRoutePackage editor, PlannerLocalOverlayData overlay)
    {
        editor.ReplaceEmployees(overlay.Employees
            .Where(e => !IsEmployeeDeleted(e, overlay))
            .Select(CloneEmployee)
            .ToList());
        editor.ReplaceRegisteredVehicles(overlay.Vehicles
            .Where(v => !overlay.DeletedVehiclePhoneKeys.Contains(NormalizePhone(v.PhoneNumber)))
            .Select(CloneVehicle)
            .ToList());
        editor.ReplaceRegisteredVehiclePhoneRedirects(overlay.PhoneRedirects.Select(CloneRedirect).ToList());
    }

    private static PlannerLocalOverlayData CaptureFromEditor(EditableRoutePackage editor)
    {
        return new PlannerLocalOverlayData
        {
            Employees = editor.Employees.Select(CloneEmployee).ToList(),
            Vehicles = editor.RegisteredVehicles.Select(CloneVehicle).ToList(),
            PhoneRedirects = editor.RegisteredVehiclePhoneRedirects.Select(CloneRedirect).ToList()
        };
    }

    private static IEnumerable<string> CollectVehiclePhoneKeys(RegisteredVehicleItem vehicle)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var current = NormalizePhone(vehicle.PhoneNumber);
        if (current.Length > 0)
        {
            keys.Add(current);
        }

        var loaded = NormalizePhone(vehicle.LoadedPhoneNumber);
        if (loaded.Length > 0)
        {
            keys.Add(loaded);
        }

        return keys;
    }

    private static string NormalizePhone(string? raw) =>
        RegisteredVehiclesEditor.NormalizePhoneKey(raw);

    private static void AddUnique(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }

    private static List<string> MergeUnique(IEnumerable<string> a, IEnumerable<string> b)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var x in a.Concat(b))
        {
            if (!string.IsNullOrWhiteSpace(x))
            {
                set.Add(x);
            }
        }

        return set.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    private static EmployeeRosterItem CloneEmployee(EmployeeRosterItem e) => new()
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

    private static RegisteredVehicleItem CloneVehicle(RegisteredVehicleItem v) => new()
    {
        Name = v.Name,
        PhoneNumber = v.PhoneNumber,
        PersonnelNumber = v.PersonnelNumber,
        Password = v.Password,
        LicenseExpiry = v.LicenseExpiry,
        FqnExpiry = v.FqnExpiry,
        DriverCardExpiry = v.DriverCardExpiry,
        LoginAsMainDevice = v.LoginAsMainDevice,
        LoadedPhoneNumber = string.IsNullOrWhiteSpace(v.LoadedPhoneNumber) ? v.PhoneNumber : v.LoadedPhoneNumber,
        PlannerDetails = v.PlannerDetails.Clone()
    };

    private static RegisteredVehiclePhoneRedirect CloneRedirect(RegisteredVehiclePhoneRedirect r) => new()
    {
        FromPhoneNumber = r.FromPhoneNumber,
        ToPhoneNumber = r.ToPhoneNumber,
        RecordedAt = r.RecordedAt,
        Note = r.Note
    };
}
