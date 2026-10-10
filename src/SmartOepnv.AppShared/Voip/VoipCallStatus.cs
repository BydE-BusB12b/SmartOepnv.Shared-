using SmartOepnv.Core.Voip;

namespace SmartOepnv.AppShared.Voip;

public sealed class VoipCallStatus
{
    public VoipCallConnectionState State { get; init; } = VoipCallConnectionState.Idle;
    public string? RemoteDisplayName { get; init; }
    public string? RemotePeerId { get; init; }
    public string? FailureMessage { get; init; }

    public string StatusText => State switch
    {
        VoipCallConnectionState.Outgoing => RemoteDisplayName is { Length: > 0 }
            ? $"Funk-Anruf an {RemoteDisplayName} …"
            : "Funk-Anruf wird aufgebaut …",
        VoipCallConnectionState.Connecting => "Sprechverbindung wird aufgebaut …",
        VoipCallConnectionState.Connected => RemoteDisplayName is { Length: > 0 }
            ? $"Verbindung aktiv – {RemoteDisplayName}"
            : "Verbindung aktiv",
        VoipCallConnectionState.Failed => FailureMessage ?? "Sprechverbindung fehlgeschlagen",
        VoipCallConnectionState.Ended => "Funk beendet",
        _ => string.Empty
    };

    public bool IsActive =>
        State is VoipCallConnectionState.Outgoing
            or VoipCallConnectionState.Connecting
            or VoipCallConnectionState.Connected;
}
