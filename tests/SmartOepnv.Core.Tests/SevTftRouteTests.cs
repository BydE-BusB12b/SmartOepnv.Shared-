using System.Net;
using System.Text;
using System.Text.Json;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Sev;
using Xunit;

namespace SmartOepnv.Core.Tests;

public sealed class SevTftRouteTests
{
    private const string RouteKey =
        "RE4 Düsseldorf Hbf > Neuss Hbf (Linie: 004/95, Fahrt: 12, Verkehr: Mo-Fr)";

    [Fact]
    public void Route_004_95_takes_line_and_destination_from_the_name()
    {
        var ok = SevTftRouteBuilder.TryBuild(
            RouteKey,
            SampleStops(),
            betrieb: null,
            out var payload,
            out var error);

        Assert.True(ok, error);
        Assert.NotNull(payload);
        Assert.Equal("RE4", payload!.Linie);
        Assert.Equal("Neuss Hauptbahnhof", payload.Ziel);
        Assert.Equal(
            ["Düsseldorf Hbf", "Düsseldorf-Bilk", "Neuss Süd", "Neuss Hauptbahnhof", "Neuss Rheinpark"],
            payload.Haltestellen);
        Assert.Equal(string.Empty, payload.Betrieb);
    }

    [Fact]
    public void Split_line_prefix_and_number_are_joined()
    {
        var ok = SevTftRouteBuilder.TryReadLineAndDestination(
            "RE 4 Düsseldorf Hbf > Neuss Hbf",
            out var linie,
            out var ziel,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("RE4", linie);
        Assert.Equal("Neuss Hbf", ziel);
    }

    [Fact]
    public void Calendar_prefix_does_not_become_the_line()
    {
        var ok = SevTftRouteBuilder.TryReadLineAndDestination(
            "13.07.2026 RE4 Düsseldorf Hbf > Neuss Hbf (Linie: 004/95, Fahrt: 1)",
            out var linie,
            out var ziel,
            out _);

        Assert.True(ok);
        Assert.Equal("RE4", linie);
        Assert.Equal("Neuss Hbf", ziel);
    }

    [Fact]
    public void Line_course_is_not_sent_as_the_passenger_line()
    {
        var ok = SevTftRouteBuilder.TryReadLineAndDestination(RouteKey, out var linie, out _, out _);
        Assert.True(ok);
        Assert.NotEqual("004/95", linie);
    }

    [Fact]
    public void Waypoints_are_left_out_and_order_stays()
    {
        var stops = new[]
        {
            Stop("A"),
            new RouteStopItem { Name = "nur Geokoordinate", IsWaypoint = true },
            Stop("B"),
            new RouteStopItem { Name = "  ", StopDisplay = "C über Anzeige" },
            Stop("D"),
            Stop("E")
        };

        var ok = SevTftRouteBuilder.TryBuild("S6 Start > Ziel", stops, null, out var payload, out var error);
        Assert.True(ok, error);
        Assert.Equal(["A", "B", "C über Anzeige", "D", "E"], payload!.Haltestellen);
    }

    [Fact]
    public void Empty_line_destination_or_stops_are_rejected()
    {
        Assert.False(SevTftRouteBuilder.TryBuild("Nur ein Name", SampleStops(), null, out _, out var nameError));
        Assert.Contains("Ziel", nameError, StringComparison.Ordinal);

        Assert.True(SevTftRouteBuilder.TryBuild("RE4 Start > ", SampleStops(), null, out var openDestination, out var openError));
        Assert.True(string.IsNullOrEmpty(openError));
        Assert.Equal("Neuss Hauptbahnhof", openDestination!.Ziel);

        Assert.False(SevTftRouteBuilder.TryBuild(RouteKey, [], null, out _, out var stopError));
        Assert.Contains("Haltestellen", stopError, StringComparison.Ordinal);
    }

    [Fact]
    public void Course_000_00_sends_standby_and_clears_the_plan()
    {
        var ok = SevTftRouteBuilder.TryBuild(
            "Pause (Linie: 000/00, Fahrt: 1)",
            SampleStops(),
            null,
            out var payload,
            out var error);

        Assert.True(ok, error);
        Assert.True(payload!.Standby);
        Assert.Equal("000/00", payload.Linie);
        Assert.Empty(payload.Haltestellen);

        var json = SevTftRoutePayload.ToJson(payload);
        Assert.Contains("\"linie\":\"000/00\"", json, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("000/00", doc.RootElement.GetProperty("linie").GetString());
        Assert.Equal("Standby", doc.RootElement.GetProperty("titel").GetString());
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("ziel").GetProperty("zeile1").GetString());
        Assert.Equal(string.Empty, doc.RootElement.GetProperty("ziel").GetProperty("zeile2").GetString());
        Assert.Empty(doc.RootElement.GetProperty("haltestellen").EnumerateArray());
        Assert.Empty(doc.RootElement.GetProperty("partner").EnumerateArray());
        Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("linie").ValueKind);
    }

