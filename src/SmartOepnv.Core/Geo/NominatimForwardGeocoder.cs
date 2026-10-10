using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.Geo;

/// <summary>
/// Vorwärts-Geocoding (Adresse → Koordinaten) über OpenStreetMap Nominatim.
/// </summary>
public static class NominatimForwardGeocoder
{
    private const string UserAgent = "SmartOepnv-Planer/1.0 (gps stop picker; contact: planer@local)";
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private static readonly SemaphoreSlim RateGate = new(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(1.05);

    public sealed record Result(double Latitude, double Longitude, string DisplayName);

    public static async Task<Result?> TrySearchAsync(string query, CancellationToken ct = default)
    {
        var q = query.Trim();
        if (q.Length < 3)
        {
            return null;
        }

        await RateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var wait = _lastRequestUtc + MinRequestInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct).ConfigureAwait(false);
            }

            var url =
                "https://nominatim.openstreetmap.org/search?format=json&limit=1&addressdetails=0&q=" +
                Uri.EscapeDataString(q);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Accept-Language", "de");

            using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
            _lastRequestUtc = DateTime.UtcNow;
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr is null || arr.Count == 0)
            {
                return null;
            }

            var first = arr[0]?.AsObject();
            if (first is null)
            {
                return null;
            }

            var latRaw = first["lat"]?.GetValue<string>();
            var lonRaw = first["lon"]?.GetValue<string>();
            if (!double.TryParse(latRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(lonRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
                !double.IsFinite(lat) ||
                !double.IsFinite(lon))
            {
                return null;
            }

            var name = first["display_name"]?.GetValue<string>()?.Trim() ?? q;
            return new Result(lat, lon, name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            RateGate.Release();
        }
    }
}
