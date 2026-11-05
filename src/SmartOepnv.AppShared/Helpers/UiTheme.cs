using System.Windows;
using System.Windows.Media;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>Hell/Dunkel für Planer und Leitstelle (gemeinsame AppShared-UI).</summary>
public static class UiTheme
{
    public static bool IsLight { get; private set; }

    public static bool LoadPreference()
    {
        try
        {
            if (AppServices.IsInitialized && AppServices.PlanerAppSettings is not null)
            {
                return AppServices.PlanerAppSettings.Load().UseLightTheme;
            }
        }
        catch
        {
            // fallback dark
        }

        return false;
    }

    public static void SavePreference(bool useLight)
    {
        if (!AppServices.IsInitialized || AppServices.PlanerAppSettings is null)
        {
            return;
        }

        var settings = AppServices.PlanerAppSettings.Load();
        settings.UseLightTheme = useLight;
        AppServices.PlanerAppSettings.Save(settings);
    }

    public static void Apply(
        Application application,
        SmartOepnvAppProfile profile,
        bool useLight)
    {
        IsLight = useLight;
        WindowTitleBarHelper.IsLightTheme = useLight;

        var primary = (Color)ColorConverter.ConvertFromString(profile.PrimaryColorHex)!;
        var primaryDark = (Color)ColorConverter.ConvertFromString(profile.PrimaryDarkColorHex)!;
        var accent = (Color)ColorConverter.ConvertFromString(profile.AccentColorHex)!;
        var primaryLight = Blend(primary, Colors.White, 0.35);

        // Hell: Flächen hellblau → sehr hellblau; Schrift dunkelblau
        Color background;
        Color surface;
        Color foreground;
        Color panel;
        Color windowSys;
        Color controlSys;
        if (useLight)
        {
            background = (Color)ColorConverter.ConvertFromString("#FFF5FAFF")!; // sehr hellblau
            surface = (Color)ColorConverter.ConvertFromString("#FFD6EAF8")!;    // hellblau
            panel = surface;
            foreground = primaryDark; // #002171
            windowSys = (Color)ColorConverter.ConvertFromString("#FFE3F2FD")!;
            controlSys = (Color)ColorConverter.ConvertFromString("#FFBBDEFB")!;
            accent = Blend(primary, Colors.White, 0.15);
            primaryLight = Blend(primary, Colors.White, 0.45);
        }
        else
        {
            background = (Color)ColorConverter.ConvertFromString("#FF0A1020")!;
            surface = (Color)ColorConverter.ConvertFromString("#FF121A2E")!;
            panel = primaryDark;
            foreground = Colors.White;
            windowSys = primaryDark;
            controlSys = (Color)ColorConverter.ConvertFromString("#FF1E5A9E")!;
        }

        var resources = new ResourceDictionary();
        resources.MergedDictionaries.Add(new BundledTheme
        {
            BaseTheme = useLight ? BaseTheme.Light : BaseTheme.Dark,
            PrimaryColor = PrimaryColor.Blue,
            SecondaryColor = SecondaryColor.LightBlue
        });
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml")
        });
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/SmartOepnv.AppShared;component/Themes/SmartOepnvTheme.xaml")
        });

        resources["SmartPrimaryColor"] = primary;
        resources["SmartPrimaryDarkColor"] = primaryDark;
        resources["SmartPrimaryLightColor"] = primaryLight;
        resources["SmartAccentColor"] = accent;
        resources["SmartSurfaceColor"] = surface;
        resources["SmartBackgroundColor"] = background;
        resources["SmartForegroundColor"] = foreground;
        resources["SmartPanelColor"] = panel;

        resources["SmartPrimaryBrush"] = Brush(primary);
        resources["SmartPrimaryDarkBrush"] = Brush(primaryDark);
        resources["SmartPrimaryLightBrush"] = Brush(primaryLight);
        resources["SmartAccentBrush"] = Brush(accent);
        resources["SmartSurfaceBrush"] = Brush(surface);
        resources["SmartBackgroundBrush"] = Brush(background);
        resources["SmartForegroundBrush"] = Brush(foreground);
        resources["SmartPanelBrush"] = Brush(panel);
        resources["SmartOnPrimaryBrush"] = Brush(Colors.White);

        resources["MaterialDesign.Brush.Primary"] = Brush(primary);
        resources["MaterialDesign.Brush.Primary.Dark"] = Brush(primaryDark);
        resources["MaterialDesign.Brush.Primary.Light"] = Brush(primaryLight);
        resources["MaterialDesign.Brush.Secondary"] = Brush(accent);

        resources[SystemColors.WindowBrushKey] = Brush(windowSys);
        resources[SystemColors.WindowTextBrushKey] = Brush(foreground);
        resources[SystemColors.HighlightBrushKey] = Brush(primary);
        resources[SystemColors.HighlightTextBrushKey] = Brush(useLight ? Colors.White : Colors.White);
        resources[SystemColors.ControlBrushKey] = Brush(controlSys);
        resources[SystemColors.ControlTextBrushKey] = Brush(foreground);

        application.Resources = resources;
    }

    /// <summary>Schrift auf Flächen. Im Hellmodus dunkelblau, im Dunkelmodus weiß.</summary>
    public static Brush ForegroundBrush => FindBrush("SmartForegroundBrush");

    /// <summary>Fenster- und Dialogfläche.</summary>
    public static Brush BackgroundBrush => FindBrush("SmartBackgroundBrush");

    /// <summary>Karten und Innenflächen.</summary>
    public static Brush SurfaceBrush => FindBrush("SmartSurfaceBrush");

    /// <summary>Schrift auf dunklen Schaltflächen.</summary>
    public static Brush OnPrimaryBrush => FindBrush("SmartOnPrimaryBrush");

    private static Brush FindBrush(string key)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return key == "SmartOnPrimaryBrush" || !IsLight
            ? Brushes.White
            : Brush(Color.FromRgb(0x00, 0x21, 0x71));
    }

    private static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
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
