using System.Windows;
using Microsoft.Win32;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Beendet den Planer beim System-Ruhezustand (Deckel zu / Standby) mit Speichern und Sperrfreigabe.
/// </summary>
public sealed class PlanerSystemLifecycleMonitor : IDisposable
{
    private bool _suspendCloseTriggered;

    public void Start()
    {
        if (!AppServices.IsPlannerApp)
        {
            return;
        }

        Stop();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Stop() => SystemEvents.PowerModeChanged -= OnPowerModeChanged;

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Suspend || _suspendCloseTriggered)
        {
            return;
        }

        _suspendCloseTriggered = true;
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        _ = app.Dispatcher.BeginInvoke(() =>
        {
            if (app.MainWindow is MainShellWindow shell)
            {
                shell.RequestAutoCloseFromSystemSuspend();
                return;
            }

            SmartOepnvAppHost.EnsurePlanerShutdownSaveAndRelease();
            app.Shutdown();
        });
    }

    public void Dispose() => Stop();
}
