using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Verkauf;

/// <summary>
/// Verkaufsliste = Mitarbeiterregister. Dateien ohne Register-Zeile bleiben sichtbar.
/// Der Geräte-Admin (PN 1995) ist kein Register-Mitarbeiter und bleibt eine eigene Zeile.
/// </summary>
public static class VerkaufDriverListComposer
{
    public static IReadOnlyList<VerkaufDriverDocument> Compose(
        IReadOnlyList<VerkaufDriverDocument> files,
        IEnumerable<EmployeeRosterItem> roster)
    {
        var people = roster
            .Where(e => !BuiltinAdminEmployee.IsBuiltinAdmin(e))
            .Where(e => EmployeeRosterItem.NormalizePersonnelDigits(e.PersonnelNumber).Length > 0)
            .ToList();
        if (people.Count == 0)
        {
            return files;
        }

        var byPn = new Dictionary<string, VerkaufDriverDocument>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var pn = EmployeeRosterItem.NormalizePersonnelDigits(file.PersonnelNumber);
            if (pn.Length == 0 || byPn.ContainsKey(pn)) continue;
            byPn[pn] = file;
        }

        var result = new List<VerkaufDriverDocument>();
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var employee in people)
        {
            var pn = EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);
            var name = string.IsNullOrWhiteSpace(employee.Name) ? pn : employee.Name.Trim();
            if (byPn.TryGetValue(pn, out var own))
            {
                result.Add(own.WithDisplay(name, employee.PersonnelNumber.Trim()));
                consumed.Add(own.SourceFileName);
                continue;
            }

            result.Add(VerkaufDocumentParser.CreateEmpty(employee.PersonnelNumber, name));
        }

        foreach (var file in files)
        {
            if (consumed.Contains(file.SourceFileName)) continue;
            result.Add(file);
        }

        return result;
    }
}
