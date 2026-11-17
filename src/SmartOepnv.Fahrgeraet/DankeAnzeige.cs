using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Wie auf dem Tablet: DS021T zeigt „Danke“ und E15, Mobitec den Smile.
/// Nach 10 Sekunden kommt das vorherige Ziel zurück.
/// </summary>
public static class DankeAnzeige
{
    private static string _line = "";
    private static string _destination = "";
    private static CancellationTokenSource? _restore;
    private static int _generation;

    public static void Remember(string? line, string? destination)
    {
        var name = destination?.Trim() ?? "";
        if (name.Length == 0 || name.Equals("Danke", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _line = line?.Trim() ?? "";
        _destination = name;
    }

    public static async Task<string> Show(FahrSession session)
    {
        var protocol = session.OutsideProtocol ?? "";
        var ds021t = protocol == "DS021T" || protocol.StartsWith("DS021 Test", StringComparison.Ordinal);
        var mobitec = protocol == "Mobitec";
        if (!ds021t && !mobitec)
        {
            return "Danke nur bei DS021T oder Mobitec.";
        }

        var generation = Interlocked.Increment(ref _generation);
        _restore?.Cancel();
        _restore = new CancellationTokenSource();
        var token = _restore.Token;
        _ = RestoreLater(session, generation, token);

        if (mobitec)
        {
            foreach (var frame in MobitecZiel.Danke())
            {
                VehicleLink.Send(session.Rs485Port, frame, 4800);
            }

            return "Danke auf Mobitec, 10 s.";
        }

        VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("l000"), 9600);
        await Task.Delay(50);
        VehicleLink.Send(session.IbisPort, DisplayTelegrams.Ibis("lE15"), 9600);
        await Task.Delay(50);
        VehicleLink.Send(
            session.IbisPort,
            Ds021tProgramBuilder.CreateFrontProgramA2([("Danke", "")], 3),
            9600);
        VehicleLink.Send(
            session.IbisPort,
            Ds021tProgramBuilder.CreateSideProgramA2([("Danke", "")], 3),
            9600);
        return "Danke und E15, 10 s.";
    }

    private static async Task RestoreLater(FahrSession session, int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (generation != _generation)
        {
            return;
        }

        var line = _line;
        var destination = _destination;
        if (destination.Length == 0)
        {
            var start = session.Package?.GetStops(session.RouteName ?? "")
                .FirstOrDefault(stop => !stop.IsWaypoint);
            if (start is not null)
            {
                line = start.LineNumber ?? "";
                destination = First(start.Destination, start.EndDestination, start.Ds021NeuDestination);
            }
        }

        if (destination.Length == 0)
        {
            return;
        }

        await DisplayTelegrams.SendAsync(session, line, destination, destination, null);
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
}
