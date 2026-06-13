using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Effects;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Session;

namespace SmartOepnv.AppShared;

public partial class MainShellWindow : Window
{
    private bool _closeConfirmed;
    private bool _closeHandlerRunning;

    public bool LoginGateActive { get; private set; }

    public MainShellWindow()
    {
        InitializeComponent();
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
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
            return;
        }

        if (AppServices.IsPlannerApp && AppServices.PlanerSession?.IsLoggedIn == true)
        {
            LogoutButton.Visibility = Visibility.Visible;
        }

        Activate();
        Focus();
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Planer wirklich abmelden?\n\nDer Arbeitsstand wird gespeichert und die Dropbox-Sperre freigegeben.",
            Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        SetLoginOverlay(true);

        var savingDialog = new AppExitSavingDialog(
            "Planer-Arbeitsstand wird lokal gespeichert und als planer_workspace.json nach Dropbox hochgeladen…")
        {
            Owner = this
        };
        WindowTitleBarHelper.ShowWhenContentReady(savingDialog);
        IsEnabled = false;

        try
        {
            await SavePlanerWorkspaceAsync("planer-logout").ConfigureAwait(true);
            await SmartOepnvAppHost.ReleasePlanerSessionAsync().ConfigureAwait(true);
        }
        catch
        {
            // Abmeldung trotzdem fortsetzen
        }
        finally
        {
            savingDialog.PrepareToClose();
            savingDialog.Close();
            IsEnabled = true;
        }

        if (!await SmartOepnvAppHost.RunPlanerLoginGateAsync(this))
        {
            SetLoginOverlay(false);
            Close();
            return;
        }

        SetLoginOverlay(false);
        BeginPostLoginInitialization();
    }

    public void BeginPostLoginInitialization()
    {
        _ = RunPostLoginInitializationAsync();
    }

    private async Task RunPostLoginInitializationAsync()
    {
        if (DataContext is not MainViewModel mainViewModel)
        {
            return;
        }

        PlanerSyncDialog? syncDialog = null;
        if (AppServices.IsPlannerApp)
        {
            syncDialog = new PlanerSyncDialog
            {
                Owner = this
            };
            WindowTitleBarHelper.ShowWhenContentReady(syncDialog);
            IsEnabled = false;
        }

        try
        {
            await mainViewModel.InitializeAfterLoginAsync().ConfigureAwait(true);
        }
        finally
        {
            if (syncDialog is not null)
            {
                syncDialog.PrepareToClose();
                syncDialog.Close();
                IsEnabled = true;
                Activate();
                Focus();
            }
        }
    }

    public async Task SyncDropboxAfterLoginAsync()
    {
        if (DataContext is MainViewModel mainViewModel)
        {
            await mainViewModel.SyncDropboxAfterLoginAsync().ConfigureAwait(true);
        }
    }

    private static async Task SavePlanerWorkspaceAsync(string backupReason, bool bestEffortFlush = false)
    {
        await Task.Run(() =>
        {
            if (bestEffortFlush)
            {
                AppServices.FlushAllPendingEditsBestEffort();
            }
            else
            {
                AppServices.FlushAllPendingEdits();
            }

            SmartOepnvDataBackupService.BackupAllProfiles(backupReason);
        }).ConfigureAwait(true);

        await ExportPlanerToDropboxAsync().ConfigureAwait(true);
    }

    private static async Task ExportPlanerToDropboxAsync()
    {
        if (!AppServices.IsPlannerApp)
        {
            return;
        }

        await PlanerDropboxWorkspaceSync.TryExportAsync().ConfigureAwait(true);
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || !AppServices.IsInitialized)
        {
            return;
        }

        var planerNeedsExit = AppServices.IsPlannerApp &&
                              AppServices.PlanerSession?.NeedsExitHandling() == true;

        // Planer: nur speichern/freigeben nach erfolgreicher Anmeldung – nicht bei „Abbrechen“ im Login.
        if (AppServices.IsPlannerApp && !planerNeedsExit)
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

        var savingDialog = new AppExitSavingDialog
        {
            Owner = this
        };
        WindowTitleBarHelper.ShowWhenContentReady(savingDialog);
        IsEnabled = false;

        try
        {
            try
            {
                await SavePlanerWorkspaceAsync("app-exit", bestEffortFlush: true).ConfigureAwait(true);
            }
            catch
            {
                // Arbeitsstand optional – Sperre hat Vorrang
            }

            try
            {
                await SmartOepnvAppHost.ReleasePlanerSessionAsync().ConfigureAwait(true);
            }
            catch
            {
                // synchroner Fallback unten
            }
        }
        finally
        {
            SmartOepnvAppHost.EnsurePlanerShutdownSaveAndRelease();
            savingDialog.PrepareToClose();
            savingDialog.Close();
            IsEnabled = true;
            _closeHandlerRunning = false;
        }

        _closeConfirmed = true;
        Close();
    }
}
