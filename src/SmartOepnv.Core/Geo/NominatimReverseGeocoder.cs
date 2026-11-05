using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.Geo;

/// <summary>
/// Reverse-Geocoding über OpenStreetMap Nominatim (max. 1 Anfrage/s).
/// </summary>
public sealed class NominatimReverseGeocoder
{
    private const string UserAgent = "SmartOepnv-Leitstelle/1.0 (vehicle tracking; contact: leitstelle@local)";
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(1.05);
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private readonly ConcurrentDictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _rateGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public async Task<string?> TryResolveStreetAsync(
        double latitude,
        double longitude,
        CancellationToken ct = default)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
        {
            return null;
        }

        if (Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
        {
            return null;
        }

        var cacheKey = BuildCacheKey(latitude, longitude);
        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        await _rateGate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            var wait = _lastRequestUtc + MinRequestInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            var street = await FetchStreetAsync(latitude, longitude, ct);
            _lastRequestUtc = DateTime.UtcNow;
            _cache[cacheKey] = street;
            return street;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _cache[cacheKey] = null;
            return null;
        }
        finally
        {
            _rateGate.Release();
        }
    }

    private static async Task<string?> FetchStreetAsync(double latitude, double longitude, CancellationToken ct)
    {
        var lat = latitude.ToString("F5", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("F5", CultureInfo.InvariantCulture);
        var url =
            $"https://nominatim.openstreetmap.org/reverse?lat={lat}&lon={lon}&format=json&addressdetails=1&zoom=18";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.TryAddWithoutValidation("Accept-Language", "de");

        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var root = JsonNode.Parse(json)?.AsObject();
        var address = root?["address"]?.AsObject();
        if (address is null)
        {
            return null;
        }

        var street = FormatStreet(address);
        if (!string.IsNullOrWhiteSpace(street))
        {
            return street;
        }

        return root?["display_name"]?.GetValue<string>()?.Trim();
    }

    private static string? FormatStreet(JsonObject address)
    {
        var road = address["road"]?.GetValue<string>()
                   ?? address["pedestrian"]?.GetValue<string>()
                   ?? address["footway"]?.GetValue<string>()
                   ?? address["path"]?.GetValue<string>()
                   ?? address["cycleway"]?.GetValue<string>()
                   ?? address["residential"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(road))
        {
            return null;
        }

        var houseNumber = address["house_number"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(houseNumber) ? road.Trim() : $"{road.Trim()} {houseNumber.Trim()}";
    }

    private static string BuildCacheKey(double latitude, double longitude) =>
        $"{Math.Round(latitude, 4):F4},{Math.Round(longitude, 4):F4}";
}
