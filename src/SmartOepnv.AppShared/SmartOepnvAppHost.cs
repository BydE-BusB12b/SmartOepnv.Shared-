using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
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
        }
        RegisterShutdownHandlersIfNeeded();
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
        var primary = (Color)ColorConverter.ConvertFromString(profile.PrimaryColorHex)!;
        var primaryDark = (Color)ColorConverter.ConvertFromString(profile.PrimaryDarkColorHex)!;
        var accent = (Color)ColorConverter.ConvertFromString(profile.AccentColorHex)!;
        var primaryLight = Blend(primary, Colors.White, 0.35);

        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new BundledTheme
        {
            BaseTheme = BaseTheme.Dark,
            PrimaryColor = PrimaryColor.Blue,
            SecondaryColor = SecondaryColor.LightBlue
        });
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml")
        });
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SmartOepnv.AppShared;component/Themes/SmartOepnvTheme.xaml")
        });

        resources["SmartPrimaryColor"] = primary;
        resources["SmartPrimaryDarkColor"] = primaryDark;
        resources["SmartPrimaryLightColor"] = primaryLight;
        resources["SmartAccentColor"] = accent;
        resources["MaterialDesign.Brush.Primary"] = new SolidColorBrush(primary);
        resources["MaterialDesign.Brush.Primary.Dark"] = new SolidColorBrush(primaryDark);
        resources["MaterialDesign.Brush.Primary.Light"] = new SolidColorBrush(primaryLight);
        resources["MaterialDesign.Brush.Secondary"] = new SolidColorBrush(accent);

        application.Resources = resources;
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

    /// <summary>
    /// Dropbox-Export nach UI-Commit (ohne Dispatcher). Für Logout-Dialog und Hintergrund-Beenden.
    /// </summary>
    public static async Task ExportPlanerWorkspaceForShutdownAsync(
        IProgress<DropboxTransferProgress>? transferProgress = null)
    {
        PlanerDropboxWorkspaceSync.ExportResult? exportResult = null;
        await Task.Run(async () =>
        {
            const int maxExportAttempts = 3;
            for (var attempt = 1; attempt <= maxExportAttempts; attempt++)
            {
                exportResult = await PlanerDropboxWorkspaceSync.TryExportAsync(
                        flushBeforeCapture: false,
                        progress: transferProgress)
                    .ConfigureAwait(false);
                if (exportResult is { Exported: true })
                {
                    return;
                }

                if (exportResult?.Message.Contains("payload_too_large", StringComparison.OrdinalIgnoreCase) == true)
                {
                    break;
                }

                if (attempt < maxExportAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(8 * attempt)).ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);

        if (exportResult is { Exported: false })
        {
            var hint = exportResult.LocalSaved
                ? "\n\nDer Arbeitsstand liegt lokal vor – bitte Internetverbindung prüfen und erneut speichern."
                : string.Empty;
            throw new InvalidOperationException(exportResult.Message + hint);
        }
    }

    /// <summary>Beim Beenden synchron speichern und Dropbox-Sperre freigeben (blockiert bis Upload fertig).</summary>
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

        try
        {
            PlanerDropboxWorkspaceSync.TryExportAsync(flushBeforeCapture: true).GetAwaiter().GetResult();
        }
        catch
        {
            // trotzdem Sperre freigeben
        }

        AppServices.PlanerSession.ReleaseLockBestEffortSync();
    }

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

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * amount),
            (byte)(a.G + (b.G - a.G) * amount),
            (byte)(a.B + (b.B - a.B) * amount));
    }
}
