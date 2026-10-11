using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Effects;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Models;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Session;

namespace SmartOepnv.AppShared;

public partial class MainShellWindow : Window
{
    private static readonly System.Windows.Media.SolidColorBrush IdleCountdownNormalBrush =
        System.Windows.Media.Brushes.White;
    private static readonly System.Windows.Media.SolidColorBrush IdleCountdownWarningBrush = CreateFrozenBrush(0xFF, 0xD5, 0x4F);
    private static readonly System.Windows.Media.SolidColorBrush IdleCountdownCriticalBrush = CreateFrozenBrush(0xFF, 0x8A, 0x65);

    private bool _closeConfirmed;
    private bool _closeHandlerRunning;
    private bool _softwareUpdateChecked;
    private PlanerIdleLogoutMonitor? _idleLogoutMonitor;
    private PlanerSystemLifecycleMonitor? _lifecycleMonitor;

    public bool LoginGateActive { get; private set; }

    public MainShellWindow()
    {
        InitializeComponent();
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;

        if (AppServices.IsPlannerApp)
        {
            _idleLogoutMonitor = new PlanerIdleLogoutMonitor();
            _idleLogoutMonitor.IdleTimeoutReached += OnIdleLogoutAsync;
            _idleLogoutMonitor.CountdownChanged += OnIdleCountdownChanged;
            _lifecycleMonitor = new PlanerSystemLifecycleMonitor();
            _lifecycleMonitor.Start();
        }
    }

    /// <summary>
    /// Ruhezustand / Standby: synchron speichern, Dropbox-Sperre freigeben, Anwendung beenden.
    /// </summary>
    internal void RequestAutoCloseFromSystemSuspend()
    {
        if (_closeConfirmed || _closeHandlerRunning)
        {
            return;
        }

        _idleLogoutMonitor?.Stop();
        _lifecycleMonitor?.Stop();

        if (AppServices.IsPlannerApp &&
            AppServices.PlanerSession?.NeedsExitHandling() == true)
        {
            SmartOepnvAppHost.SkipShutdownSave = false;
            SmartOepnvAppHost.EnsurePlanerShutdownSaveAndRelease();
        }

        _closeConfirmed = true;
        Application.Current.Shutdown();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (LoginGateActive)
        {
            return;
        }

        if (AppServices.IsPlannerApp && AppServices.PlanerSession?.IsLoggedIn == true)
        {
            LogoutButton.Visibility = Visibility.Visible;
            RestartIdleLogoutMonitor();
        }

        if (!_softwareUpdateChecked && AppServices.IsInitialized && !AppServices.IsPlannerApp)
        {
            _softwareUpdateChecked = true;
            _ = SmartOepnvAppHost.CheckForSoftwareUpdateAsync(this);
        }

        if (AppServices.IsInitialized && !AppServices.IsPlannerApp && DataContext is MainViewModel leitstelleVm)
        {
            _ = leitstelleVm.InitializeLeitstelleAfterShowAsync();
        }
    }

    public void SetLoginOverlay(bool active)
    {
        LoginGateActive = active;
        Effect = active
            ? new BlurEffect { Radius = 12, RenderingBias = RenderingBias.Quality }
            : null;

        if (active)
        {
            LogoutButton.Visibility = Visibility.Collapsed;
            UpdateIdleCountdownDisplay(null);
            _idleLogoutMonitor?.Stop();
            return;
        }

        if (AppServices.IsPlannerApp && AppServices.PlanerSession?.IsLoggedIn == true)
        {
            LogoutButton.Visibility = Visibility.Visible;
            RestartIdleLogoutMonitor();
        }

        Activate();
        Focus();
    }

    private async void Logout_Click(object sender, RoutedEventArgs e) =>
        await PerformLogoutAsync(requireConfirmation: true).ConfigureAwait(true);

    private async Task OnIdleLogoutAsync()
    {
        if (LoginGateActive || AppServices.PlanerSession?.IsLoggedIn != true)
        {
            return;
        }

        await PerformLogoutAsync(requireConfirmation: false, idleTimeout: true).ConfigureAwait(true);
    }

    private async Task PerformLogoutAsync(bool requireConfirmation, bool idleTimeout = false)
    {
        if (requireConfirmation)
        {
            if (!SmartConfirmDialog.ShowConfirm(
                    this,
                    "Abmelden",
                    "Planer wirklich abmelden?\n\nDer Arbeitsstand wird gespeichert und die Dropbox-Sperre freigegeben."))
            {
                return;
            }
        }

        _idleLogoutMonitor?.Suspend();
        SetLoginOverlay(true);

        if (idleTimeout)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            Activate();
        }