    [Fact]
    public void A_normal_course_is_not_standby()
    {
        Assert.False(SevTftRouteBuilder.IsStandbyCourse(RouteKey));
        Assert.Equal("004/95", SevTftRouteBuilder.ReadCourse(RouteKey));
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), null, out var payload, out _));
        Assert.False(payload!.Standby);
        Assert.Equal("RE4", payload.Linie);
        using var doc = JsonDocument.Parse(SevTftRoutePayload.ToJson(payload));
        Assert.Equal("RE4", doc.RootElement.GetProperty("linie").GetString());
        Assert.False(doc.RootElement.TryGetProperty("standby", out _));
    }

    [Fact]
    public void Destination_is_the_end_stop_name()
    {
        var ok = SevTftRouteBuilder.TryBuild(
            "RE4 Düsseldorf Hbf > Neuss Hbf",
            [
                Stop("Düsseldorf Hbf"),
                new RouteStopItem { Name = "Neuss Hauptbahnhof", IsEndStop = true },
                Stop("Abstellung")
            ],
            null,
            out var payload,
            out var error);

        Assert.True(ok, error);
        Assert.Equal("RE4", payload!.Linie);
        Assert.Equal("Neuss Hauptbahnhof", payload.Ziel);
        Assert.Equal(["Düsseldorf Hbf", "Neuss Hauptbahnhof", "Abstellung"], payload.Haltestellen);
    }

    [Fact]
    public void Passed_stops_drop_off_and_the_destination_stays()
    {
        var ok = SevTftRouteBuilder.TryBuild(
            RouteKey,
            SampleStops(),
            null,
            out var payload,
            out var error,
            dropThroughIndex: 1);

        Assert.True(ok, error);
        Assert.Equal(["Neuss Süd", "Neuss Hauptbahnhof", "Neuss Rheinpark"], payload!.Haltestellen);
        Assert.Equal("Neuss Hauptbahnhof", payload.Ziel);
        Assert.Equal("RE4", payload.Linie);
    }

    [Fact]
    public void Advancing_past_the_last_stop_clears_the_stop_list()
    {
        var ok = SevTftRouteBuilder.TryBuild(
            RouteKey,
            SampleStops(),
            null,
            out var payload,
            out var error,
            dropThroughIndex: 4);

        Assert.True(ok, error);
        Assert.Empty(payload!.Haltestellen);
        Assert.Equal("Neuss Hauptbahnhof", payload.Ziel);
    }

    [Fact]
    public void Json_uses_the_agreed_fields_and_omits_empty_betrieb()
    {
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), null, out var payload, out _));
        using var doc = JsonDocument.Parse(SevTftRoutePayload.ToJson(payload!));
        Assert.Equal("RE4", doc.RootElement.GetProperty("linie").GetString());
        Assert.Equal("Neuss Hauptbahnhof", doc.RootElement.GetProperty("ziel").GetString());
        var stops = doc.RootElement.GetProperty("haltestellen").EnumerateArray().Select(s => s.GetString()).ToArray();
        Assert.Equal(payload!.Haltestellen, stops);
        Assert.False(doc.RootElement.TryGetProperty("betrieb", out _));
    }

    [Fact]
    public void Betrieb_is_sent_only_when_the_route_defines_it()
    {
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), "  Regiobahn  ", out var payload, out _));
        using var doc = JsonDocument.Parse(SevTftRoutePayload.ToJson(payload!));
        Assert.Equal("Regiobahn", doc.RootElement.GetProperty("betrieb").GetString());
    }

    [Fact]
    public void A_new_route_replaces_the_previous_payload()
    {
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), null, out var first, out _));
        var secondStops = new[] { Stop("Köln Hbf"), Stop("Köln Messe"), Stop("Leverkusen"), Stop("Düsseldorf Hbf"), Stop("Neuss Hbf") };
        Assert.True(SevTftRouteBuilder.TryBuild(
            "RE6 Köln Hbf > Neuss Hbf (Linie: 006/01, Fahrt: 3)",
            secondStops,
            null,
            out var second,
            out _));

        var json = SevTftRoutePayload.ToJson(second!);
        using var doc = JsonDocument.Parse(json);
        var stops = doc.RootElement.GetProperty("haltestellen").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(["Köln Hbf", "Köln Messe", "Leverkusen", "Düsseldorf Hbf", "Neuss Hbf"], stops);
        Assert.DoesNotContain(stops, stop => stop == "Düsseldorf-Bilk");
        Assert.Equal("RE6", second!.Linie);
        Assert.NotEqual(SevTftRoutePayload.ToJson(first!), json);
    }

    [Fact]
    public async Task Post_sends_bearer_token_and_the_route_json()
    {
        var handler = new ScriptedHandler(_ => JsonResponse(HttpStatusCode.OK, """{"ok":true}"""));
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), null, out var payload, out _));
        var endpoint = new SevTftEndpoint("192.168.1.50", 8080, "geheim");

        var result = await new SevTftClient(handler).PostRouteAsync(endpoint, payload!);

        Assert.True(result.Success, result.Message);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("http://192.168.1.50:8080/api/data", handler.Requests[0].RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
        Assert.Equal("geheim", handler.Requests[0].Headers.Authorization!.Parameter);
        Assert.Contains("\"linie\":\"RE4\"", handler.Bodies[0], StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests.Count);
    }

    [Fact]
    public async Task Post_without_token_sends_no_authorization_header()
    {
        var handler = new ScriptedHandler(_ => JsonResponse(HttpStatusCode.OK, "{}"));
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), null, out var payload, out _));
        await new SevTftClient(handler).PostRouteAsync(new SevTftEndpoint("10.0.0.8", 8080, null), payload!);
        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task Wrong_token_is_not_retried()
    {
        var handler = new ScriptedHandler(_ => JsonResponse(HttpStatusCode.Unauthorized, "denied"));
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), null, out var payload, out _));
        var result = await new SevTftClient(handler).PostRouteAsync(new SevTftEndpoint("10.0.0.8", 8080, "falsch"), payload!);
        Assert.False(result.Success);
        Assert.Contains("Token", result.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests.Count);
    }

    [Fact]
    public async Task Empty_or_invalid_json_from_the_pi_is_reported()
    {
        var handler = new ScriptedHandler(_ => JsonResponse(HttpStatusCode.BadRequest, "{\"fehler\":\"linie fehlt\"}"));
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), null, out var payload, out _));
        var result = await new SevTftClient(handler).PostRouteAsync(new SevTftEndpoint("10.0.0.8", 8080, null), payload!);
        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("linie fehlt", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timeout_is_retried_once_and_then_reported()
    {
        var handler = new ScriptedHandler(_ => throw new TaskCanceledException("timeout"));
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), null, out var payload, out _));
        var result = await new SevTftClient(handler).PostRouteAsync(new SevTftEndpoint("10.0.0.8", 8080, null), payload!);
        Assert.False(result.Success);
        Assert.Contains("Zeitüberschreitung", result.Message, StringComparison.Ordinal);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Bodies[0], handler.Bodies[1]);
    }

    [Fact]
    public async Task Connection_error_then_success_sends_the_same_route_again()
    {
        var attempt = 0;
        var handler = new ScriptedHandler(_ =>
        {
            attempt++;
            if (attempt == 1)
            {
                throw new HttpRequestException("Netz weg");
            }

            return JsonResponse(HttpStatusCode.OK, "{}");
        });
        Assert.True(SevTftRouteBuilder.TryBuild(RouteKey, SampleStops(), null, out var payload, out _));
        var result = await new SevTftClient(handler).PostRouteAsync(new SevTftEndpoint("10.0.0.8", 8080, null), payload!);
        Assert.True(result.Success, result.Message);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Bodies[0], handler.Bodies[1]);
    }

    [Fact]
    public async Task Get_data_uses_the_configured_port()
    {
        var handler = new ScriptedHandler(_ => JsonResponse(HttpStatusCode.OK, """{"linie":"RE4"}"""));
        var result = await new SevTftClient(handler).GetDataAsync(new SevTftEndpoint("192.168.0.20", 9797, null));
        Assert.True(result.Success, result.Message);
        Assert.Equal("http://192.168.0.20:9797/api/data", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public void Host_and_port_roundtrip_without_storing_the_token()
    {
        var directory = Path.Combine(Path.GetTempPath(), "sev-tft-tests", Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(directory, "planer_app_settings.json");
        var store = PlanerAppSettingsStore.CreateForFile(settingsPath);
        var settings = store.Load();
        settings.SevTftHost = "192.168.1.40";
        settings.SevTftPort = 8080;
        settings.SevTftEnabled = true;
        settings.SevTftAutoSend = true;
        store.Save(settings);

        var loaded = store.Load();
        Assert.Equal("192.168.1.40", loaded.SevTftHost);
        Assert.Equal(8080, loaded.SevTftPort);
        Assert.True(loaded.SevTftEnabled);
        Assert.True(loaded.SevTftAutoSend);

        var json = File.ReadAllText(settingsPath);
        Assert.DoesNotContain("apiToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("geheim", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Api_token_is_not_stored_as_plaintext()
    {
        var path = Path.Combine(Path.GetTempPath(), "sev-tft-tests", Guid.NewGuid().ToString("N") + ".dat");
        var store = SevTftSecretStore.CreateForFile(path);
        store.SaveToken("geheim-token");
        var raw = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        Assert.DoesNotContain("geheim-token", raw, StringComparison.Ordinal);
        Assert.Equal("geheim-token", store.LoadToken());
        store.SaveToken(null);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Pasted_url_is_reduced_to_the_host()
    {
        var ok = SevTftEndpoint.TryCreate("http://192.168.1.10:8080/api/route", "8080", null, out var endpoint, out var error);
        Assert.True(ok, error);
        Assert.Equal("192.168.1.10", endpoint!.Host);
        Assert.Equal(8080, endpoint.Port);
    }

    private static IReadOnlyList<RouteStopItem> SampleStops() =>
    [
        Stop("Düsseldorf Hbf"),
        Stop("Düsseldorf-Bilk"),
        new RouteStopItem { Name = "Wegpunkt", IsWaypoint = true },
        Stop("Neuss Süd"),
        new RouteStopItem { Name = "Neuss Hauptbahnhof", IsEndStop = true },
        Stop("Neuss Rheinpark")
    ];

    private static RouteStopItem Stop(string name) => new() { Name = name };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return _respond(request);
        }
    }
}
