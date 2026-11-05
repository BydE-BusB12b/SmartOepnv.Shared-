using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.Core.Realtime.Vdv454;

/// <summary>
/// Schreibt Dry-Run-XML aus Live-Fahrzeugen (z. B. Leitstellen-Debugordner).
/// Optional mit Planer-Paket für Aimed-Zeiten. Kein Netzwerkversand.
/// </summary>
public static class Vdv454PreparationExporter
{
    public static Vdv454ExportSnapshot BuildSnapshot(
        IEnumerable<VehicleLiveState> vehicles,
        Vdv454ParticipantConfig? config = null,
        EditableRoutePackage? schedulePackage = null) =>
        Vdv454LiveMapper.FromLiveVehicles(vehicles, config, schedulePackage: schedulePackage);

    public static string WritePreparationBundle(
        string directory,
        IEnumerable<VehicleLiveState> vehicles,
        Vdv454ParticipantConfig? config = null,
        EditableRoutePackage? schedulePackage = null)
    {
        Directory.CreateDirectory(directory);
        var snapshot = BuildSnapshot(vehicles, config, schedulePackage);
        var xml = Vdv454SiriXmlBuilder.BuildPreparationBundle(snapshot);
        var path = Path.Combine(
            directory,
            $"vdv454_prep_{snapshot.ProducedAt:yyyyMMdd_HHmmss}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    /// <summary>Anzahl Fahrzeuge mit mindestens einer Aimed-Zeit im Snapshot.</summary>
    public static int CountWithAimedTimes(Vdv454ExportSnapshot snapshot) =>
        snapshot.Activities.Count(a =>
            a.EstimatedCalls.Any(c => c.AimedArrival is not null || c.AimedDeparture is not null));
}
