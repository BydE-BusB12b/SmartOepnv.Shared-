using System.Net.Http.Headers;
using System.Text;

namespace SmartOepnv.Core.Sev;

/// <summary>
/// Schreibt eine Route an die Pi-Anzeige und liest den aktuellen Stand.
/// Eine Wiederholung nur, wenn gar keine Antwort kam. Dieselbe Route ersetzt auf der Pi den bisherigen Plan.
/// </summary>
public sealed class SevTftClient
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    private const int MaxAttempts = 2;

    private readonly HttpMessageHandler? _handler;

    public SevTftClient(HttpMessageHandler? handler = null)
    {
        _handler = handler;
    }

    public Task<SevTftCallResult> PostRouteAsync(
        SevTftEndpoint endpoint,
        SevTftRoutePayload payload,
        CancellationToken cancellationToken = default)
    {
        var json = SevTftRoutePayload.ToJson(payload);
        var success = payload.Standby
            ? "Gesendet: Standby, Bildschirm aus."
            : $"Gesendet: {payload.Linie} → {payload.Ziel}, {payload.Haltestellen.Count} Haltestellen.";
        return SendAsync(
            endpoint,
            SevTftRoutePayload.RoutePath,
            HttpMethod.Post,
            json,
            success,
            cancellationToken);
    }

    public Task<SevTftCallResult> GetDataAsync(
        SevTftEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var success = $"Verbindung steht ({endpoint.Host}:{endpoint.Port}).";
        return SendAsync(
            endpoint,
            SevTftRoutePayload.DataPath,
            HttpMethod.Get,
            content: null,
            success,
            cancellationToken);
    }

    private async Task<SevTftCallResult> SendAsync(
        SevTftEndpoint endpoint,
        string path,
        HttpMethod method,
        string? content,
        string successMessage,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var client = CreateClient();
                using var request = new HttpRequestMessage(method, endpoint.CreateUri(path));
                if (!string.IsNullOrEmpty(endpoint.ApiToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiToken);
                }

                if (content is not null)
                {
                    request.Content = new StringContent(content, Encoding.UTF8, "application/json");
                }

                using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return SevTftCallResult.Ok(successMessage, (int)response.StatusCode);
                }

                return SevTftCallResult.Fail(DescribeHttpError(response.StatusCode, body), (int)response.StatusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return SevTftCallResult.Fail("Senden abgebrochen.");
            }
            catch (OperationCanceledException ex)
            {
                lastError = ex;
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
            }

            if (attempt < MaxAttempts)
            {
                try
                {
                    await Task.Delay(400, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return SevTftCallResult.Fail("Senden abgebrochen.");
                }
            }
        }

        if (lastError is OperationCanceledException)
        {
            return SevTftCallResult.Fail(
                $"Zeitüberschreitung – Raspberry Pi unter {endpoint.Host}:{endpoint.Port} nicht erreicht.");
        }

        return SevTftCallResult.Fail(
            $"Verbindung fehlgeschlagen ({endpoint.Host}:{endpoint.Port}). {lastError?.Message}".Trim());
    }

    private HttpClient CreateClient()
    {
        var client = _handler is null
            ? new HttpClient()
            : new HttpClient(_handler, disposeHandler: false);
        client.Timeout = RequestTimeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SmartOepnv-Planer/SEV-TFT");
        return client;
    }

    private static string DescribeHttpError(System.Net.HttpStatusCode status, string body)
    {
        var code = (int)status;
        var snippet = Snippet(body);
        return status switch
        {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
                $"Zugriff abgelehnt (HTTP {code}). API-Token prüfen.",
            System.Net.HttpStatusCode.BadRequest =>
                snippet.Length == 0
                    ? "Der Raspberry Pi hat die Route abgelehnt (HTTP 400)."
                    : $"Der Raspberry Pi hat die Route abgelehnt (HTTP 400): {snippet}",
            _ => snippet.Length == 0
                ? $"Raspberry Pi antwortet mit HTTP {code}."
                : $"Raspberry Pi antwortet mit HTTP {code}: {snippet}"
        };
    }

    private static string Snippet(string body)
    {
        var compact = (body ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 180 ? compact : compact[..180];
    }
}
