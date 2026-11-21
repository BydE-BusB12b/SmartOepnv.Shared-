using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// Einmal pro Fahrt, in der Sollsekunde der Starthaltestelle und höchstens 90 s danach,
/// solange das Fahrzeug im 100-m-Bereich steht.
/// </summary>
public static class Abfahrtston
{
    private const int RadiusMeters = 100;
    private static string _doneRoute = "";
    private static CancellationTokenSource? _timer;

    public static void Arm(FahrSession session)
    {
        Disarm();
        _doneRoute = "";
        _timer = new CancellationTokenSource();
        var token = _timer.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    Tick(session, session.Trip, session.Trip.LastLat, session.Trip.LastLon);
                }
                catch (Exception ex)
                {
                    FahrProtokoll.Add("Abfahrtston: " + ex.Message);
                }

                await Task.Delay(1000, token).ContinueWith(_ => { });
            }
        }, token);
    }

    public static void Disarm()
    {
        _timer?.Cancel();
        _timer = null;
    }

    public static void Tick(FahrSession session, FahrTrip trip, double lat, double lon)
    {
        if (!trip.Running || string.IsNullOrWhiteSpace(session.RouteName) || session.RouteName == _doneRoute)
        {
            return;
        }

        var stops = session.Package?.GetStops(session.RouteName);
        var start = stops?.FirstOrDefault(stop => !stop.IsWaypoint);
        if (start is null || !InWindow(start.Time))
        {
            return;
        }

        if (trip.HasFix && StopMatch.TryCoord(start, out var slat, out var slon) &&
            StopMatch.Meters(lat, lon, slat, slon) > RadiusMeters)
        {
            return;
        }

        if (FahrAnsage.Active)
        {
            return;
        }

        _doneRoute = session.RouteName;
        Task.Run(() =>
        {
            lock (Gate)
            {
                if (session.Package is not null)
                {
                    AnsageSequenz.PlayClip(session, "IBIS_startup.wav", false, AnnouncementAudioOutput.Both, 0.65, display: true);
                }

                if (!PlayedStartup(session))
                {
                    TonAusgang.PlayDisplay(Beep());
                }
            }
        });
    }

    private static readonly object Gate = new();

    private static bool PlayedStartup(FahrSession session)
    {
        if (session.Package is null)
        {
            return false;
        }

        var sounds = GpsAnsagenEmbeddedSoundsJson.ReadAllEntries(session.Package.PackageRoot);
        return AnsageWahl.Prefer(sounds, "IBIS_startup.wav", false) is not null;
    }

    private static bool InWindow(string time)
    {
        var parts = (time ?? "").Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var hour) || !int.TryParse(parts[1], out var minute))
        {
            return false;
        }

        var second = parts.Length > 2 && int.TryParse(parts[2], out var parsed) ? parsed : 0;
        var planned = hour * 3600 + minute * 60 + second;
        var now = DateTime.Now;
        var current = now.Hour * 3600 + now.Minute * 60 + now.Second;
        var remain = current - planned;
        if (remain < -12 * 3600)
        {
            remain += 24 * 3600;
        }

        return remain is >= 0 and <= 90;
    }

    private static byte[] Beep()
    {
        const int rate = 22050;
        var frames = rate * 700 / 1000;
        var samples = new short[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = (short)(Math.Sin(2 * Math.PI * 880 * i / rate) * 8000);
        }

        return PcmTone.Wrap(samples, rate);
    }
}

file static class PcmTone
{
    public static byte[] Wrap(short[] samples, int rate)
    {
        var data = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, data, 0, data.Length);
        var wav = new byte[44 + data.Length];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BitConverter.TryWriteBytes(wav.AsSpan(4), 36 + data.Length);
        System.Text.Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
        BitConverter.TryWriteBytes(wav.AsSpan(16), 16);
        BitConverter.TryWriteBytes(wav.AsSpan(20), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(22), (short)1);
        BitConverter.TryWriteBytes(wav.AsSpan(24), rate);
        BitConverter.TryWriteBytes(wav.AsSpan(28), rate * 2);
        BitConverter.TryWriteBytes(wav.AsSpan(32), (short)2);
        BitConverter.TryWriteBytes(wav.AsSpan(34), (short)16);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BitConverter.TryWriteBytes(wav.AsSpan(40), data.Length);
        data.CopyTo(wav, 44);
        return wav;
    }
}
