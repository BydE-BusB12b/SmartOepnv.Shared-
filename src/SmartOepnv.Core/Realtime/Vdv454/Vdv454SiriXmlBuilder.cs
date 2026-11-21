using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace SmartOepnv.Core.Realtime.Vdv454;

/// <summary>
/// Baut vorbereitende SIRI-XML-Fragmente (VehicleMonitoring + EstimatedTimetable).
/// Kein SOAP/HTTP-Subscription – nur Serialisierung zum Anbinden an Verbund-Tests.
/// Namespace bewusst SIRI 2.0; VDV 454 nutzt kompatible Strukturen.
/// </summary>
public static class Vdv454SiriXmlBuilder
{
    private static readonly XNamespace Siri = "http://www.siri.org.uk/siri";

    /// <summary>Beide Deliveries in einem Container für Dry-Run-Dateien.</summary>
    public static string BuildPreparationBundle(Vdv454ExportSnapshot snapshot)
    {
        var root = new XElement("SmartOepnvVdv454Preparation",
            new XAttribute("producedAt", Iso(snapshot.ProducedAt)),
            new XAttribute("exportEnabled", snapshot.Config.ExportEnabled),
            new XAttribute("participant", snapshot.Config.ParticipantRef),
            new XAttribute("operatorRef", snapshot.Config.OperatorRef),
            new XAttribute("localCodespace", snapshot.Config.LocalCodespace),
            snapshot.Config.ExternalStopCodespace is { } ext
                ? new XAttribute("externalStopCodespace", ext)
                : null,
            BuildVehicleMonitoringDeliveryElement(snapshot),
            BuildEstimatedTimetableDeliveryElement(snapshot));
        return ToXmlDocument(root);
    }

    public static string BuildVehicleMonitoringDelivery(Vdv454ExportSnapshot snapshot) =>
        ToXmlDocument(BuildVehicleMonitoringDeliveryElement(snapshot));

    public static string BuildEstimatedTimetableDelivery(Vdv454ExportSnapshot snapshot) =>
        ToXmlDocument(BuildEstimatedTimetableDeliveryElement(snapshot));

    private static XElement BuildVehicleMonitoringDeliveryElement(Vdv454ExportSnapshot snapshot) =>
        new XElement(Siri + "VehicleMonitoringDelivery",
            new XAttribute("version", "2.0"),
            new XElement(Siri + "ResponseTimestamp", Iso(snapshot.ProducedAt)),
            new XElement(Siri + "SubscriberRef", snapshot.Config.ParticipantRef),
            new XElement(Siri + "SubscriptionRef", $"{snapshot.Config.ParticipantRef}:VM:prep"),
            new XElement(Siri + "Status", true),
            snapshot.Activities.Select(BuildVehicleActivity));

    private static XElement BuildEstimatedTimetableDeliveryElement(Vdv454ExportSnapshot snapshot)
    {
        var frames = snapshot.Activities.Select(a =>
            new XElement(Siri + "EstimatedJourneyVersionFrame",
                new XElement(Siri + "RecordedAtTime", Iso(a.RecordedAtTime)),
                BuildEstimatedVehicleJourney(a)));

        return new XElement(Siri + "EstimatedTimetableDelivery",
            new XAttribute("version", "2.0"),
            new XElement(Siri + "ResponseTimestamp", Iso(snapshot.ProducedAt)),
            new XElement(Siri + "SubscriberRef", snapshot.Config.ParticipantRef),
            new XElement(Siri + "SubscriptionRef", $"{snapshot.Config.ParticipantRef}:ET:prep"),
            new XElement(Siri + "Status", true),
            frames);
    }

    private static XElement BuildVehicleActivity(Vdv454VehicleActivity a)
    {
        var mvj = new XElement(Siri + "MonitoredVehicleJourney",
            new XElement(Siri + "LineRef", a.LineRef),
            ElIf(a.OperatorRef, "OperatorRef"),
            ElIf(a.DatedVehicleJourneyRef, "DatedVehicleJourneyRef"),
            ElIf(a.JourneyPatternRef, "JourneyPatternRef"),
            ElIf(a.DirectionRef, "DirectionRef"),
            new XElement(Siri + "VehicleRef", a.VehicleRef),
            ElIf(a.DestinationName, "DestinationName"),
            a.DelaySeconds is int d
                ? new XElement(Siri + "Delay", FormatDelay(d))
                : null,
            a.Latitude is double lat && a.Longitude is double lon
                ? new XElement(Siri + "VehicleLocation",
                    new XElement(Siri + "Longitude", lon.ToString("F6", CultureInfo.InvariantCulture)),
                    new XElement(Siri + "Latitude", lat.ToString("F6", CultureInfo.InvariantCulture)))
                : null);

        if (a.MonitoredCall is { } call)
        {
            mvj.Add(new XElement(Siri + "MonitoredCall",
                new XElement(Siri + "StopPointRef", call.StopPointRef),
                ElIf(call.StopPointName, "StopPointName"),
                CallTime(call.AimedArrival, "AimedArrivalTime"),
                CallTime(call.ExpectedArrival, "ExpectedArrivalTime"),
                CallTime(call.AimedDeparture, "AimedDepartureTime"),
                CallTime(call.ExpectedDeparture, "ExpectedDepartureTime")));
        }

        return new XElement(Siri + "VehicleActivity",
            new XElement(Siri + "RecordedAtTime", Iso(a.RecordedAtTime)),
            new XElement(Siri + "ValidUntilTime", Iso(a.RecordedAtTime.AddMinutes(5))),
            mvj);
    }

    private static XElement BuildEstimatedVehicleJourney(Vdv454VehicleActivity a)
    {
        var evj = new XElement(Siri + "EstimatedVehicleJourney",
            new XElement(Siri + "LineRef", a.LineRef),
            ElIf(a.OperatorRef, "OperatorRef"),
            ElIf(a.DatedVehicleJourneyRef, "DatedVehicleJourneyRef"),
            new XElement(Siri + "VehicleRef", a.VehicleRef),
            ElIf(a.DestinationName, "DestinationName"),
            new XElement(Siri + "EstimatedCalls",
                a.EstimatedCalls.Select(c =>
                    new XElement(Siri + "EstimatedCall",
                        new XElement(Siri + "StopPointRef", c.StopPointRef),
                        ElIf(c.StopPointName, "StopPointName"),
                        c.Order is int o ? new XElement(Siri + "Order", o) : null,
                        CallTime(c.AimedArrival, "AimedArrivalTime"),
                        CallTime(c.ExpectedArrival, "ExpectedArrivalTime"),
                        CallTime(c.AimedDeparture, "AimedDepartureTime"),
                        CallTime(c.ExpectedDeparture, "ExpectedDepartureTime")))));
        return evj;
    }

    private static XElement? CallTime(DateTimeOffset? value, string localName) =>
        value is DateTimeOffset t ? new XElement(Siri + localName, Iso(t)) : null;

    private static XElement? ElIf(string? value, string localName) =>
        string.IsNullOrWhiteSpace(value) ? null : new XElement(Siri + localName, value);

    private static string FormatDelay(int delaySeconds)
    {
        var sign = delaySeconds < 0 ? "-" : "";
        var abs = Math.Abs(delaySeconds);
        var h = abs / 3600;
        var m = abs % 3600 / 60;
        var s = abs % 60;
        return $"{sign}PT{h}H{m}M{s}S";
    }

    private static string Iso(DateTimeOffset dto) =>
        dto.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string ToXmlDocument(XElement root)
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
            doc.Save(writer);
        return sb.ToString();
    }
}
