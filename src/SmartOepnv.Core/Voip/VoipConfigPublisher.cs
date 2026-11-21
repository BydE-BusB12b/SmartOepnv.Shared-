using System.Text.Json;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.Core.Voip;

/// <summary>Schreibt VoIP-Konfiguration nach Dropbox (Leitstelle + Planer nach Geräte-Registrierung).</summary>
public sealed class VoipConfigPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public async Task PublishDispatchAsync(
        DropboxApiClient dropbox,
        VoipSettings settings,
        CancellationToken ct = default)
    {
        var config = BuildDispatchConfig(settings);
        await UploadJsonAsync(dropbox, VoipConstants.DispatchConfigFileName, config, ct).ConfigureAwait(false);
    }

    public async Task PublishVehicleAsync(
        DropboxApiClient dropbox,
        VoipPeerConfig template,
        string displayName,
        string phoneRaw,
        CancellationToken ct = default)
    {
        var phone = VoipPhone.Normalize(phoneRaw);
        if (string.IsNullOrEmpty(phone))
        {
            return;
        }

        var config = new VoipPeerConfig
        {
            Version = VoipConstants.ConfigVersion,
            PeerId = phone,
            DisplayName = displayName.Trim(),
            Role = VoipConstants.RoleVehicle,
            SignalingUrl = template.SignalingUrl,
            SignalingUrlFallback = template.SignalingUrlFallback,
            ConnectivityMode = template.ConnectivityMode,
            TurnUrl = template.TurnUrl,
            TurnUsername = template.TurnUsername,
            TurnPassword = template.TurnPassword,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var json = JsonSerializer.Serialize(config, JsonOptions);
        foreach (var fileName in VoipPhone.ConfigFileNameVariantsForPhone(phoneRaw))
        {
            await dropbox.UploadNamedFileAsync(fileName, json, ct).ConfigureAwait(false);
        }
    }

    public async Task<VoipPeerConfig?> TryDownloadDispatchAsync(
        DropboxApiClient dropbox,
        CancellationToken ct = default)
    {
        try
        {
            var json = await dropbox.DownloadNamedFileAsync(VoipConstants.DispatchConfigFileName, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<VoipPeerConfig>(json);
        }
        catch
        {
            return null;
        }
    }

    public VoipPeerConfig BuildDispatchConfig(VoipSettings settings)
    {
        var fallback = settings.BuildSignalingFallbackUrl();
        return new VoipPeerConfig
        {
            Version = VoipConstants.ConfigVersion,
            PeerId = VoipConstants.RoleDispatch,
            DisplayName = settings.DispatchDisplayName.Trim(),
            Role = VoipConstants.RoleDispatch,
            SignalingUrl = settings.BuildSignalingUrl(),
            SignalingUrlFallback = fallback,
            ConnectivityMode = settings.ConnectivityMode.ToString(),
            TurnUrl = settings.BuildTurnUrlForRemotePeers(),
            TurnUsername = settings.TurnUsername.Trim(),
            TurnPassword = settings.TurnPassword,
            UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    public VoipPeerConfig BuildVehicleTemplate(VoipSettings settings)
    {
        var fallback = settings.BuildSignalingFallbackUrl();
        return new VoipPeerConfig
        {
            SignalingUrl = settings.BuildSignalingUrl(),
            SignalingUrlFallback = fallback,
            ConnectivityMode = settings.ConnectivityMode.ToString(),
            TurnUrl = settings.BuildTurnUrlForRemotePeers(),
            TurnUsername = settings.TurnUsername.Trim(),
            TurnPassword = settings.TurnPassword
        };
    }

    private static async Task UploadJsonAsync(
        DropboxApiClient dropbox,
        string fileName,
        VoipPeerConfig config,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        await dropbox.UploadNamedFileAsync(fileName, json, ct).ConfigureAwait(false);
    }
}
