using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Views;

namespace SmartOepnv.AppShared.Kom;

/// <summary>Kurzes KOM-Status-Fenster im Smart-ÖPNV-Design (auto-schließend).</summary>
public partial class KomCommandStatusDialog : Window
{
    private const int VisibleMs = 4_000;
    private static KomCommandStatusDialog? _current;
    private static DispatcherTimer? _hideTimer;

    public static void Show(Window owner, string title, string message, bool success)
    {
        if (!owner.Dispatcher.CheckAccess())
        {
            owner.Dispatcher.Invoke(() => Show(owner, title, message, success));
            return;
        }

        _hideTimer?.Stop();
        _current?.Close();
        _current = null;

        var feedbackOwner = KomFeedbackOwner.Resolve(owner);
        try
        {
            var dialog = new KomCommandStatusDialog(title, message, success)
            {
                Owner = feedbackOwner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            _current = dialog;
            dialog.Show();

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(VisibleMs) };
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer?.Stop();
                try
                {
                    if (dialog.IsVisible)
                    {
                        dialog.Close();
                    }
                }
                catch
                {
                    // bereits geschlossen
                }

                if (_current == dialog)
                {
                    _current = null;
                }
            };
            _hideTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"KomCommandStatusDialog: {ex}");
            try
            {
                SmartConfirmDialog.ShowInfo(feedbackOwner, title, message);
            }
            catch
            {
                MessageBox.Show(
                    feedbackOwner,
                    message,
                    title,
                    MessageBoxButton.OK,
                    success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
        }
    }

    private KomCommandStatusDialog(string title, string message, bool success)
    {
        InitializeComponent();
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        IconText.Text = success ? "✓" : "⚠";
        IconText.Foreground = success
            ? (Brush)FindResource("SmartSuccessForegroundBrush")
            : new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
    }
}
