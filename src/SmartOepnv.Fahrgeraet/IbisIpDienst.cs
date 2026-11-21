using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// VDV-301-Dienst auf dem gespeicherten Lauschport. Antworten ohne XML-Namespace,
/// wie die Tablet-App.
/// </summary>
public static class IbisIpDienst
{
    private static readonly object Gate = new();
    private static HttpListener? _listener;
    private static int _port;
    private static string _line = "";
    private static string _destination = "";
    private static string _stop = "";
    private static int _stopIndex;
    private static string _announcement = "";
    private static double _lat;
    private static double _lon;
    private static double _speed;
    private static bool _hasGnss;
    private static List<RouteStopItem> _stops = [];
    private static readonly List<Sub> Subs = [];

    public static void Ensure(FahrSession session)
    {
        var port = session.IbisIpListenPort <= 0 ? 8080 : session.IbisIpListenPort;
        lock (Gate)
        {
            if (_listener is { IsListening: true } && _port == port)
            {
                return;
            }

            Stop();
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add($"http://+:{port}/");
                listener.Start();
            }
            catch
            {
                listener.Close();
                listener = new HttpListener();
                listener.Prefixes.Add($"http://localhost:{port}/");
                listener.Start();
            }

            _listener = listener;
            _port = port;
            FahrProtokoll.Add("IBIS-IP lauscht auf " + port);
            _ = Task.Run(Loop);
            _ = Task.Run(Heartbeat);
            TryAvahi(port);
        }
    }

    public static void NoteStop(FahrSession session, RouteStopItem stop, string destination)
    {
        var stops = session.Package?.GetStops(session.RouteName ?? "")?.ToList() ?? [];
        var index = Math.Max(0, stops.FindIndex(item => item.Name == stop.Name));
        lock (Gate)
        {
            _stops = stops;
            _stop = stop.Name;
            _stopIndex = index;
            _line = stop.LineNumber;
            _destination = destination;
        }

        var vrr = stop.VrrStopId?.Trim();
        if (!string.IsNullOrWhiteSpace(vrr))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await VrrAbfahrten.RefreshAsync(vrr);
                }
                catch
                {
                    // Echtzeit kurz nicht erreichbar
                }
            });
        }
    }

    public static void NoteAnnouncement(string text)
    {
        lock (Gate)
        {
            _announcement = text;
        }
    }

    public static void NoteGnss(double lat, double lon, double speedMps)
    {
        lock (Gate)
        {
            _lat = lat;
            _lon = lon;
            _speed = speedMps;
            _hasGnss = true;
        }
    }

    public static void NoteManual(string line, string destination)
    {
        lock (Gate)
        {
            _line = line;
            _destination = destination;
        }
    }

    private static void Stop()
    {
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // Der nächste Start ersetzt den Dienst.
        }

        _listener = null;
    }

    private static async Task Loop()
    {
        var listener = _listener;
        while (listener is { IsListening: true })
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => Answer(context));
        }
    }

    private static void Answer(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            var body = ReadBody(context.Request);
            var op = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            string xml;
            if (op.Contains("Unsubscribe", StringComparison.OrdinalIgnoreCase))
            {
                RemoveSub(path, body);
                xml = Response("UnsubscribeResponse", "<Active><Value>false</Value></Active>");
            }
            else if (op.Contains("Subscribe", StringComparison.OrdinalIgnoreCase))
            {
                Remember(path, body, context.Request.RemoteEndPoint?.Address?.ToString() ?? "127.0.0.1");
                xml = Response("SubscribeResponse", "<Active><Value>true</Value></Active><Heartbeat><Value>PT30S</Value></Heartbeat>");
            }
            else
            {
                xml = Payload(path);
            }

            var bytes = Encoding.UTF8.GetBytes(xml);
            context.Response.ContentType = "application/xml";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
            context.Response.Close();
        }
        catch
        {
            try
            {
                context.Response.Abort();
            }
            catch
            {
                // Die Verbindung ist schon weg.
            }
        }
    }

    private static string Payload(string path)
    {
        lock (Gate)
        {
            if (path.Contains("GetCurrentStopIndex", StringComparison.OrdinalIgnoreCase))
            {
                return Response("CustomerInformationService.GetCurrentStopIndexResponse",
                    $"{Stamp()}<CurrentStopIndexData>{Stamp()}<CurrentStopIndex><Value>{_stopIndex + 1}</Value></CurrentStopIndex></CurrentStopIndexData>");
            }

            if (path.Contains("GetCurrentStopPoint", StringComparison.OrdinalIgnoreCase))
            {
                return Response("CustomerInformationService.GetCurrentStopPointResponse",
                    $"""
                    <CurrentStopPointData>
                      {Stamp()}
                      <CurrentStopPoint>
                        <StopIndex><Value>{_stopIndex + 1}</Value></StopIndex>
                        <StopRef><Value>{Token(_stop)}</Value></StopRef>
                        <StopName><Value>{Esc(_stop)}</Value><Language>de</Language></StopName>
                        <DisplayContent>{Display()}</DisplayContent>
                      </CurrentStopPoint>
                    </CurrentStopPointData>
                    """);
            }

            if (path.Contains("GetCurrentAnnouncement", StringComparison.OrdinalIgnoreCase))
            {
                return Response("CustomerInformationService.GetCurrentAnnouncementResponse",
                    $"""
                    <CurrentAnnouncementData>
                      {Stamp()}
                      <CurrentAnnouncement>
                        <AnnouncementRef><Value>ANN_CURRENT</Value></AnnouncementRef>
                        <AnnouncementText><Value>{Esc(_announcement)}</Value><Language>de</Language></AnnouncementText>
                      </CurrentAnnouncement>
                    </CurrentAnnouncementData>
                    """);
            }

            if (path.Contains("GetCurrentConnectionInformation", StringComparison.OrdinalIgnoreCase))
            {
                return Response(
                    "CustomerInformationService.GetCurrentConnectionInformationResponse",
                    VrrAbfahrten.ConnectionXml(Stamp()));
            }

            if (path.Contains("GetGNSS", StringComparison.OrdinalIgnoreCase) || path.Contains("GNSSLocation", StringComparison.OrdinalIgnoreCase))
            {
                if (!_hasGnss)
                {
                    return Response("GNSSLocationService.GetGNSSLocationResponse", "<ErrorCode><Value>No GNSS fix available</Value></ErrorCode>");
                }

                return Response("GNSSLocationService.Data",
                    $"""
                    <latitude><Degree><Value>{Math.Abs(_lat).ToString(System.Globalization.CultureInfo.InvariantCulture)}</Value></Degree><Direction><Value>{(_lat < 0 ? "S" : "N")}</Value></Direction></latitude>
                    <longitude><Degree><Value>{Math.Abs(_lon).ToString(System.Globalization.CultureInfo.InvariantCulture)}</Value></Degree><Direction><Value>{(_lon < 0 ? "W" : "E")}</Value></Direction></longitude>
                    <SpeedOverGround><Value>{_speed.ToString(System.Globalization.CultureInfo.InvariantCulture)}</Value></SpeedOverGround>
                    <GNSSType>GPS</GNSSType>
                    <GNSSCoordinateSystem>WGS84</GNSSCoordinateSystem>
                    """);
            }

            if (path.Contains("GetDevice", StringComparison.OrdinalIgnoreCase))
            {
                return Response("DeviceManagementService.GetDeviceInformationResponse",
                    $"{Stamp()}<DeviceName><Value>SmartOepnv</Value></DeviceName><SerialNumber><Value>{Token(_line)}</Value></SerialNumber>");
            }

            var trip = $"""
                <TripInformation>
                  <TripRef><Value>{Token(_line + "_" + _destination)}</Value></TripRef>
                  <StopSequence>
                {string.Join("\n", _stops.Select((stop, index) => $"<StopPoint><StopIndex><Value>{index + 1}</Value></StopIndex><StopRef><Value>{Token(stop.Name)}</Value></StopRef><StopName><Value>{Esc(stop.Name)}</Value><Language>de</Language></StopName></StopPoint>"))}
                  </StopSequence>
                </TripInformation>
                <CurrentStopIndex><Value>{_stopIndex + 1}</Value></CurrentStopIndex>
                """;
            if (path.Contains("GetTripData", StringComparison.OrdinalIgnoreCase) || path.Contains("JourneyInformation", StringComparison.OrdinalIgnoreCase))
            {
                return Response("CustomerInformationService.GetTripDataResponse", $"<TripData>{Stamp()}{trip}</TripData>");
            }

            if (path.Contains("GetVehicleData", StringComparison.OrdinalIgnoreCase))
            {
                return Response("CustomerInformationService.GetVehicleDataResponse", $"{Stamp()}<VehicleRef><Value>SmartOepnv</Value></VehicleRef>");
            }

            return Response("CustomerInformationService.GetAllDataResponse",
                $"<AllData>{Stamp()}<VehicleRef><Value>SmartOepnv</Value></VehicleRef><DefaultLanguage><Value>de</Value></DefaultLanguage>{trip}<GlobalDisplayContent>{Display()}</GlobalDisplayContent></AllData>");
        }
    }

    private static string Display() =>
        $"""
        <DisplayContentRef><Value>Front</Value></DisplayContentRef>
        <LineInformation><LineRef><Value>{Token("LINE_" + _line)}</Value></LineRef><LineName><Value>{Esc(_line)}</Value><Language>de</Language></LineName></LineInformation>
        <Destination><DestinationRef><Value>{Token("DEST_" + _destination)}</Value></DestinationRef><DestinationName><Value>{Esc(_destination)}</Value><Language>de</Language></DestinationName></Destination>
        """;

    private static async Task Heartbeat()
    {
        while (_listener is { IsListening: true })
        {
            List<Sub> copy;
            lock (Gate)
            {
                copy = Subs.ToList();
            }

            foreach (var sub in copy)
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                    await http.PostAsync(sub.Url, new StringContent(Payload(sub.Topic), Encoding.UTF8, "application/xml"));
                }
                catch
                {
                    // Ein stiller Empfänger fällt beim nächsten Abbestellen weg.
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(25));
        }
    }

    private static void Remember(string path, string body, string remote)
    {
        var ip = Value(body, "Client-IP-Address") ?? remote;
        var portText = Value(body, "ReplyPort");
        if (!int.TryParse(portText, out var port))
        {
            return;
        }

        var reply = Value(body, "ReplyPath") ?? "/";
        if (!reply.StartsWith('/'))
        {
            reply = "/" + reply;
        }

        var topic = path;
        lock (Gate)
        {
            Subs.RemoveAll(sub => sub.Url.StartsWith($"http://{ip}:{port}", StringComparison.OrdinalIgnoreCase));
            Subs.Add(new Sub($"http://{ip}:{port}{reply}", topic));
        }
    }

    private static void RemoveSub(string path, string body)
    {
        var ip = Value(body, "Client-IP-Address") ?? "";
        lock (Gate)
        {
            Subs.RemoveAll(sub => sub.Topic.Contains(path, StringComparison.OrdinalIgnoreCase) ||
                                  ip.Length > 0 && sub.Url.Contains(ip, StringComparison.Ordinal));
        }
    }

    private static string? Value(string xml, string tag)
    {
        var match = Regex.Match(xml, $"<{tag}>\\s*<Value>(.*?)</Value>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string ReadBody(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
        {
            return "";
        }

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        return reader.ReadToEnd();
    }

    private static string Stamp() =>
        $"<TimeStamp><Value>{DateTime.UtcNow:yyyy-MM-dd'T'HH:mm:ss'Z'}</Value></TimeStamp>";

    private static string Response(string root, string body) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<{root}>\n{body}\n</{root}>";

    private static string Esc(string? text) =>
        (text ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Token(string? text)
    {
        var cleaned = Regex.Replace(text ?? "", "[^A-Za-z0-9._\\-]", "_").Trim('_');
        return cleaned.Length == 0 ? "UNKNOWN" : cleaned[..Math.Min(64, cleaned.Length)];
    }

    private static void TryAvahi(int port)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "avahi-publish-service",
                ArgumentList = { "SmartOepnv", "_ibisip_http._tcp", port.ToString() },
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // Ohne avahi bleibt der Dienst über die IP erreichbar.
        }
    }

    private sealed record Sub(string Url, string Topic);
}
