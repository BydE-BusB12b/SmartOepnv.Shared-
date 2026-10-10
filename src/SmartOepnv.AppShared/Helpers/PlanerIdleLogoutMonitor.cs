using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Meldet den Planer-Nutzer nach Inaktivität automatisch ab (Dropbox-Sperre freigeben).
/// Nutzt Wall-Clock-Zeit, damit Countdown und Abmeldung auch im Hintergrund greifen.
/// </summary>
public sealed class PlanerIdleLogoutMonitor : IDisposable
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private System.Threading.Timer? _wallClockTimer;
    private DateTime _idleDeadlineUtc;
    private bool _isActive;
    private bool _logoutInProgress;
    private int _suspendCount;

    public event Func<Task>? IdleTimeoutReached;
    public event Action<TimeSpan?>? CountdownChanged;

    public void Start()
    {
        if (!AppServices.IsPlannerApp)
        {
            return;
        }

        lock (_sync)
        {
            if (!_isActive)
            {
                InputManager.Current.PreProcessInput += OnPreProcessInput;
                _isActive = true;
            }

            if (_suspendCount == 0 && !_logoutInProgress)
            {
                ResetTimerCore();
            }

            EnsureWallClockTimerRunning();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_isActive)
            {
                _suspendCount = 0;
                _logoutInProgress = false;
                return;
            }

            _wallClockTimer?.Dispose();
            _wallClockTimer = null;
            _isActive = false;
            _suspendCount = 0;
            _logoutInProgress = false;
        }

        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        PublishCountdownOnUi(null);
    }

    /// <summary>Monitor neu starten und Countdown-Anzeige sofort aktualisieren (z. B. nach abgebrochenem Schließen).</summary>
    public void Restart()
    {
        if (!AppServices.IsPlannerApp)
        {
            return;
        }

        lock (_sync)
        {
            if (!_isActive)
            {
                InputManager.Current.PreProcessInput += OnPreProcessInput;
                _isActive = true;
            }

            _suspendCount = 0;
            _logoutInProgress = false;
            ResetTimerCore();
            EnsureWallClockTimerRunning();
        }
    }

    public void ResetTimer()
    {
        lock (_sync)
        {
            if (!_isActive || _logoutInProgress || _suspendCount > 0)
            {
                return;
            }

            ResetTimerCore();
        }
    }

    /// <summary>
    /// Pausiert Countdown und Auto-Abmeldung während langer Vorgänge (Sync, Speichern).
    /// Unterstützt verschachtelte Aufrufe.
    /// </summary>
    public void Suspend()
    {
        lock (_sync)
        {
            _suspendCount++;
        }

        PublishCountdownOnUi(null);
    }

    public void Resume()
    {
        var shouldHide = false;
        lock (_sync)
        {
            if (_suspendCount > 0)
            {
                _suspendCount--;
            }

            if (_suspendCount == 0 && _isActive && !_logoutInProgress)
            {
                ResetTimerCore();
                return;
            }

            shouldHide = _suspendCount > 0;
        }

        if (shouldHide)
        {
            PublishCountdownOnUi(null);
        }
    }

    private void ResetTimerCore()
    {
        _idleDeadlineUtc = DateTime.UtcNow + IdleTimeout;
        PublishCountdownOnUi(GetRemaining());
    }

    private void EnsureWallClockTimerRunning()
    {
        _wallClockTimer ??= new System.Threading.Timer(
            OnWallClockTick,
            null,
            TickInterval,
            TickInterval);
    }

    private void OnWallClockTick(object? state)
    {
        TimeSpan remaining;
        var shouldLogout = false;

        lock (_sync)
        {
            if (!_isActive || _logoutInProgress || _suspendCount > 0)
            {
                return;
            }

            if (AppServices.PlanerSession?.IsLoggedIn != true)
            {
                return;
            }

            remaining = GetRemaining();
            shouldLogout = remaining <= TimeSpan.Zero;
        }

        PublishCountdownOnUi(shouldLogout ? TimeSpan.Zero : remaining);

        if (shouldLogout)
        {
            BeginIdleLogout();
        }
    }

    private void BeginIdleLogout()
    {
        lock (_sync)
        {
            if (!_isActive || _logoutInProgress)
            {
                return;
            }

            _logoutInProgress = true;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            CompleteIdleLogoutAttempt();
            return;
        }

        _ = dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (!_isActive || AppServices.PlanerSession?.IsLoggedIn != true)
                {
                    return;
                }

                var handler = IdleTimeoutReached;
                if (handler is not null)
                {
                    await handler().ConfigureAwait(true);
                }
            }
            finally
            {
                CompleteIdleLogoutAttempt();
            }
        }, DispatcherPriority.Normal);
    }

    private void CompleteIdleLogoutAttempt()
    {
        lock (_sync)
        {
            _logoutInProgress = false;
            if (_isActive && AppServices.PlanerSession?.IsLoggedIn == true)
            {
                ResetTimerCore();
            }
        }
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (!_isActive || _logoutInProgress || _suspendCount > 0)
        {
            return;
        }

        if (AppServices.PlanerSession?.IsLoggedIn != true)
        {
            return;
        }

        if (!IsUserActivity(e.StagingItem.Input))
        {
            return;
        }

        ResetTimer();
    }

    private static bool IsUserActivity(InputEventArgs input) =>
        input is MouseEventArgs or KeyEventArgs or TextCompositionEventArgs or TouchEventArgs;

    private TimeSpan GetRemaining()
    {
        var remaining = _idleDeadlineUtc - DateTime.UtcNow;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private void PublishCountdownOnUi(TimeSpan? remaining)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            CountdownChanged?.Invoke(remaining);
            return;
        }

        _ = dispatcher.BeginInvoke(
            () => CountdownChanged?.Invoke(remaining),
            DispatcherPriority.Background);
    }

    public void Dispose() => Stop();
}
