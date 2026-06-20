using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SmartOepnv.AppShared.Views;

public partial class PlanerSyncBusAnimation : UserControl
{
    private const double DefaultRoadWidth = 520;
    private const double BusHeight = 44;
    private const double RoadLineTop = 74;

    private static readonly BitmapImage BusBitmap = CreateBusBitmap();

    private bool _animationRunning;

    public PlanerSyncBusAnimation()
    {
        InitializeComponent();
        BusImage.Source = BusBitmap;
        Loaded += (_, _) => ScheduleStartAnimation();
    }

    public static void PreloadBusImage() => _ = BusBitmap;

    public void StopAnimation()
    {
        _animationRunning = false;
        BusTransform.BeginAnimation(TranslateTransform.XProperty, null);
    }

    public void StartAnimation() => ScheduleStartAnimation();

    private static BitmapImage CreateBusBitmap()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(
            "pack://application:,,,/SmartOepnv.AppShared;component/Assets/planer_sync_solaris_bus.png",
            UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void ScheduleStartAnimation()
    {
        if (!IsLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, BeginBusAnimation);
    }

    private void BeginBusAnimation()
    {
        if (!IsLoaded || _animationRunning)
        {
            return;
        }

        BusImage.Height = BusHeight;
        BusImage.UpdateLayout();

        var busWidth = BusImage.ActualWidth > 1 ? BusImage.ActualWidth : 240;
        var roadWidth = RoadCanvas.ActualWidth > 1 ? RoadCanvas.ActualWidth : DefaultRoadWidth;

        Canvas.SetTop(BusImage, RoadLineTop - BusImage.ActualHeight);

        var animation = new DoubleAnimation
        {
            From = -busWidth - 12,
            To = roadWidth + 12,
            Duration = TimeSpan.FromSeconds(3.8),
            RepeatBehavior = RepeatBehavior.Forever
        };

        _animationRunning = true;
        BusTransform.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}