        var savingDialog = new AppExitSavingDialog(
            idleTimeout
                ? "Inaktivität – Arbeitsstand wird gespeichert und Planer-Sperre freigegeben…"
                : "Planer-Arbeitsstand wird gespeichert und nach Dropbox hochgeladen (bei großen Datenmengen kann das einige Minuten dauern)…")
        {
            Owner = this
        };
        await ShowSavingDialogAsync(savingDialog).ConfigureAwait(true);

        var saveSucceeded = false;
        try
        {
            var progress = new Progress<DropboxTransferProgress>(p => savingDialog.UpdateTransferProgress(p));
            await SavePlanerWorkspaceAsync("planer-logout", transferProgress: progress).ConfigureAwait(true);
            await SmartOepnvAppHost.ReleasePlanerSessionAsync().ConfigureAwait(true);
            saveSucceeded = true;
        }
        catch (Exception ex)
        {
            SetLoginOverlay(false);
            _idleLogoutMonitor?.Resume();
            RestartIdleLogoutMonitor();
            SmartConfirmDialog.ShowInfo(
                this,
                "Speichern fehlgeschlagen",
                $"Der Arbeitsstand konnte nicht vollständig nach Dropbox gespeichert werden.\n\n{ex.Message}\n\n" +
                "Sie bleiben angemeldet. Bitte Internetverbindung prüfen und erneut „Abmelden“ wählen.");
            return;
        }
        finally
        {
            savingDialog.PrepareToClose();
            savingDialog.Close();
            IsEnabled = true;
            _idleLogoutMonitor?.Resume();
        }

        if (idleTimeout && saveSucceeded)
        {
            SmartConfirmDialog.ShowInfo(
                this,
                "Automatisch abgemeldet",
                "Sie wurden wegen 10 Minuten Inaktivität automatisch abgemeldet.\n\nDer Arbeitsstand wurde gespeichert.");
        }

        var gate = await SmartOepnvAppHost.RunPlanerLoginGateAsync(this).ConfigureAwait(true);
        if (!gate.Success)
        {
            SetLoginOverlay(false);
            Close();
            return;
        }

