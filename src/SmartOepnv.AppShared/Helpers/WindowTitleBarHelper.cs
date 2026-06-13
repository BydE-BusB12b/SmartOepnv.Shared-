using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>Windows-Titelleiste an Smart-ÖPNV-Statusleiste (SmartPrimaryDark) anpassen.</summary>
public static class WindowTitleBarHelper
{
    private static readonly SolidColorBrush DarkWindowBackground = CreateFrozenBrush(0x0A, 0x10, 0x20);

    /// <summary>SmartBackgroundColor (#0A1020) – vor InitializeComponent setzen, kein weißer Aufblitzer.</summary>
    public static void ApplyDarkWindowBackground(Window window)
    {
        window.Background = DarkWindowBackground;
    }

    /// <summary>Zeigt Fenster erst nach erstem Render – vermeidet weißen HWND-Aufblitzer.</summary>
    public static void ShowWhenContentReady(Window window)
    {
        ApplyDarkWindowBackground(window);
        window.Opacity = 0;

        void OnContentRendered(object? sender, EventArgs e)
        {
            window.ContentRendered -= OnContentRendered;
            window.Opacity = 1;
        }

        window.ContentRendered += OnContentRendered;
        window.Show();
    }

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

        var darkMode = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref darkMode, sizeof(int));

        var captionColor = SmartPrimaryDarkColorRef;
        _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref captionColor, sizeof(int));

        var textColor = 0x00FFFFFF;
        _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref textColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);
}
