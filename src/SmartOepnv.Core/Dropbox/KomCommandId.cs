namespace SmartOepnv.Core.Dropbox;

/// <summary>Entspricht GPSAnsagen <c>KomCommandAck.newCommandId()</c>.</summary>
public static class KomCommandId
{
    public static long New() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
