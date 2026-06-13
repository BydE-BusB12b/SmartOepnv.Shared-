using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Zeitwirtschaft;

namespace SmartOepnv.Core.Maengelkarte;

public static class MaengelkarteVehicleLabelResolver
{
    public static void EnrichVehicleDisplay(
        IEnumerable<MaengelkarteEntry> entries,
        IEnumerable<RegisteredVehicleItem>? vehicles,
        IEnumerable<RegisteredVehiclePhoneRedirect>? phoneRedirects = null)
    {
        var map = vehicles is null
            ? null
            : ZeitwirtschaftVehicleLabelResolver.BuildLabelMap(vehicles, phoneRedirects);

        foreach (var entry in entries)
        {
            entry.VehicleDisplay = Resolve(entry, map);
        }
    }

    public static string Resolve(MaengelkarteEntry entry, IReadOnlyDictionary<string, string>? labelMap)
    {
        if (!string.IsNullOrWhiteSpace(entry.AuthorVehicleName))
        {
            return entry.AuthorVehicleName.Trim();
        }

        var fromPhone = ZeitwirtschaftVehicleLabelResolver.Resolve(entry.AuthorDevicePhone, labelMap);
        if (!string.IsNullOrWhiteSpace(fromPhone) &&
            !string.Equals(fromPhone, entry.AuthorDevicePhone, StringComparison.Ordinal))
        {
            return fromPhone;
        }

        return string.IsNullOrWhiteSpace(entry.AuthorDevicePhone) ? "—" : entry.AuthorDevicePhone;
    }
}
