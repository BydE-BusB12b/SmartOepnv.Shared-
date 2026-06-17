namespace SmartOepnv.Core.RoutePackage;

/// <summary>ITCS-Außen-Zielliste (nur Einträge mit „In ITCS-Liste“), analog GPSAnsagen <c>ItcsDestinationListHelper</c>.</summary>
public static class KomOutsideDestinationCatalog
{
    public static IReadOnlyList<string> LoadListEnabledNames(EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return [];
        }

        return editor.OutsideDisplays
            .Select(OutsideDisplayProgram.TryParse)
            .Where(p => p is not null && p.IsListEnabled && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p!.Name.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }
}
