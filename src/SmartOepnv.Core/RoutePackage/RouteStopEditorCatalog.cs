namespace SmartOepnv.Core.RoutePackage;

/// <summary>Ziel- und Routenlisten für die Haltestellen-Bearbeitung (wie GPSAnsagen dialog_add_stop).</summary>
public static class RouteStopEditorCatalog
{
    public const string NoDestinationLabel = "Kein Ziel";
    public const string NoLineCourseTripLabel = "Keine Fahrt ausgewählt";

    public static string ToComboLabel(string? value, string emptyLabel)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            string.Equals(trimmed, "Starthaltestelle", StringComparison.OrdinalIgnoreCase))
        {
            return emptyLabel;
        }

        return trimmed;
    }

    public static string FromComboLabel(string? value, string emptyLabel) =>
        string.Equals(value?.Trim(), emptyLabel, StringComparison.Ordinal) ? string.Empty : value?.Trim() ?? string.Empty;

    public static IReadOnlyList<string> LoadDs021tNames(EditableRoutePackage? editor) =>
        LoadProtocolNames(editor, isKrefeld: false);

    public static IReadOnlyList<string> LoadDs003aNames(EditableRoutePackage? editor) =>
        LoadProtocolNames(editor, isKrefeld: true);

    public static IReadOnlyList<string> LoadLineCourseTripRoutes(EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return [];
        }

        return editor.RouteNames
            .Select(route => RouteDisplayHelper.Parse(route))
            .Where(def =>
                !string.IsNullOrWhiteSpace(def.LineCourse) ||
                !string.IsNullOrWhiteSpace(def.TripNumber))
            .Select(RouteDisplayHelper.ToDisplayString)
            .Where(display =>
                !string.IsNullOrWhiteSpace(display) &&
                display != "()" &&
                display != " (Linie: , Fahrt: )")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> LoadProtocolNames(EditableRoutePackage? editor, bool isKrefeld)
    {
        if (editor is null)
        {
            return [];
        }

        return editor.OutsideDisplays
            .Select(OutsideDisplayProgram.TryParse)
            .Where(p => p is not null && p.IsListEnabled && !string.IsNullOrWhiteSpace(p.Name) && p.IsKrefeld == isKrefeld)
            .Select(p => p!.Name.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }
}
