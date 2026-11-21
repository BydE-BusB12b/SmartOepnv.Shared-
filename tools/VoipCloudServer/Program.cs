using SmartOepnv.Core.Voip;

var listen = Environment.GetEnvironmentVariable("VOIP_LISTEN") ?? "0.0.0.0";
var portText = Environment.GetEnvironmentVariable("VOIP_PORT") ?? VoipConstants.DefaultSignalingPort.ToString();
if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
{
    Console.Error.WriteLine($"Ungültiger VOIP_PORT: {portText}");
    return 1;
}

var settings = new VoipSettings
{
    Enabled = true,
    ListenHost = listen,
    SignalingPort = port,
    ConnectivityMode = VoipConnectivityMode.Cloud
};

using var server = new VoipSignalingServer();
server.Start(settings);
if (!server.IsRunning)
{
    Console.Error.WriteLine($"Start fehlgeschlagen: {server.LastError ?? "unbekannt"}");
    return 1;
}

Console.WriteLine($"VoIP Cloud Signaling: {listen}:{port}{VoipConstants.SignalingWebSocketPath}");
Console.WriteLine("Strg+C zum Beenden.");

var exit = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    exit.TrySetResult();
};
await exit.Task.ConfigureAwait(false);
server.Stop();
return 0;
