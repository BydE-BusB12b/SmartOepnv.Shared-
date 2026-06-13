using System.Windows;
using System.Windows.Media;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Session;

namespace SmartOepnv.AppShared;

public static class SmartOepnvAppHost
{
    private static bool _shutdownHandlersRegistered;

    public static SmartOepnvAppProfile Profile { get; private set; } = SmartOepnvAppProfile.Planer;

    public static void Initialize(SmartOepnvAppProfile profile)
    {
        Profile = profile;
        var settingsFolder = profile.IsLeitstelle ? "Leitstelle" : "Planer";
        AppServices.Initialize(settingsFolder);
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

    /// <summary>Planer: Sperre prüfen, Login-Dialog über verschwommenem Hauptfenster.</summary>
    public static async Task<bool> RunPlanerLoginGateAsync(MainShellWindow owner)
    {
        if (Profile.IsLeitstelle || AppServices.PlanerSession is null)
        {
            return true;
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
        return login.ShowDialog() == true;
    }

    public static async Task ReleasePlanerSessionAsync()
    {
        if (AppServices.PlanerSession is null)
        {
            return;
        }

        await AppServices.PlanerSession.ReleaseLockAsync().ConfigureAwait(false);
    }

    /// <summary>Beim Beenden synchron speichern und Dropbox-Sperre freigeben (blockiert bis Upload fertig).</summary>
    public static void EnsurePlanerShutdownSaveAndRelease()
    {
        if (!AppServices.IsPlannerApp || !AppServices.IsInitialized)
        {
            return;
        }

        if (AppServices.PlanerSession is null ||
            AppServices.PlanerSession.NeedsExitHandling() != true)
        {
            return;
        }

        try
        {
            AppServices.FlushAllPendingEditsBestEffort();
            SmartOepnvDataBackupService.BackupAllProfiles("app-exit-sync");
        }
        catch
        {
            // trotzdem exportieren/freigeben
        }

        try
        {
            PlanerDropboxWorkspaceSync.TryExportAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // trotzdem Sperre freigeben
        }

        AppServices.PlanerSession.ReleaseLockBestEffortSync();
    }

    public static void SaveAndReleasePlanerSessionSync() => EnsurePlanerShutdownSaveAndRelease();

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * amount),
            (byte)(a.G + (b.G - a.G) * amount),
            (byte)(a.B + (b.B - a.B) * amount));
    }
}
