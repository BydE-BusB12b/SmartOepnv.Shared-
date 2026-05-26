using System.Windows;
using System.Windows.Media;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared;

public static class SmartOepnvAppHost
{
    public static SmartOepnvAppProfile Profile { get; private set; } = SmartOepnvAppProfile.Planer;

    public static void Initialize(SmartOepnvAppProfile profile)
    {
        Profile = profile;
        var settingsFolder = profile.IsLeitstelle ? "Leitstelle" : "Planer";
        AppServices.Initialize(settingsFolder);
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

    private static Color Blend(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * amount),
            (byte)(a.G + (b.G - a.G) * amount),
            (byte)(a.B + (b.B - a.B) * amount));
    }
}
