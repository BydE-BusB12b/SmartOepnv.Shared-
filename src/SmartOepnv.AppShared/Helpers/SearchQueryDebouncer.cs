using System.Windows.Threading;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Verzögert Filter-Updates, damit Tippen in Suchfeldern die UI nicht blockiert.
/// </summary>
public sealed class SearchQueryDebouncer
{
    private readonly DispatcherTimer _timer;
    private readonly Action _apply;

    public SearchQueryDebouncer(Action apply, int delayMilliseconds = 120)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(delayMilliseconds, 40, 500))
        };
        _timer.Tick += OnTick;
    }

    public void Schedule()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void ApplyNow()
    {
        _timer.Stop();
        _apply();
    }

    /// <summary>Timer stoppen ohne Apply (z. B. vor explizitem Speichern).</summary>
    public void Cancel() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        _apply();
    }
}
