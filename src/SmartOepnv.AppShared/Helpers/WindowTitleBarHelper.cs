using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>Windows-Titelleiste an Smart-ÖPNV-Statusleiste (SmartPrimaryDark) anpassen.</summary>
public static class WindowTitleBarHelper
{
    public static bool IsLightTheme { get; set; }

    private static readonly SolidColorBrush DarkWindowBackground = CreateFrozenBrush(0x0A, 0x10, 0x20);
    private static readonly SolidColorBrush LightWindowBackground = CreateFrozenBrush(0xF5, 0xFA, 0xFF);

    /// <summary>SmartBackground – vor InitializeComponent setzen, kein Aufblitzer.</summary>
    public static void ApplyDarkWindowBackground(Window window)
    {
        window.Background = IsLightTheme ? LightWindowBackground : DarkWindowBackground;
    }

    /// <summary>Zeigt Fenster sichtbar und aktiviert es – Hintergrund verhindert den Aufblitzer.</summary>
    public static void ShowWhenContentReady(Window window)
    {
        ApplyDarkWindowBackground(window);
        window.Opacity = 1;
        window.Show();
        window.Activate();
        window.UpdateLayout();
    }

    /// <summary>Gibt dem Dialog einen Layout-/Render-Durchlauf, bevor lange Arbeit beginnt.</summary>
    public static Task WaitForInitialRenderAsync(Window window) =>
        window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render).Task;

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    // SmartPrimaryDarkColor #FF002171 → COLORREF 0x00BBGGRR
    private const int SmartPrimaryDarkColorRef = 0x00712100;
    // Hell: hellblau Caption #BBDEFB → 0x00FBDEBB
    private const int SmartLightCaptionColorRef = 0x00FBDEBB;
    // Dunkelblau Text #002171
    private const int SmartDarkTextColorRef = 0x00712100;

    public static void ApplySmartOepnvTitleBar(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == nint.Zero)
        {
            return;
        }

        var darkMode = IsLightTheme ? 0 : 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref darkMode, sizeof(int));

        var captionColor = IsLightTheme ? SmartLightCaptionColorRef : SmartPrimaryDarkColorRef;
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColor, sizeof(int));

        var textColor = IsLightTheme ? SmartDarkTextColorRef : 0x00FFFFFF;
        _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);
}
