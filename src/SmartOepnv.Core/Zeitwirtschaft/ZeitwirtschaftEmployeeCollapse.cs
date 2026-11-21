using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Zeitwirtschaft;

/// <summary>
/// Dieselbe Person kann in älteren Fahrzeugdateien unter einer zweiten Personalnummer stehen.
/// In der Auswahl soll sie einmal erscheinen, die Zeiten bleiben erhalten.
/// </summary>
public static class ZeitwirtschaftEmployeeCollapse
{
    public static IReadOnlyList<ZeitwirtschaftMergedEmployee> CollapseSamePerson(
        IReadOnlyList<ZeitwirtschaftMergedEmployee> employees,
        IEnumerable<EmployeeRosterItem>? roster)
    {
        var rosterByName = new Dictionary<string, List<(string Name, string PersonnelNumber)>>(StringComparer.Ordinal);
        if (roster is not null)
        {
            foreach (var person in roster)
            {
                var key = NameKey(person.Name);
                var personnel = person.PersonnelNumber?.Trim() ?? string.Empty;
                if (key.Length == 0 || EmployeeRosterItem.NormalizePersonnelDigits(personnel).Length == 0)
                {
                    continue;
                }

                if (!rosterByName.TryGetValue(key, out var list))
                {
                    list = [];
                    rosterByName[key] = list;
                }

                list.Add((person.Name.Trim(), personnel));
            }
        }

        var result = new List<ZeitwirtschaftMergedEmployee>();
        foreach (var group in employees.GroupBy(employee => GroupKey(employee)))
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                result.Add(members[0]);
                continue;
            }

            var nameKey = NameKey(members[0].Name);
            string canonicalPersonnel;
            string canonicalName;
            if (nameKey.Length > 0 &&
                rosterByName.TryGetValue(nameKey, out var rosterHits) &&
                rosterHits.Count == 1)
            {
                canonicalPersonnel = rosterHits[0].PersonnelNumber;
                canonicalName = rosterHits[0].Name;
            }
            else
            {
                var best = members
                    .OrderByDescending(member => member.Entries.Count)
                    .ThenByDescending(LatestStartMs)
                    .First();
                canonicalPersonnel = best.PersonnelNumber;
                canonicalName = best.Name;
            }

            var byId = new Dictionary<string, ZeitwirtschaftMergedEntry>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                foreach (var entry in member.Entries)
                {
                    if (string.IsNullOrEmpty(entry.EntryId))
                    {
                        continue;
                    }

                    if (!byId.TryGetValue(entry.EntryId, out var existing) || PreferIncoming(existing, entry))
                    {
                        byId[entry.EntryId] = entry;
                    }
                }
            }

            result.Add(new ZeitwirtschaftMergedEmployee
            {
                PersonnelNumber = canonicalPersonnel,
                Name = string.IsNullOrWhiteSpace(canonicalName) ? members[0].Name : canonicalName,
                Entries = byId.Values
                    .OrderBy(ZeitwirtschaftMergeService.EffectiveStartMs)
                    .ToList()
            });
        }

        return result
            .OrderBy(employee => employee.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(employee => employee.PersonnelNumber, StringComparer.Ordinal)
            .ToList();
    }

    private static string GroupKey(ZeitwirtschaftMergedEmployee employee)
    {
        var name = NameKey(employee.Name);
        if (name.Length == 0)
        {
            return "pn:" + EmployeeRosterItem.NormalizePersonnelDigits(employee.PersonnelNumber);
        }

        return "name:" + name;
    }

    private static string NameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var parts = name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    private static long LatestStartMs(ZeitwirtschaftMergedEmployee employee) =>
        employee.Entries.Count == 0
            ? 0
            : employee.Entries.Max(ZeitwirtschaftMergeService.EffectiveStartMs);

    private static bool PreferIncoming(ZeitwirtschaftMergedEntry existing, ZeitwirtschaftMergedEntry incoming)
    {
        if (incoming.Voided && !existing.Voided)
        {
            return true;
        }

        if (existing.Voided && !incoming.Voided)
        {
            return false;
        }

        if (incoming.CorrectedAtMs is > 0 && existing.CorrectedAtMs is not > 0)
        {
            return true;
        }

        var incomingEnd = incoming.EndEpochMs ?? 0;
        var existingEnd = existing.EndEpochMs ?? 0;
        return incomingEnd > existingEnd;
    }
}