        BeginPostLoginInitialization(gate);
    }

    private void RestartIdleLogoutMonitor()
    {
        if (!AppServices.IsPlannerApp || LoginGateActive || AppServices.PlanerSession?.IsLoggedIn != true)
        {
            UpdateIdleCountdownDisplay(null);
            return;
        }

        _lifecycleMonitor?.Start();
        _idleLogoutMonitor?.Restart();
    }

    private void StopIdleLogoutMonitor()
    {
        _idleLogoutMonitor?.Stop();
        _lifecycleMonitor?.Stop();
        UpdateIdleCountdownDisplay(null);
    }

    private void OnIdleCountdownChanged(TimeSpan? remaining) => UpdateIdleCountdownDisplay(remaining);

    private void UpdateIdleCountdownDisplay(TimeSpan? remaining)
    {
        if (remaining is null || LoginGateActive || AppServices.PlanerSession?.IsLoggedIn != true)
        {
            IdleLogoutCountdown.Visibility = Visibility.Collapsed;
            return;
        }

        var totalSeconds = (int)Math.Ceiling(remaining.Value.TotalSeconds);
        if (totalSeconds < 0)
        {
            totalSeconds = 0;
        }

        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        var logoutAt = DateTime.Now.AddSeconds(totalSeconds);
        IdleLogoutCountdown.Text = $"Abmeldung um {logoutAt:HH:mm} ({minutes:D2}:{seconds:D2})";
        IdleLogoutCountdown.Visibility = Visibility.Visible;
        IdleLogoutCountdown.Foreground = totalSeconds switch
        {
            <= 60 => IdleCountdownCriticalBrush,
            <= 120 => IdleCountdownWarningBrush,
            _ => IdleCountdownNormalBrush
        };
    }

    private static System.Windows.Media.SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public void BeginPostLoginInitialization(SmartOepnvAppHost.PlanerLoginGateResult gate)
    {
        _ = RunPostLoginInitializationAsync(gate);
    }

    private async Task RunPostLoginInitializationAsync(SmartOepnvAppHost.PlanerLoginGateResult gate)
    {
        if (DataContext is not MainViewModel mainViewModel)
        {
            return;
        }

        PlanerSyncDialog? syncDialog = null;
        var idleMonitorSuspended = false;
        if (AppServices.IsPlannerApp)
        {
            _idleLogoutMonitor?.Suspend();
            idleMonitorSuspended = true;
            syncDialog = new PlanerSyncDialog
            {
                Owner = this
            };
            WindowTitleBarHelper.ShowWhenContentReady(syncDialog);
            syncDialog.ShowLoginPhase();
            IsEnabled = false;
            await WindowTitleBarHelper.WaitForInitialRenderAsync(syncDialog).ConfigureAwait(true);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(gate.Username))
            {
                var session = AppServices.PlanerSession;
                if (session is null)
                {
                    throw new InvalidOperationException("Anmeldung im Planer nicht verfügbar.");
                }

                syncDialog?.ShowLoginPhase("Anmeldung bei Dropbox…");
                var loginResult = await session.TryLoginAsync(gate.Username, gate.Password ?? string.Empty)
                    .ConfigureAwait(true);
                if (!loginResult.Success)
                {
                    await HandleFailedPostLoginAsync(syncDialog, loginResult.Message).ConfigureAwait(true);
                    syncDialog = null;
                    return;
                }
            }

            SetLoginOverlay(false);
            syncDialog?.ShowSyncPhase();
            var progress = new Progress<DropboxTransferProgress>(p => syncDialog?.UpdateTransferProgress(p));
            await mainViewModel.InitializeAfterLoginAsync(progress).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            await HandleFailedPostLoginAsync(syncDialog, ex.Message).ConfigureAwait(true);
            syncDialog = null;
            return;
        }
        finally
        {
            if (idleMonitorSuspended)
            {
                _idleLogoutMonitor?.Resume();
                if (AppServices.PlanerSession?.IsLoggedIn == true && !LoginGateActive)
                {
                    RestartIdleLogoutMonitor();
                }
            }

            if (syncDialog is not null)
            {
                syncDialog.PrepareToClose();
                syncDialog.Close();
                IsEnabled = true;
                Activate();
                Focus();
            }

            if (!_softwareUpdateChecked)
            {
                _softwareUpdateChecked = true;
                _ = SmartOepnvAppHost.CheckForSoftwareUpdateAsync(this);
            }
        }
    }

    private async Task HandleFailedPostLoginAsync(PlanerSyncDialog? syncDialog, string message)
    {
        if (syncDialog is not null)
        {
            syncDialog.PrepareToClose();
            syncDialog.Close();
        }

        IsEnabled = true;
        SetLoginOverlay(true);
        SmartConfirmDialog.ShowInfo(
            this,
            "Anmeldung fehlgeschlagen",
            message + "\n\nBitte erneut anmelden.");

        var retryGate = await SmartOepnvAppHost.RunPlanerLoginGateAsync(this).ConfigureAwait(true);
        if (!retryGate.Success)
        {
            Close();
            return;
        }

        await RunPostLoginInitializationAsync(retryGate).ConfigureAwait(true);
    }

    public async Task SyncDropboxAfterLoginAsync()
    {
        if (DataContext is MainViewModel mainViewModel)
        {
            await mainViewModel.SyncDropboxAfterLoginAsync().ConfigureAwait(true);
        }
    }

    private async Task SavePlanerWorkspaceAsync(
        string backupReason,
        bool bestEffortFlush = false,
        IProgress<DropboxTransferProgress>? transferProgress = null)
    {
        Exception? flushError = null;

        await Dispatcher.InvokeAsync(() =>
        {
            if (DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.CommitAllAreasBeforeExport();
            }
            else
            {
                try
                {
                    if (bestEffortFlush)
                    {
                        AppServices.FlushAllPendingEditsBestEffort();
                    }
                    else
                    {
                        AppServices.FlushAllPendingEdits();
                    }
                }
                catch (Exception ex)
                {
                    flushError = ex;
                }
            }
        }).Task.ConfigureAwait(true);

        if (flushError is not null && !bestEffortFlush)
        {
            throw flushError;
        }

        if (string.Equals(backupReason, "manual", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => SmartOepnvDataBackupService.BackupAllProfiles(backupReason)).ConfigureAwait(true);
        }

        await SmartOepnvAppHost.ExportPlanerWorkspaceForShutdownAsync(transferProgress).ConfigureAwait(true);
    }

    private async Task ShowSavingDialogAsync(AppExitSavingDialog savingDialog)
    {
        _idleLogoutMonitor?.Suspend();
        savingDialog.Owner = this;
        WindowTitleBarHelper.ShowWhenContentReady(savingDialog);
        IsEnabled = false;
        await WindowTitleBarHelper.WaitForInitialRenderAsync(savingDialog).ConfigureAwait(true);
        await savingDialog.Dispatcher.InvokeAsync(
            () => savingDialog.StartBusAnimation(),
            System.Windows.Threading.DispatcherPriority.Render).Task.ConfigureAwait(true);
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel vm && !AppServices.IsPlannerApp)
        {
            vm.ShutdownVoip();
        }

        if (_closeConfirmed || !AppServices.IsInitialized)
        {
            StopIdleLogoutMonitor();
            return;
        }

        // Leitstelle: kein Planer-Speicher-Dialog – direkt schließen.
        if (!AppServices.IsPlannerApp)
        {
            return;
        }

        var planerNeedsExit = AppServices.IsPlannerApp &&
                              AppServices.PlanerSession?.NeedsExitHandling() == true;

        // Planer: nur speichern/freigeben nach erfolgreicher Anmeldung – nicht bei „Abbrechen“ im Login.
        if (!planerNeedsExit)
        {
            return;
        }

        if (_closeHandlerRunning)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _closeHandlerRunning = true;

        var closeChoice = PlanerCloseChoiceDialog.Show(this);
        if (closeChoice == PlanerCloseChoice.Cancel)
        {
            _closeHandlerRunning = false;
            RestartIdleLogoutMonitor();
            return;
        }

        if (closeChoice == PlanerCloseChoice.SaveAndClose)
        {
            SmartOepnvAppHost.SkipShutdownSave = false;
            var savingDialog = new AppExitSavingDialog
            {
                Owner = this
            };
            await ShowSavingDialogAsync(savingDialog).ConfigureAwait(true);

            try
            {
                var progress = new Progress<DropboxTransferProgress>(p => savingDialog.UpdateTransferProgress(p));
                await SavePlanerWorkspaceAsync("app-exit", bestEffortFlush: false, transferProgress: progress)
                    .ConfigureAwait(true);

                try
                {
                    await SmartOepnvAppHost.ReleasePlanerSessionAsync().ConfigureAwait(true);
                }
                catch
                {
                    AppServices.PlanerSession?.ReleaseLockBestEffortSync();
                }
            }
            catch (Exception ex)
            {
                savingDialog.PrepareToClose();
                savingDialog.Close();
                IsEnabled = true;
                _idleLogoutMonitor?.Resume();
                RestartIdleLogoutMonitor();
                _closeHandlerRunning = false;
                SmartConfirmDialog.ShowInfo(
                    this,
                    "Speichern fehlgeschlagen",
                    $"Der Arbeitsstand konnte nicht nach Dropbox gespeichert werden.\n\n{ex.Message}\n\n" +
                    "Das Programm bleibt geöffnet. Bitte erneut schließen oder zuerst unter Versand manuell speichern.");
                return;
            }
            finally
            {
                if (!PlanerWorkspaceSaveCoordinator.WasDropboxExportedRecently())
                {
                    SmartOepnvAppHost.EnsurePlanerShutdownSaveAndRelease();
                }
                else if (AppServices.PlanerSession?.IsLoggedIn == true)
                {
                    AppServices.PlanerSession.ReleaseLockBestEffortSync();
                }

                savingDialog.PrepareToClose();
                savingDialog.Close();
                IsEnabled = true;
                _idleLogoutMonitor?.Resume();
            }
        }
        else
        {
            SmartOepnvAppHost.SkipShutdownSave = true;
            try
            {
                await SmartOepnvAppHost.ReleasePlanerSessionAsync().ConfigureAwait(true);
            }
            catch
            {
                AppServices.PlanerSession?.ReleaseLockBestEffortSync();
            }
        }

        _closeHandlerRunning = false;
        StopIdleLogoutMonitor();
        ConfirmAndClose();
    }

    /// <summary>Schließen außerhalb des Closing-Events auslösen (sonst zweites X nötig).</summary>
    private void ConfirmAndClose()
    {
        _closeConfirmed = true;
        Dispatcher.BeginInvoke(Close);
    }

    private void NavigationGroup_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NavigationGroup group })
        {
            group.SetHoverOpen(true);
        }
    }

    private void NavigationGroup_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NavigationGroup group })
        {
            group.SetHoverOpen(false);
        }
    }
}
