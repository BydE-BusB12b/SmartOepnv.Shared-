using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SmartOepnv.AppShared.Views;

public partial class PlanerSyncBusAnimation : UserControl
{
    private const double DefaultRoadWidth = 520;
    private const double BusHeight = 44;
    private const double RoadLineTop = 74;

    public PlanerSyncBusAnimation()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void StopAnimation()
    {
        BusTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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

        BusTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, animation);
    }
}
