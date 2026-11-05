using System.Windows.Threading;
using SmartOepnv.Core;
using SmartOepnv.Core.Sev;

namespace SmartOepnv.AppShared.Sev;

/// <summary>
/// Öffnen einer Route in der Planer-App überträgt Linie, Ziel und Verlauf an die SEV-TFT.
/// Die beim Laden markierte Route löst keinen Versand aus.
/// </summary>
public static class SevTftCoordinator
{
    private static readonly SemaphoreSlim SendGate = new(1, 1);
    private static int _suppressDepth;
    private static string? _openedRouteKey;
    private static CancellationTokenSource _debounce = new();

    public static string? OpenedRouteKey => _openedRouteKey;

    public static bool IsAutoSendSuppressed => Volatile.Read(ref _suppressDepth) > 0;

    public static event Action<string?>? OpenedRouteChanged;

    public static event Action<string>? StatusChanged;

    public static IDisposable SuppressAutoSend() => new SuppressScope();

    public static void NotifyRouteOpened(string? routeKey, bool autoSend)
    {
        _openedRouteKey = string.IsNullOrWhiteSpace(routeKey) ? null : routeKey.Trim();
        OpenedRouteChanged?.Invoke(_openedRouteKey);

        var previous = Interlocked.Exchange(ref _debounce, new CancellationTokenSource());
        previous.Cancel();

        if (!autoSend || _openedRouteKey is null)
        {
            return;
        }

        var token = _debounce.Token;
        var key = _openedRouteKey;
        _ = AutoSendAfterDelayAsync(key, token);
    }

    public static async Task<SevTftCallResult> SendRouteAsync(
        string routeKey,
        SevTftEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        if (!TryBuildFromEditor(routeKey, out var payload, out var error) || payload is null)
        {
            var failed = SevTftCallResult.Fail(error);
            Report(failed.Message);
            return failed;
        }

        return await PostAsync(endpoint, payload, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AutoSendAfterDelayAsync(string routeKey, CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(300, debounceToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        SevTftEndpoint? endpoint = null;
        SevTftRoutePayload? payload = null;
        var proceed = false;

        void Prepare()
        {
            if (!string.Equals(_openedRouteKey, routeKey, StringComparison.Ordinal))
            {
                return;
            }

            if (!TryReadSaved(out endpoint, out var message, out var silent))
            {
                if (!silent && !string.IsNullOrWhiteSpace(message))
                {
                    Report(message);
                }

                return;
            }

            if (!TryBuildFromEditor(routeKey, out payload, out var error))
            {
                Report(error);
                return;
            }

            proceed = true;
        }

        try
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                Prepare();
            }
            else
            {
                await dispatcher.InvokeAsync(Prepare, DispatcherPriority.Normal).Task.ConfigureAwait(false);
            }

            if (!proceed || endpoint is null || payload is null)
            {
                return;
            }

            if (!string.Equals(_openedRouteKey, routeKey, StringComparison.Ordinal))
            {
                return;
            }

            await PostAsync(endpoint, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // neuer Routenwechsel oder Abbruch
        }
        catch (Exception ex)
        {
            Report("SEV-TFT: " + ex.Message);
        }
    }

    private static async Task<SevTftCallResult> PostAsync(
        SevTftEndpoint endpoint,
        SevTftRoutePayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return SevTftCallResult.Fail("Senden abgebrochen.");
        }

        try
        {
            var result = await new SevTftClient().PostRouteAsync(endpoint, payload, cancellationToken)
                .ConfigureAwait(false);
            Report(result.Message);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failed = SevTftCallResult.Fail("SEV-TFT: " + ex.Message);
            Report(failed.Message);
            return failed;
        }
        finally
        {
            SendGate.Release();
        }
    }

    private static bool TryReadSaved(out SevTftEndpoint? endpoint, out string message, out bool silent)
    {
        endpoint = null;
        message = string.Empty;
        silent = false;
        if (!AppServices.IsInitialized || AppServices.PlanerAppSettings is null)
        {
            silent = true;
            return false;
        }

        var settings = AppServices.PlanerAppSettings.Load();
        if (!settings.SevTftEnabled || !settings.SevTftAutoSend)
        {
            silent = true;
            return false;
        }

        var token = new SevTftSecretStore(AppServices.SettingsSubfolder).LoadToken();
        if (!SevTftEndpoint.TryCreate(settings.SevTftHost, settings.SevTftPort.ToString(), token, out endpoint, out message))
        {
            silent = false;
            return false;
        }

        return true;
    }

    private static bool TryBuildFromEditor(
        string routeKey,
        out SevTftRoutePayload? payload,
        out string error)
    {
        payload = null;
        if (!AppServices.IsInitialized || AppServices.Routes.Editor is null)
        {
            error = "Kein Routenpaket geladen.";
            return false;
        }

        var stops = AppServices.Routes.Editor.GetStops(routeKey);
        // Betrieb folgt, sobald er an der Route hinterlegt ist.
        return SevTftRouteBuilder.TryBuild(routeKey, stops, betrieb: null, out payload, out error);
    }

    private static void Report(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            StatusChanged?.Invoke(message);
            return;
        }

        dispatcher.BeginInvoke(() => StatusChanged?.Invoke(message));
    }

    private sealed class SuppressScope : IDisposable
    {
        private int _disposed;

        public SuppressScope() => Interlocked.Increment(ref _suppressDepth);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref _suppressDepth);
            }
        }
    }
}
