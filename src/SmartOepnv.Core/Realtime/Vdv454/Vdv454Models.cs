namespace SmartOepnv.Core.Realtime.Vdv454;

/// <summary>Interne Zwischenform vor SIRI-XML (VehicleMonitoring / EstimatedTimetable).</summary>
public sealed class Vdv454VehicleActivity
{
    public required string VehicleRef { get; init; }
    public required string LineRef { get; init; }
    public string? OperatorRef { get; init; }
    public string? DatedVehicleJourneyRef { get; init; }
    public string? JourneyPatternRef { get; init; }
    public string? DirectionRef { get; init; }
    public string? DestinationName { get; init; }
    public string? OriginAimedName { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public int? DelaySeconds { get; init; }
    public int? SpeedKmh { get; init; }
    public string? ProgressStatus { get; init; }
    public DateTimeOffset RecordedAtTime { get; init; }
    public Vdv454EstimatedCall? MonitoredCall { get; init; }
    public IReadOnlyList<Vdv454EstimatedCall> EstimatedCalls { get; init; } = [];
}

public sealed class Vdv454EstimatedCall
{
    public required string StopPointRef { get; init; }
    public string? StopPointName { get; init; }
    public int? Order { get; init; }
    public DateTimeOffset? AimedArrival { get; init; }
    public DateTimeOffset? ExpectedArrival { get; init; }
    public DateTimeOffset? AimedDeparture { get; init; }
    public DateTimeOffset? ExpectedDeparture { get; init; }
    public bool? VehicleAtStop { get; init; }
}

public sealed class Vdv454ExportSnapshot
{
    public required Vdv454ParticipantConfig Config { get; init; }
    public required DateTimeOffset ProducedAt { get; init; }
    public required IReadOnlyList<Vdv454VehicleActivity> Activities { get; init; }
}
