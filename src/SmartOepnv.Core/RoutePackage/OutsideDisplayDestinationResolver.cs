namespace SmartOepnv.Core.RoutePackage;

/// <summary>Zielname/ID zwischen Haltestelle und Außenanzeigen-Programm auflösen.</summary>
public static class OutsideDisplayDestinationResolver
{
    public sealed record CatalogEntry(string Id, string Name, OutsideDisplayProtocolKind Protocol);

    public static IReadOnlyList<CatalogEntry> BuildCatalog(IEnumerable<string> outsideDisplayEntries)
    {
        var list = new List<CatalogEntry>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in outsideDisplayEntries)
        {
            var program = OutsideDisplayProgram.TryParse(entry);
            if (program is null || !program.IsListEnabled)
            {
                continue;
            }

            var id = OutsideDisplayId.Ensure(program.Id);
            if (!seenIds.Add(id))
            {
                continue;
            }

            list.Add(new CatalogEntry(id, program.Name.Trim(), program.Protocol));
        }

        return list;
    }

    public static string? FindIdByName(
        IEnumerable<CatalogEntry> catalog,
        OutsideDisplayProtocolKind protocol,
        string? name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            string.Equals(name.Trim(), RouteStopEditorCatalog.NoDestinationLabel, StringComparison.Ordinal))
        {
            return null;
        }

        var trimmed = name.Trim();
        return catalog
            .FirstOrDefault(e =>
                e.Protocol == protocol &&
                string.Equals(e.Name, trimmed, StringComparison.Ordinal))
            ?.Id;
    }

    public static string? FindNameById(
        IEnumerable<CatalogEntry> catalog,
        OutsideDisplayProtocolKind protocol,
        string? id)
    {
        var normalized = OutsideDisplayId.Normalize(id);
        if (!OutsideDisplayId.IsValid(normalized))
        {
            return null;
        }

        return catalog
            .FirstOrDefault(e =>
                e.Protocol == protocol &&
                string.Equals(e.Id, normalized, StringComparison.OrdinalIgnoreCase))
            ?.Name;
    }

    /// <summary>
    /// Anzeigename für Combo: bevorzugt aktuelle Programm-Namen zur gespeicherten ID,
    /// sonst Legacy-Name.
    /// </summary>
    public static string ResolveDisplayName(
        IEnumerable<CatalogEntry> catalog,
        OutsideDisplayProtocolKind protocol,
        string? destinationId,
        string? destinationName)
    {
        var byId = FindNameById(catalog, protocol, destinationId);
        if (!string.IsNullOrWhiteSpace(byId))
        {
            return byId;
        }

        return destinationName?.Trim() ?? string.Empty;
    }

    /// <summary>Beim Speichern der Auswahl: Name + ID setzen.</summary>
    public static void ApplySelection(
        RouteStopItem stop,
        OutsideDisplayProtocolKind protocol,
        bool isEndStop,
        string? comboName,
        IEnumerable<CatalogEntry> catalog)
    {
        var name = string.Equals(comboName?.Trim(), RouteStopEditorCatalog.NoDestinationLabel, StringComparison.Ordinal)
            ? string.Empty
            : comboName?.Trim() ?? string.Empty;
        var id = string.IsNullOrEmpty(name)
            ? string.Empty
            : FindIdByName(catalog, protocol, name) ?? string.Empty;

        SetLink(stop, protocol, isEndStop, name, id);
    }

    public static void SetLink(
        RouteStopItem stop,
        OutsideDisplayProtocolKind protocol,
        bool isEndStop,
        string name,
        string id)
    {
        if (isEndStop)
        {
            switch (protocol)
            {
                case OutsideDisplayProtocolKind.Ds021Neu:
                    stop.Ds021NeuEndDestination = name;
                    stop.Ds021NeuEndDestinationId = id;
                    break;
                case OutsideDisplayProtocolKind.FmaS1:
                    stop.FmaS1EndDestination = name;
                    stop.FmaS1EndDestinationId = id;
                    break;
                case OutsideDisplayProtocolKind.Ds003aKrefeld:
                    stop.Ds003aEndDestination = name;
                    stop.Ds003aEndDestinationId = id;
                    break;
                case OutsideDisplayProtocolKind.Zielnummer:
                    stop.ZielnummerEndDestination = name;
                    stop.ZielnummerEndDestinationId = id;
                    break;
                default:
                    stop.EndDestination = name;
                    stop.EndDestinationId = id;
                    break;
            }

            return;
        }

        switch (protocol)
        {
            case OutsideDisplayProtocolKind.Ds021Neu:
                stop.Ds021NeuDestination = name;
                stop.Ds021NeuDestinationId = id;
                break;
            case OutsideDisplayProtocolKind.FmaS1:
                stop.FmaS1Destination = name;
                stop.FmaS1DestinationId = id;
                break;
            case OutsideDisplayProtocolKind.Ds003aKrefeld:
                stop.Ds003aDestination = name;
                stop.Ds003aDestinationId = id;
                break;
            case OutsideDisplayProtocolKind.Zielnummer:
                stop.ZielnummerDestination = name;
                stop.ZielnummerDestinationId = id;
                break;
            default:
                stop.Destination = name;
                stop.DestinationId = id;
                break;
        }
    }

    /// <summary>Fehlende IDs aus Namen nachziehen; Namen aus IDs aktualisieren (nach Umbenennung).</summary>
    public static void SyncStopLinks(EditableRoutePackage editor)
    {
        var catalog = BuildCatalog(editor.OutsideDisplays);
        foreach (var routeKey in editor.RouteNames)
        {
            foreach (var stop in editor.GetStops(routeKey))
            {
                SyncOne(stop, catalog, OutsideDisplayProtocolKind.Ds021T, isEnd: false,
                    () => stop.Destination, () => stop.DestinationId,
                    (n, i) => { stop.Destination = n; stop.DestinationId = i; });
                SyncOne(stop, catalog, OutsideDisplayProtocolKind.Ds021Neu, isEnd: false,
                    () => stop.Ds021NeuDestination, () => stop.Ds021NeuDestinationId,
                    (n, i) => { stop.Ds021NeuDestination = n; stop.Ds021NeuDestinationId = i; });
                SyncOne(stop, catalog, OutsideDisplayProtocolKind.FmaS1, isEnd: false,
                    () => stop.FmaS1Destination, () => stop.FmaS1DestinationId,
                    (n, i) => { stop.FmaS1Destination = n; stop.FmaS1DestinationId = i; });
                SyncOne(stop, catalog, OutsideDisplayProtocolKind.Ds003aKrefeld, isEnd: false,
                    () => stop.Ds003aDestination, () => stop.Ds003aDestinationId,
                    (n, i) => { stop.Ds003aDestination = n; stop.Ds003aDestinationId = i; });
                SyncOne(stop, catalog, OutsideDisplayProtocolKind.Zielnummer, isEnd: false,
                    () => stop.ZielnummerDestination, () => stop.ZielnummerDestinationId,
                    (n, i) => { stop.ZielnummerDestination = n; stop.ZielnummerDestinationId = i; });

                SyncOne(stop, catalog, OutsideDisplayProtocolKind.Ds021T, isEnd: true,
                    () => stop.EndDestination, () => stop.EndDestinationId,
                    (n, i) => { stop.EndDestination = n; stop.EndDestinationId = i; });
                SyncOne(stop, catalog, OutsideDisplayProtocolKind.Ds021Neu, isEnd: true,
                    () => stop.Ds021NeuEndDestination, () => stop.Ds021NeuEndDestinationId,
                    (n, i) => { stop.Ds021NeuEndDestination = n; stop.Ds021NeuEndDestinationId = i; });
                SyncOne(stop, catalog, OutsideDisplayProtocolKind.FmaS1, isEnd: true,
                    () => stop.FmaS1EndDestination, () => stop.FmaS1EndDestinationId,
                    (n, i) => { stop.FmaS1EndDestination = n; stop.FmaS1EndDestinationId = i; });
                SyncOne(stop, catalog, OutsideDisplayProtocolKind.Ds003aKrefeld, isEnd: true,
                    () => stop.Ds003aEndDestination, () => stop.Ds003aEndDestinationId,
                    (n, i) => { stop.Ds003aEndDestination = n; stop.Ds003aEndDestinationId = i; });
                SyncOne(stop, catalog, OutsideDisplayProtocolKind.Zielnummer, isEnd: true,
                    () => stop.ZielnummerEndDestination, () => stop.ZielnummerEndDestinationId,
                    (n, i) => { stop.ZielnummerEndDestination = n; stop.ZielnummerEndDestinationId = i; });
            }
        }
    }

    private static void SyncOne(
        RouteStopItem stop,
        IReadOnlyList<CatalogEntry> catalog,
        OutsideDisplayProtocolKind protocol,
        bool isEnd,
        Func<string> getName,
        Func<string> getId,
        Action<string, string> set)
    {
        _ = stop;
        _ = isEnd;
        var name = getName()?.Trim() ?? string.Empty;
        var id = OutsideDisplayId.Normalize(getId());

        if (OutsideDisplayId.IsValid(id))
        {
            var currentName = FindNameById(catalog, protocol, id);
            if (!string.IsNullOrWhiteSpace(currentName) &&
                !string.Equals(currentName, name, StringComparison.Ordinal))
            {
                set(currentName, id);
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(name) ||
            RouteStopEditorCatalog.IsStartStopPlaceholder(name))
        {
            return;
        }

        var resolvedId = FindIdByName(catalog, protocol, name);
        if (!string.IsNullOrWhiteSpace(resolvedId))
        {
            set(name, resolvedId);
        }
    }

    /// <summary>Vergibt fehlende IDs in outsideDisplays und schreibt Einträge zurück.</summary>
    public static bool EnsureOutsideDisplayIds(EditableRoutePackage editor)
    {
        var changed = false;
        var rewritten = new List<string>();
        foreach (var entry in editor.OutsideDisplays)
        {
            var program = OutsideDisplayProgram.TryParse(entry);
            if (program is null)
            {
                rewritten.Add(entry);
                continue;
            }

            var before = OutsideDisplayId.FromStorageEntry(entry);
            if (!OutsideDisplayId.IsValid(before))
            {
                changed = true;
            }

            rewritten.Add(program.ToStorageEntry());
        }

        if (changed || rewritten.Count != editor.OutsideDisplays.Count)
        {
            editor.ReplaceOutsideDisplays(rewritten);
            return true;
        }

        // Auch wenn IDs schon da: normalisieren (Encode-Konsistenz)
        for (var i = 0; i < editor.OutsideDisplays.Count; i++)
        {
            if (!string.Equals(editor.OutsideDisplays[i], rewritten[i], StringComparison.Ordinal))
            {
                editor.ReplaceOutsideDisplays(rewritten);
                return true;
            }
        }

        return false;
    }
}
