using System.Diagnostics;
using System.IO;
using System.Windows;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.Betrieb;
using SmartOepnv.Core.Dropbox;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Session;
using SmartOepnv.Core.Updates;

namespace SmartOepnv.AppShared;

public static class SmartOepnvAppHost
{
    private static bool _shutdownHandlersRegistered;

    /// <summary>Beim Schließen ohne Speichern – verhindert den automatischen Exit-Export.</summary>
    public static bool SkipShutdownSave { get; set; }

    public static SmartOepnvAppProfile Profile { get; private set; } = SmartOepnvAppProfile.Planer;

    public static void Initialize(SmartOepnvAppProfile profile)
    {
        Profile = profile;
        var settingsFolder = profile.IsLeitstelle ? "Leitstelle" : "Planer";
        AppServices.Initialize(settingsFolder);
        if (!profile.IsLeitstelle)
        {
            PlanerSyncBusAnimation.PreloadBusImage();
            SyncActiveBetriebFolderPathFromDropboxSettings();
        }
        RegisterShutdownHandlersIfNeeded();
    }

    /// <summary>Profil-Metadaten an aktuellen Dropbox-Pfad anbinden (falls manuell geändert).</summary>
    private static void SyncActiveBetriebFolderPathFromDropboxSettings()
    {
        try
        {
            var active = BetriebProfileStore.GetActiveProfile();
            if (active is null)
            {
                return;
            }

            var folder = DropboxConstants.NormalizeFolderPath(AppServices.Dropbox.Settings.FolderPath);
            if (string.Equals(active.DropboxFolderPath, folder, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(active.DisplayName))
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(active.DisplayName)
                ? BetriebProfileStore.DeriveDisplayName(folder)
                : active.DisplayName;
            BetriebProfileStore.UpdateProfileMeta(active.Id, name, folder);
        }
        catch
        {
            // optional
        }
    }

    /// <summary>
    /// Speichert aktuellen Betrieb (schnell), meldet ab, wechselt Profil und startet neu.
    /// </summary>
    public static async Task SwitchBetriebAndRestartAsync(
        Window owner,
        string? switchToExistingId,
        string? newDisplayName,
        string? newDropboxFolderPath,
        IProgress<DropboxTransferProgress>? progress = null,
        Action<string>? statusMessage = null)
    {
        if (!AppServices.IsPlannerApp)
        {
            return;
        }

        var leavingName = BetriebProfileStore.GetActiveProfile()?.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(leavingName))
        {
            leavingName = "aktueller Betrieb";
        }

        void Status(string text) => statusMessage?.Invoke(text);

        Status($"Betrieb „{leavingName}“ wird abgemeldet…");
        ReportOverall(progress, $"Betrieb „{leavingName}“ wird abgemeldet…", 2);

        try
        {
            AppServices.FlushAllPendingEditsBestEffort();
        }
        catch
        {
            // weiter
        }

        // Dropbox-Pfad im Profil festhalten, bevor wir abmelden/wechseln
        try
        {
            var active = BetriebProfileStore.GetActiveProfile();
            if (active is not null)
            {
                BetriebProfileStore.UpdateProfileMeta(
                    active.Id,
                    active.DisplayName,
                    AppServices.Dropbox.Settings.FolderPath);
            }
        }
        catch
        {
            // optional
        }

        // Sperre sofort freigeben – sichtbar als Abmeldung, bevor der Upload fertig ist
        Status($"Betrieb „{leavingName}“: Sperre wird freigegeben…");
        ReportOverall(progress, $"Betrieb „{leavingName}“: Sperre wird freigegeben…", 8);
        try
        {
            await ReleasePlanerSessionAsync().ConfigureAwait(true);
            Status($"Betrieb „{leavingName}“ abgemeldet – Arbeitsstand wird gesichert…");
            ReportOverall(progress, $"Betrieb „{leavingName}“ abgemeldet – Arbeitsstand wird gesichert…", 12);
        }
        catch
        {
            // trotzdem weiterwechseln
        }

        if (AppServices.Dropbox.Settings.IsConnected)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await PlanerDropboxWorkspaceSync.TryExportAsync(
                        flushBeforeCapture: false,
                        progress: progress,
                        ct: cts.Token,
                        skipAnnouncementSounds: true,
                        skipVersionSnapshots: true)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                Status("Upload-Zeitlimit – Wechsel geht trotzdem weiter (lokal gespeichert).");
            }
            catch
            {
                // lokaler Stand bleibt erhalten
            }
        }

        Status("Neuer Betrieb wird geladen…");
        ReportOverall(progress, "Neuer Betrieb wird geladen…", 95);

        if (!string.IsNullOrWhiteSpace(switchToExistingId))
        {
            BetriebProfileStore.SwitchTo(switchToExistingId);
        }
        else if (!string.IsNullOrWhiteSpace(newDisplayName) && !string.IsNullOrWhiteSpace(newDropboxFolderPath))
        {
            BetriebProfileStore.CreateAndActivate(
                newDisplayName,
                newDropboxFolderPath,
                AppServices.Dropbox.Settings);
        }
        else
        {
            throw new InvalidOperationException("Kein Betrieb gewählt.");
        }

        SkipShutdownSave = true;
        ReportOverall(progress, "Planer wird neu gestartet…", 100);
        RestartCurrentProcess(owner);
    }

    private static void ReportOverall(IProgress<DropboxTransferProgress>? progress, string phase, double percent) =>
        progress?.Report(new DropboxTransferProgress
        {
            Phase = phase,
            BytesTransferred = (long)Math.Round(percent),
            TotalBytes = 100
        });

    private static void RestartCurrentProcess(Window? owner)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            try
            {
                exe = Process.GetCurrentProcess().MainModule?.FileName;
            }
            catch
            {
                exe = null;
            }
        }

        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            MessageBox.Show(
                owner,
                "Betrieb wurde umgestellt. Bitte den Planer manuell neu starten.",
                "Betrieb wechseln",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Application.Current?.Shutdown();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                owner,
                $"Neustart fehlgeschlagen ({ex.Message}). Bitte den Planer manuell neu starten.",
                "Betrieb wechseln",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        Application.Current?.Shutdown();
    }

    private static void RegisterShutdownHandlersIfNeeded()
    {
        if (_shutdownHandlersRegistered || Profile.IsLeitstelle)
        {
            return;
        }

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        app.Exit += (_, _) => EnsurePlanerShutdownSaveAndRelease();
        app.SessionEnding += (_, _) => EnsurePlanerShutdownSaveAndRelease();
        _shutdownHandlersRegistered = true;
    }

    public static void ApplyApplicationResources(Application application, SmartOepnvAppProfile profile)
    {
        Helpers.UiTheme.Apply(application, profile, Helpers.UiTheme.LoadPreference());
    }

    public static MainShellWindow CreateMainWindow()
    {
        RegisterShutdownHandlersIfNeeded();
        var window = new MainShellWindow();
        window.DataContext = new ViewModels.MainViewModel(Profile);
        window.Title = Profile.ProductName;
        try
        {
            window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        }
        catch
        {
            // Icon optional – Host-Projekt muss Assets\app.ico enthalten
        }

        return window;
    }

    /// <summary>Ergebnis des Anmeldedialogs – Dropbox-Anmeldung folgt im Synchronisationsdialog.</summary>
    public readonly record struct PlanerLoginGateResult(bool Success, string? Username, string? Password);

    /// <summary>Planer: Sperre prüfen, Login-Dialog über verschwommenem Hauptfenster.</summary>
    public static async Task<PlanerLoginGateResult> RunPlanerLoginGateAsync(MainShellWindow owner)
    {
        if (Profile.IsLeitstelle || AppServices.PlanerSession is null)
        {
            return new PlanerLoginGateResult(true, null, null);
        }

        var session = AppServices.PlanerSession;
        await session.TryReleasePendingLocalSessionAsync().ConfigureAwait(true);

        var (availability, activeUser) = await session.InspectLockAsync().ConfigureAwait(true);

        string? lockWarning = null;
        if (availability == PlanerSessionAvailability.InUseByOther)
        {
            lockWarning = "Planer gesperrt – anderer Nutzer ist angemeldet" +
                          (string.IsNullOrWhiteSpace(activeUser) ? "." : $": {activeUser}.") +
                          " Bitte dort abmelden oder Sperre freigeben.";
        }

        var login = new LoginWindow(lockWarning)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        var ok = login.ShowDialog() == true;
        return new PlanerLoginGateResult(ok, login.PendingUsername, login.PendingPassword);
    }

    public static async Task ReleasePlanerSessionAsync()
    {
        if (AppServices.PlanerSession is null)
        {
            return;
        }

        await AppServices.PlanerSession.ReleaseLockAsync().ConfigureAwait(false);
    }

    /// <summary>Obergrenze für den stillen Export, wenn das Fenster schon zu ist.</summary>
    private static readonly TimeSpan ShutdownNetworkBudget = TimeSpan.FromSeconds(20);

    /// <summary>Speichern mit Dialog: der Upload darf mehrere Minuten dauern.</summary>
    private static readonly TimeSpan ExplicitExportOverall = TimeSpan.FromMinutes(8);

    /// <summary>Ohne Fortschritt (kein Netz) nicht ewig warten.</summary>
    private static readonly TimeSpan ExplicitExportStall = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Dropbox-Export nach UI-Commit. Läuft weiter, solange Bytes ankommen.
    /// Kommt 90 Sekunden nichts, wird abgebrochen und das Fenster ist wieder bedienbar.
    /// </summary>
    public static async Task ExportPlanerWorkspaceForShutdownAsync(
        IProgress<DropboxTransferProgress>? transferProgress = null)
    {
        using var timeout = new CancellationTokenSource();
        var started = Environment.TickCount64;
        var lastProgress = started;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var watch = Task.Run(async () =>
        {
            while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                var now = Environment.TickCount64;
                var idle = now - Volatile.Read(ref lastProgress);
                if (now - started >= (long)ExplicitExportOverall.TotalMilliseconds ||
                    idle >= (long)ExplicitExportStall.TotalMilliseconds)
                {
                    timeout.Cancel();
                    return;
                }
            }
        });

        var progress = new Progress<DropboxTransferProgress>(update =>
        {
            Volatile.Write(ref lastProgress, Environment.TickCount64);
            transferProgress?.Report(update);
        });

        try
        {
            var exportResult = await PlanerDropboxWorkspaceSync.TryExportAsync(
                    timeout.Token,
                    flushBeforeCapture: false,
                    progress: progress)
                .ConfigureAwait(false);

            if (exportResult is { Exported: false })
            {
                var hint = exportResult.LocalSaved
                    ? "\n\nDer Arbeitsstand liegt auf diesem PC. Das Programm bleibt benutzbar."
                    : string.Empty;
                throw new InvalidOperationException(exportResult.Message + hint);
            }
        }
        finally
        {
            timer.Dispose();
            try
            {
                await watch.ConfigureAwait(false);
            }
            catch
            {
                // Der Wächter endet mit dem Timer.
            }
        }
    }

    /// <summary>Beim Beenden lokal sichern, Dropbox nur kurz versuchen, Sperre freigeben. Blockiert höchstens wenige Sekunden.</summary>
    public static void EnsurePlanerShutdownSaveAndRelease()
    {
        if (!AppServices.IsPlannerApp || !AppServices.IsInitialized || SkipShutdownSave)
        {
            return;
        }

        if (AppServices.PlanerSession is null ||
            AppServices.PlanerSession.NeedsExitHandling() != true)
        {
            return;
        }

        if (PlanerWorkspaceSaveCoordinator.WasDropboxExportedRecently())
        {
            AppServices.PlanerSession.ReleaseLockBestEffortSync();
            return;
        }

        try
        {
            AppServices.FlushAllPendingEditsBestEffort();
        }
        catch
        {
            // trotzdem exportieren/freigeben
        }

        using var exportTimeout = new CancellationTokenSource(ShutdownNetworkBudget);
        var export = PlanerDropboxWorkspaceSync.TryExportAsync(exportTimeout.Token, flushBeforeCapture: true);
        ObserveFault(export);
        WaitBounded(export, exportTimeout.Token);

        AppServices.PlanerSession.ReleaseLockBestEffortSync();
    }

    private static void WaitBounded(Task task, CancellationToken timeout)
    {
        try
        {
            task.Wait(timeout);
        }
        catch
        {
            // Zeit abgelaufen oder kein Netz: Aufrufer läuft weiter.
        }
    }

    private static void ObserveFault(Task task) =>
        task.ContinueWith(
            static finished => { _ = finished.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    public static void SaveAndReleasePlanerSessionSync() => EnsurePlanerShutdownSaveAndRelease();

    /// <summary>Dropbox <c>software_versions.json</c> prüfen; bei OK Installer aus Dropbox laden.</summary>
    public static async Task CheckForSoftwareUpdateAsync(Window? owner)
    {
        if (!AppServices.IsInitialized)
        {
            return;
        }

        SoftwareUpdateNotice? notice;
        try
        {
            notice = await DesktopSoftwareUpdateChecker.CheckAsync().ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (notice is null)
        {
            return;
        }

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var confirmed = false;
        await app.Dispatcher.InvokeAsync(() =>
        {
            var text =
                $"Version {notice.AvailableVersion} verfügbar.\n\n" +
                $"Installiert: {notice.InstalledVersion}\n\n" +
                "Mit OK wird der Installer aus Dropbox in den Download-Ordner geladen.";
            if (!string.IsNullOrWhiteSpace(notice.Message))
            {
                text += $"\n\n{notice.Message}";
            }

            confirmed = MessageBox.Show(
                owner,
                text,
                $"{Profile.ProductName} – Update verfügbar",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information) == MessageBoxResult.OK;
        });

        if (!confirmed)
        {
            return;
        }

        AppExitSavingDialog? savingDialog = null;
        await app.Dispatcher.InvokeAsync(() =>
        {
            savingDialog = new AppExitSavingDialog(
                $"Installer Version {notice.AvailableVersion} wird aus Dropbox heruntergeladen…")
            {
                Owner = owner,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner
            };
            savingDialog.Show();
        });

        try
        {
            var targetPath = await DesktopSoftwareUpdateDownloader.DownloadAsync(notice).ConfigureAwait(true);
            DesktopSoftwareUpdateChecker.MarkNoticeAcknowledged(notice);

            await app.Dispatcher.InvokeAsync(() =>
            {
                var openFolder = MessageBox.Show(
                    owner,
                    $"Setup wurde gespeichert:\n{targetPath}\n\nOrdner öffnen?",
                    $"{Profile.ProductName} – Download abgeschlossen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information) == MessageBoxResult.Yes;
                if (openFolder)
                {
                    OpenDownloadedFileInExplorer(targetPath);
                }
            });
        }
        catch (Exception ex)
        {
            await app.Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(
                    owner,
                    $"Download fehlgeschlagen:\n{ex.Message}",
                    $"{Profile.ProductName} – Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }
        finally
        {
            if (savingDialog is not null)
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    savingDialog.PrepareToClose();
                    savingDialog.Close();
                });
            }
        }
    }

    private static void OpenDownloadedFileInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            // optional
        }
    }

}
