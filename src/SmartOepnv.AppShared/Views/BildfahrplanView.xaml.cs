using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class BildfahrplanView : UserControl
{
    private const double LeftPad = 150;
    private const double RightPad = 24;
    private const double TopPad = 16;
    private const double BottomPad = 36;
    private const double BasePixelsPerHour = 110;
    private const double MinChartHeight = 520;

    private BildfahrplanViewModel? _viewModel;

    public BildfahrplanView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as BildfahrplanViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        Redraw();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BildfahrplanViewModel.Chart)
            or nameof(BildfahrplanViewModel.ZoomPercent)
            or nameof(BildfahrplanViewModel.SelectedTripKey)
            or null)
        {
            Dispatcher.BeginInvoke(Redraw);
        }
    }

    private void OnChartPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_viewModel is null || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        if (e.Delta > 0)
        {
            _viewModel.ZoomInCommand.Execute(null);
        }
        else
        {
            _viewModel.ZoomOutCommand.Execute(null);
        }

        e.Handled = true;
    }

    private void OnLegendClick(object sender, MouseButtonEventArgs e)
    {
        if (FindLegendItem(e.OriginalSource) is { } item)
        {
            _viewModel?.OpenTripCommand.Execute(item.RouteKey);
        }
    }

    private void OnLegendDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindLegendItem(e.OriginalSource) is { } item)
        {
            _viewModel?.OpenTripCommand.Execute(item.RouteKey);
        }
    }

    private static BildfahrplanTripLegendItem? FindLegendItem(object? source)
    {
        if (source is not DependencyObject current)
        {
            return null;
        }

        while (current is not null)
        {
            if (current is ListBoxItem { DataContext: BildfahrplanTripLegendItem item })
            {
                return item;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void Redraw()
    {
        ChartCanvas.Children.Clear();
        var chart = _viewModel?.Chart;
        if (chart is null || chart.AxisStops.Count < 2)
        {
            ChartCanvas.Width = 800;
            ChartCanvas.Height = 400;
            ChartCanvas.Children.Add(new TextBlock
            {
                Text = "Keine Darstellung – Linie/Kurs und Richtung wählen.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(24)
            });
            return;
        }

        var zoom = Math.Clamp((_viewModel?.ZoomPercent ?? 100) / 100.0, 0.5, 4.0);
        var pixelsPerHour = BasePixelsPerHour * zoom;
        var windowMinutes = Math.Max(60, chart.WindowEndMinutes - chart.WindowStartMinutes);
        var plotW = Math.Max(400, windowMinutes / 60.0 * pixelsPerHour);
        var plotH = Math.Max(MinChartHeight, chart.AxisStops.Count * 36.0) * Math.Max(1.0, Math.Sqrt(zoom));
        var totalW = LeftPad + plotW + RightPad;
        var totalH = TopPad + plotH + BottomPad;
        ChartCanvas.Width = totalW;
        ChartCanvas.Height = totalH;

        var totalMeters = Math.Max(1, chart.TotalMeters);
        var selectedKey = _viewModel?.SelectedTripKey;

        double X(int minutes) =>
            LeftPad + (minutes - chart.WindowStartMinutes) / (double)windowMinutes * plotW;

        // Gleiche Y-Abbildung für Haltestellenlinien und Fahrtpolylinien
        double Y(double meters) =>
            TopPad + (1.0 - meters / totalMeters) * plotH;

        for (var m = chart.WindowStartMinutes; m <= chart.WindowEndMinutes; m += 60)
        {
            var x = X(m);
            ChartCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = TopPad,
                Y2 = TopPad + plotH,
                Stroke = new SolidColorBrush(Color.FromRgb(220, 225, 232)),
                StrokeThickness = 1
            });
            var hour = m / 60;
            var label = new TextBlock
            {
                Text = $"{hour:00}:00",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 100, 110))
            };
            Canvas.SetLeft(label, x - 14);
            Canvas.SetTop(label, TopPad + plotH + 8);
            ChartCanvas.Children.Add(label);
        }

        for (var m = chart.WindowStartMinutes; m <= chart.WindowEndMinutes; m += 10)
        {
            if (m % 60 == 0)
            {
                continue;
            }

            var x = X(m);
            ChartCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = TopPad,
                Y2 = TopPad + plotH,
                Stroke = new SolidColorBrush(Color.FromRgb(236, 240, 244)),
                StrokeThickness = 1
            });
        }

        for (var si = 0; si < chart.AxisStops.Count; si++)
        {
            var stop = chart.AxisStops[si];
            var y = Y(stop.DistanceMeters);
            ChartCanvas.Children.Add(new Line
            {
                X1 = LeftPad,
                X2 = LeftPad + plotW,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(210, 216, 224)),
                StrokeThickness = 1
            });

            var km = stop.DistanceMeters / 1000.0;
            var nameBlock = new TextBlock
            {
                Text = stop.Name,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 40, 55)),
                TextAlignment = TextAlignment.Right,
                Width = LeftPad - 16
            };
            Canvas.SetLeft(nameBlock, 4);
            Canvas.SetTop(nameBlock, y - 8);
            ChartCanvas.Children.Add(nameBlock);

            var kmBlock = new TextBlock
            {
                Text = km.ToString("0.##", CultureInfo.GetCultureInfo("de-DE")),
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 130, 140)),
                TextAlignment = TextAlignment.Right,
                Width = LeftPad - 16
            };
            Canvas.SetLeft(kmBlock, 4);
            Canvas.SetTop(kmBlock, y + 6);
            ChartCanvas.Children.Add(kmBlock);
        }

        var frame = new Rectangle
        {
            Width = plotW,
            Height = plotH,
            Stroke = new SolidColorBrush(Color.FromRgb(160, 170, 185)),
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(frame, LeftPad);
        Canvas.SetTop(frame, TopPad);
        ChartCanvas.Children.Add(frame);

        foreach (var trip in chart.Trips)
        {
            var brush = TryBrush(trip.ColorHex);
            var selected = selectedKey is not null &&
                           string.Equals(selectedKey, trip.RouteKey, StringComparison.Ordinal);

            PointCollection? labelGeo = null;
            foreach (var run in SplitForwardTimeRuns(trip.Points))
            {
                if (run.Count < 2)
                {
                    continue;
                }

                var geo = new PointCollection();
                foreach (var p in run)
                {
                    geo.Add(new Point(X(p.TimeMinutes), Y(p.DistanceMeters)));
                }

                labelGeo ??= geo;

                var hit = new Polyline
                {
                    Points = geo,
                    Stroke = Brushes.Transparent,
                    StrokeThickness = 14,
                    Cursor = Cursors.Hand,
                    Tag = trip.RouteKey,
                    ToolTip = $"Fahrt {trip.Label} · {(trip.IsOutbound ? "Hin" : "Rück")} – Klick öffnet"
                };
                hit.MouseLeftButtonDown += OnTripClicked;
                ChartCanvas.Children.Add(hit);

                ChartCanvas.Children.Add(new Polyline
                {
                    Points = geo,
                    Stroke = brush,
                    StrokeThickness = selected ? 4.2 : 2.4,
                    StrokeLineJoin = PenLineJoin.Round,
                    Opacity = selected ? 1 : 0.9,
                    IsHitTestVisible = false
                });
            }

            if (labelGeo is { Count: >= 2 })
            {
                var mid = labelGeo[labelGeo.Count / 2];
                var tag = new TextBlock
                {
                    Text = trip.Label,
                    FontSize = selected ? 12 : 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = brush,
                    Background = new SolidColorBrush(Color.FromArgb(200, 250, 251, 252)),
                    Cursor = Cursors.Hand,
                    Tag = trip.RouteKey,
                    ToolTip = $"Fahrt {trip.Label} öffnen"
                };
                tag.MouseLeftButtonDown += OnTripClicked;
                Canvas.SetLeft(tag, mid.X + 4);
                Canvas.SetTop(tag, mid.Y - 12);
                ChartCanvas.Children.Add(tag);
            }
        }
    }

    private void OnTripClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string routeKey })
        {
            _viewModel?.OpenTripCommand.Execute(routeKey);
            e.Handled = true;
        }
    }

    /// <summary>Keine Linien rückwärts in der Zeit (rechts→links).</summary>
    private static List<List<BildfahrplanPoint>> SplitForwardTimeRuns(IReadOnlyList<BildfahrplanPoint> points)
    {
        var runs = new List<List<BildfahrplanPoint>>();
        List<BildfahrplanPoint>? current = null;
        BildfahrplanPoint? prev = null;
        foreach (var p in points)
        {
            if (prev is null || p.TimeMinutes >= prev.TimeMinutes)
            {
                current ??= [];
                current.Add(p);
            }
            else
            {
                if (current is { Count: > 0 })
                {
                    runs.Add(current);
                }

                current = [p];
            }

            prev = p;
        }

        if (current is { Count: > 0 })
        {
            runs.Add(current);
        }

        return runs;
    }

    private static Brush TryBrush(string hex)
    {
        try
        {
            return (Brush)new BrushConverter().ConvertFromString(hex)!;
        }
        catch
        {
            return Brushes.SteelBlue;
        }
    }
}
