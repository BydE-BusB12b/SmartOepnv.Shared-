using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Core.Vrr;

public sealed class VrrStopAssignment
{
    public required string VrrStopId { get; init; }
    public required string DisplayName { get; init; }
    public double? Lat { get; init; }
    public double? Lon { get; init; }
}

public static class VrrStopAssignmentManager
{
    public static VrrStopAssignment FromCatalogEntry(VrrStopEntry entry) => new()
    {
        VrrStopId = PreferredVrrStopId(entry),
        DisplayName = entry.DisplayLine,
        Lat = entry.Lat,
        Lon = entry.Lon
    };

    public static string PreferredVrrStopId(VrrStopEntry entry) =>
        string.IsNullOrWhiteSpace(entry.GlobalId) ? entry.Id : entry.GlobalId;

    public static string PrefillQuery(string? stopName, string? currentVrrStopId)
    {
        var name = stopName?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        return currentVrrStopId?.Trim() ?? string.Empty;
    }

    public static VrrStopAssignment? ResolveById(string id)
    {
        var entry = VrrStopCatalog.FindById(id);
        return entry is null ? null : FromCatalogEntry(entry);
    }

    public static void ApplyToTemplate(ManagedStopTemplateItem template, VrrStopAssignment assignment)
    {
        template.VrrStopId = assignment.VrrStopId;
        if (string.IsNullOrWhiteSpace(template.StopNameItcs) ||
            ManagedStopTemplateItem.IsPlaceholderStopName(template.StopNameItcs))
        {
            template.StopNameItcs = assignment.DisplayName;
        }

        if (string.IsNullOrWhiteSpace(template.StopLat) && assignment.Lat.HasValue)
        {
            template.StopLat = CoordinateFormatting.FormatComponent(assignment.Lat.Value);
        }

        if (string.IsNullOrWhiteSpace(template.StopLng) && assignment.Lon.HasValue)
        {
            template.StopLng = CoordinateFormatting.FormatComponent(assignment.Lon.Value);
        }
    }
}
