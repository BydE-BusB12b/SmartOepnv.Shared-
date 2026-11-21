using System.Text.Json.Nodes;
using Linux.Bluetooth;
using Linux.Bluetooth.Extensions;
using SmartOepnv.Core.VehicleTracking;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// BLE-Client für das Citaro-FMS-Gateway. Läuft nur unter Linux.
/// </summary>
public static class FmsLive
{
    private static readonly object Gate = new();
    private static int _started;
    private static JsonObject? _raw;
    private static CancellationTokenSource _listen = new();

    public static string Status { get; private set; } = "Aus";

    public static bool Enabled => GeraetEinstellungen.Bool("fms_enabled", false);

    public static void SetEnabled(bool enabled)
    {
        var values = GeraetEinstellungen.Load();
        values["fms_enabled"] = enabled ? "true" : "false";
        GeraetEinstellungen.Save(values);
        if (!enabled)
        {
            Status = "Aus";
            _listen.Cancel();
        }
    }

    public static void Start()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if (!Enabled)
        {
            Status = "Aus";
        }

        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _ = Task.Run(Watch);
    }

    public static IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (Gate)
        {
            if (_raw is null)
            {
                return new Dictionary<string, string>();
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in _raw)
            {
                map[pair.Key] = pair.Value?.ToJsonString().Trim('"') ?? "";
            }

            return map;
        }
    }

    public static JsonObject? PlannerObject()
    {
        if (!Enabled)
        {
            return null;
        }

        var selected = Forwarded();
        JsonObject? raw;
        lock (Gate)
        {
            raw = _raw?.DeepClone() as JsonObject;
        }

        if (raw is null)
        {
            return null;
        }

        var result = new JsonObject();
        foreach (var pair in raw)
        {
            if (selected.Contains(pair.Key) && pair.Value is not null)
            {
                result[pair.Key] = pair.Value.DeepClone();
            }
        }

        return result.Count == 0 ? null : result;
    }

    public static bool IsForwarded(string key) => Forwarded().Contains(key);

    public static void SetForwarded(string key, bool enabled)
    {
        var next = Forwarded();
        if (enabled)
        {
            next.Add(key);
        }
        else
        {
            next.Remove(key);
        }

        var values = GeraetEinstellungen.Load();
        values["fms_forward_ready"] = "true";
        values["fms_forward_keys"] = string.Join(",", next.OrderBy(k => k, StringComparer.Ordinal));
        GeraetEinstellungen.Save(values);
    }

    private static HashSet<string> Forwarded()
    {
        var values = GeraetEinstellungen.Load();
        if (!GeraetEinstellungen.Bool(values, "fms_forward_ready", false))
        {
            return FmsSignals.All.Where(s => s.DefaultForward).Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        }

        return (values.GetValueOrDefault("fms_forward_keys") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task Watch()
    {
        while (true)
        {
            if (!Enabled)
            {
                Status = "Aus";
                await Task.Delay(TimeSpan.FromSeconds(1));
                continue;
            }

            try
            {
                await Listen();
            }
            catch (OperationCanceledException)
            {
                Status = "Aus";
            }
            catch (Exception ex)
            {
                Status = "Getrennt";
                FahrProtokoll.Add("Fahrzeugdiagnose: " + ex.Message);
            }

            if (Enabled)
            {
                await Task.Delay(TimeSpan.FromSeconds(4));
            }
        }
    }

    private static async Task Listen()
    {
        _listen = new CancellationTokenSource();
        var token = _listen.Token;
        token.ThrowIfCancellationRequested();
        var adapters = await BlueZManager.GetAdaptersAsync();
        var adapter = adapters.FirstOrDefault() ?? throw new InvalidOperationException("Kein Bluetooth-Adapter.");
        await adapter.SetPoweredAsync(true);
        var found = new TaskCompletionSource<Device>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task Adapter_DeviceFound(Adapter sender, DeviceFoundEventArgs args)
        {
            _ = Accept(args.Device, found);
            return Task.CompletedTask;
        }

        adapter.DeviceFound += Adapter_DeviceFound;
        try
        {
            Status = "Suche " + FmsSignals.DeviceName + "…";
            await adapter.StartDiscoveryAsync();
            var device = await found.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
            await adapter.StopDiscoveryAsync();
            Status = "Verbinde…";
            await device.ConnectAsync();
            await device.WaitForPropertyValueAsync("Connected", true, TimeSpan.FromSeconds(15));
            await device.WaitForPropertyValueAsync("ServicesResolved", true, TimeSpan.FromSeconds(15));
            var service = await device.GetServiceAsync(FmsSignals.ServiceUuid);
            var characteristic = await service.GetCharacteristicAsync(FmsSignals.CharacteristicUuid);
            characteristic.Value += OnValue;
            await characteristic.StartNotifyAsync();
            Status = "Verbunden";
            var initial = await characteristic.ReadValueAsync(TimeSpan.FromSeconds(5));
            Remember(initial);
            var dropped = device.WaitForPropertyValueAsync("Connected", false, Timeout.InfiniteTimeSpan);
            var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, token);
            await Task.WhenAny(dropped, cancelled);
            if (token.IsCancellationRequested)
            {
                try
                {
                    await device.DisconnectAsync();
                }
                catch
                {
                    // Verbindung wird beim Ausschalten verworfen
                }

                throw new OperationCanceledException(token);
            }
        }
        finally
        {
            adapter.DeviceFound -= Adapter_DeviceFound;
            try
            {
                await adapter.StopDiscoveryAsync();
            }
            catch
            {
                // Suche war schon aus
            }
        }
    }

    private static async Task Accept(Device device, TaskCompletionSource<Device> found)
    {
        try
        {
            var name = await device.GetNameAsync();
            if (name.Equals(FmsSignals.DeviceName, StringComparison.OrdinalIgnoreCase) &&
                found.TrySetResult(device))
            {
                Status = "Gateway gefunden";
            }
        }
        catch
        {
            // Name noch nicht lesbar
        }
    }

    private static Task OnValue(GattCharacteristic characteristic, GattCharacteristicValueEventArgs args)
    {
        Remember(args.Value);
        return Task.CompletedTask;
    }

    private static void Remember(byte[] bytes)
    {
        var text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
        if (JsonNode.Parse(text) is not JsonObject obj)
        {
            return;
        }

        lock (Gate)
        {
            _raw = obj;
        }

        Status = "Verbunden";
    }
}
