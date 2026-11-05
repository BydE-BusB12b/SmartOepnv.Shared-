using System.Text.Encodings.Web;
using System.Text.Json;

namespace SmartOepnv.Core.Sev;

/// <summary>JSON für POST /api/data der Raspberry-Pi-Anzeige. Ziel als Text legt der Pi in ziel.zeile1 ab.</summary>
public sealed class SevTftRoutePayload
{
    public const string RoutePath = "/api/data";
    public const string DataPath = "/api/data";

    public required string Linie { get; init; }

    public required string Ziel { get; init; }

    public required IReadOnlyList<string> Haltestellen { get; init; }

    /// <summary>
    /// Verkehrsunternehmen der Fahrt. Leer, bis es an der Route hinterlegt wird.
    /// Dann erscheint es als Feld <c>betrieb</c>.
    /// </summary>
    public string Betrieb { get; init; } = string.Empty;

    /// <summary>Sonderkurs 000/00: der Pi schaltet bei diesem Text in „linie“ den Bildschirm aus.</summary>
    public bool Standby { get; init; }

    public static string ToJson(SevTftRoutePayload payload)
    {
        if (payload.Standby)
        {
            var standby = new Dictionary<string, object>
            {
                ["linie"] = "000/00",
                ["titel"] = "Standby",
                ["ziel"] = new Dictionary<string, string>
                {
                    ["zeile1"] = string.Empty,
                    ["zeile2"] = string.Empty
                },
                ["haltestellen"] = Array.Empty<string>(),
                ["partner"] = Array.Empty<string>()
            };
            return JsonSerializer.Serialize(standby, JsonOptions);
        }

        var body = new Dictionary<string, object>
        {
            ["linie"] = payload.Linie,
            ["ziel"] = payload.Ziel,
            ["haltestellen"] = payload.Haltestellen.ToArray()
        };

        if (!string.IsNullOrWhiteSpace(payload.Betrieb))
        {
            body["betrieb"] = payload.Betrieb.Trim();
        }

        return JsonSerializer.Serialize(body, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
