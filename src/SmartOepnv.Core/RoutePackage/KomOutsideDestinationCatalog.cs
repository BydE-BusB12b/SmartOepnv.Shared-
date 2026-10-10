namespace SmartOepnv.Core.RoutePackage;

/// <summary>ITCS-Außen-Zielliste (nur Einträge mit „In ITCS-Liste“), analog GPSAnsagen <c>ItcsDestinationListHelper</c>.</summary>
public static class KomOutsideDestinationCatalog
{
    public sealed record ListItem(string Name, string ProtocolLabel);

    public static IReadOnlyList<ListItem> LoadListEnabledItems(EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return [];
        }

        return editor.OutsideDisplays
            .Select(OutsideDisplayProgram.TryParse)
            .Where(p => p is not null && p.IsListEnabled && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new ListItem(p!.Name.Trim(), p.ProtocolLabel))
            .OrderBy(i => i.Name, Comparer<string>.Create(OutsideDisplayProgram.CompareZiellisteNames))
            .ThenBy(i => i.ProtocolLabel, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<string> LoadListEnabledNames(EditableRoutePackage? editor) =>
        LoadListEnabledItems(editor).Select(i => i.Name).Distinct(StringComparer.Ordinal).ToList();
}
