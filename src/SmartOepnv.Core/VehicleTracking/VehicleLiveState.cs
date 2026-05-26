namespace SmartOepnv.Core.VehicleTracking;

public enum VehicleOnlineStatus
{
    Online,
    Stale,
    Offline,
    Hidden
}

public sealed class VehicleLiveState
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? PhoneNumber { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double AccuracyM { get; init; }
    public int SpeedKmh { get; init; }
    public string? LineCourse { get; init; }
    public string? RouteName { get; init; }
    public string? StopName { get; init; }
    public string? Destination { get; init; }
    public string? DriverName { get; init; }
    public int? BatteryLevel { get; init; }
    public int? DelaySeconds { get; init; }
    public long TimestampEpochMs { get; init; }
    public long FileTimestampEpochMs { get; init; }
    public VehicleOnlineStatus Status { get; init; }

    public static VehicleOnlineStatus ComputeStatus(long timestampEpochMs, long nowEpochMs)
    {
        var ageMs = Math.Max(0, nowEpochMs - timestampEpochMs);
        return ageMs switch
        {
            > 7L * 24 * 60 * 60 * 1000 => VehicleOnlineStatus.Hidden,
            > 2L * 24 * 60 * 60 * 1000 => VehicleOnlineStatus.Offline,
            > 5 * 60 * 1000 => VehicleOnlineStatus.Stale,
            _ => VehicleOnlineStatus.Online
        };
    }
}
