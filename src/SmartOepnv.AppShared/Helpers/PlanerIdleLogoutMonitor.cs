using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Meldet den Planer-Nutzer nach Inaktivität automatisch ab (Dropbox-Sperre freigeben).
/// </summary>
public sealed class PlanerIdleLogoutMonitor : IDisposable
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    private readonly DispatcherTimer _timeoutTimer = new() { Interval = IdleTimeout };
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _idleDeadlineUtc;
    private bool _isActive;
    private bool _logoutInProgress;

    public event Func<Task>? IdleTimeoutReached;
    public event Action<TimeSpan?>? CountdownChanged;

    public PlanerIdleLogoutMonitor()
    {
        _timeoutTimer.Tick += OnTimeoutTick;
        _countdownTimer.Tick += OnCountdownTick;
    }

    public void Start()
    {
        if (!AppServices.IsPlannerApp || _isActive)
        {
            return;
        }

        InputManager.Current.PreProcessInput += OnPreProcessInput;
        _isActive = true;
        ResetTimer();
    }

    public void Stop()
    {
        if (!_isActive)
        {
            return;
        }

        _timeoutTimer.Stop();
        _countdownTimer.Stop();
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        _isActive = false;
        CountdownChanged?.Invoke(null);
    }

    public void ResetTimer()
    {
        if (!_isActive || _logoutInProgress)
        {
            return;
        }

        _idleDeadlineUtc = DateTime.UtcNow + IdleTimeout;
        _timeoutTimer.Stop();
        _timeoutTimer.Start();
        _countdownTimer.Start();
        PublishCountdown();
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (!_isActive || _logoutInProgress)
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

    private void OnCountdownTick(object? sender, EventArgs e) => PublishCountdown();

    private void PublishCountdown()
    {
        if (!_isActive || _logoutInProgress)
        {
            return;
        }

        var remaining = _idleDeadlineUtc - DateTime.UtcNow;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        CountdownChanged?.Invoke(remaining);
    }

    private async void OnTimeoutTick(object? sender, EventArgs e)
    {
        _timeoutTimer.Stop();
        _countdownTimer.Stop();
        CountdownChanged?.Invoke(TimeSpan.Zero);

        if (!_isActive || _logoutInProgress)
        {
            return;
        }

        if (AppServices.PlanerSession?.IsLoggedIn != true)
        {
            return;
        }

        var handler = IdleTimeoutReached;
        if (handler is null)
        {
            return;
        }

        _logoutInProgress = true;
        try
        {
            await handler().ConfigureAwait(true);
        }
        finally
        {
            _logoutInProgress = false;
            if (_isActive && AppServices.PlanerSession?.IsLoggedIn == true)
            {
                ResetTimer();
            }
        }
    }

    public void Dispose() => Stop();
}
