using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Pdf;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.AppShared.ViewModels;

public partial class BildfahrplanViewModel : ObservableObject
{
    private static readonly string[] Palette =
    [
        "#E53935", "#1E88E5", "#43A047", "#FB8C00", "#8E24AA",
        "#00897B", "#6D4C41", "#3949AB", "#C0CA33", "#D81B60"
    ];

    private int _loadedRevision = -1;

    /// <summary>Route-Schlüssel der angeklickten Fahrt – MainViewModel öffnet Routen.</summary>
    public event Action<string>? OpenRouteRequested;

    public ObservableCollection<string> CorridorOptions { get; } = [];
    public ObservableCollection<BildfahrplanTripLegendItem> TripLegend { get; } = [];
    public ObservableCollection<BildfahrplanDirectionOption> DirectionOptions { get; } =
    [
        new("all", "Beide Richtungen"),
        new("hin", "Hin (Start oben)"),
        new("rueck", "Rück (Start unten)")
    ];

    [ObservableProperty] private string? selectedCorridor;
    [ObservableProperty] private BildfahrplanDirectionOption? selectedDirection;
    [ObservableProperty] private string statusMessage = "Linie/Kurs wählen – Y-Achse aus gesnapptem Fahrweg.";
    [ObservableProperty] private BildfahrplanChartModel? chart;
    [ObservableProperty] private int windowStartHour = 5;
    [ObservableProperty] private int windowEndHour = 22;
    [ObservableProperty] private int zoomPercent = 100;
    [ObservableProperty] private string? selectedTripKey;

    public string ZoomLabel => $"{ZoomPercent} %";

    public BildfahrplanViewModel()
    {
        SelectedDirection = DirectionOptions[0];
    }

    public void RefreshFromEditorIfNeeded()
    {
        if (_loadedRevision == AppServices.Routes.EditorDataRevision && CorridorOptions.Count > 0)
        {
            RebuildChart();
            return;
        }

        RefreshFromEditor();
    }

    public void RefreshFromEditor()
    {
        var editor = AppServices.Routes.Editor;
        CorridorOptions.Clear();
        TripLegend.Clear();
        Chart = null;
        SelectedTripKey = null;

        if (editor is null)
        {
            StatusMessage = "Kein Fahrplan geladen.";
            _loadedRevision = AppServices.Routes.EditorDataRevision;
            return;
        }

        var corridors = editor.RouteNames
            .Select(k => RouteDisplayHelper.NormalizeLineCourse(RouteDisplayHelper.Parse(k).LineCourse))
            .Where(lc => !string.IsNullOrWhiteSpace(lc))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(lc => lc, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var c in corridors)
        {
            CorridorOptions.Add(c);
        }

        _loadedRevision = AppServices.Routes.EditorDataRevision;

        if (CorridorOptions.Count == 0)
        {
            StatusMessage = "Keine Linie/Kurs in den Routen gefunden.";
            SelectedCorridor = null;
            return;
        }

        if (SelectedCorridor is null ||
            !CorridorOptions.Contains(SelectedCorridor, StringComparer.OrdinalIgnoreCase))
        {
            SelectedCorridor = CorridorOptions[0];
        }
        else
        {
            RebuildChart();
        }
    }

    partial void OnSelectedCorridorChanged(string? value) => RebuildChart();

    partial void OnSelectedDirectionChanged(BildfahrplanDirectionOption? value) => RebuildChart();

    partial void OnWindowStartHourChanged(int value) => RebuildChart();

    partial void OnWindowEndHourChanged(int value) => RebuildChart();

    partial void OnZoomPercentChanged(int value) => OnPropertyChanged(nameof(ZoomLabel));

    [RelayCommand]
    private void Refresh() => RefreshFromEditor();

    [RelayCommand]
    private void ZoomIn() => ZoomPercent = Math.Min(400, ZoomPercent + 25);

    [RelayCommand]
    private void ZoomOut() => ZoomPercent = Math.Max(50, ZoomPercent - 25);

    [RelayCommand]
    private void ZoomReset() => ZoomPercent = 100;

    [RelayCommand]
    private void ExportPdf()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null || string.IsNullOrWhiteSpace(SelectedCorridor))
        {
            StatusMessage = "Kein Fahrplan / keine Linie – PDF nicht möglich.";
            return;
        }

        // Kompletter Tag für PDF, Anzeige-Fenster danach wiederherstellen
        var prevStart = WindowStartHour;
        var prevEnd = WindowEndHour;
        try
        {
            WindowStartHour = 0;
            WindowEndHour = 24;
            RebuildChart();
            if (Chart is null || Chart.Trips.Count == 0)
            {
                StatusMessage = "Keine Fahrten für PDF-Export.";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Bildfahrplan als PDF speichern",
                Filter = "PDF-Datei (*.pdf)|*.pdf",
                FileName = BildfahrplanPdfGenerator.BuildDefaultFileName(SelectedCorridor),
                AddExtension = true,
                DefaultExt = ".pdf"
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            var legend = TripLegend
                .Select(t => (t.Label, t.ColorHex, t.DirectionLabel))
                .ToList();
            BildfahrplanPdfGenerator.Generate(
                dialog.FileName,
                new BildfahrplanPdfGenerator.Model(
                    SelectedCorridor!,
                    SelectedDirection?.Label ?? "Beide Richtungen",
                    "00:00–24:00 (kompletter Tag)",
                    StatusMessage,
                    Chart,
                    legend));

            StatusMessage = $"PDF gespeichert: {Path.GetFileName(dialog.FileName)}";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dialog.FileName,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Öffnen optional
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF fehlgeschlagen: {ex.Message}";
            MessageBox.Show(
                Application.Current?.MainWindow,
                ex.Message,
                "Bildfahrplan",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            WindowStartHour = prevStart;
            WindowEndHour = prevEnd;
            RebuildChart();
        }
    }

    [RelayCommand]
    private void OpenTrip(string? routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
        {
            return;
        }

        SelectedTripKey = routeKey;
        OpenRouteRequested?.Invoke(routeKey);
    }

    public void SelectTrip(string? routeKey)
    {
        SelectedTripKey = routeKey;
        OnPropertyChanged(nameof(Chart));
    }

    private void RebuildChart()
    {
        TripLegend.Clear();
        Chart = null;

        var editor = AppServices.Routes.Editor;
        var corridor = SelectedCorridor;
        if (editor is null || string.IsNullOrWhiteSpace(corridor))
        {
            StatusMessage = "Linie/Kurs wählen.";
            return;
        }

        var routeKeys = editor.RouteNames
            .Where(k => string.Equals(
                RouteDisplayHelper.NormalizeLineCourse(RouteDisplayHelper.Parse(k).LineCourse),
                corridor,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, Comparer<string>.Create((a, b) =>
            {
                var sorted = RouteDisplayHelper.SortRoutesByLineCourseAndTrip([a, b]);
                return sorted[0] == a ? -1 : 1;
            }))
            .ToList();

        if (routeKeys.Count == 0)
        {
            StatusMessage = $"Keine Fahrten für {corridor}.";
            return;
        }

        var corridorAxis = BildfahrplanCorridorBuilder.Build(editor, corridor, routeKeys);
        if (corridorAxis.Stations.Count < 2)
        {
            StatusMessage = "Zu wenige Halte für die Y-Achse.";
            return;
        }

        var chainColorByRoute = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var colorIdx = 0;
        foreach (var chain in corridorAxis.Chains)
        {
            var hex = Palette[colorIdx % Palette.Length];
            colorIdx++;
            foreach (var routeKey in chain)
            {
                chainColorByRoute[RouteDisplayHelper.ToCanonicalRouteKey(routeKey)] = hex;
                chainColorByRoute[routeKey] = hex;
            }
        }

        var startMin = Math.Clamp(WindowStartHour, 0, 23) * 60;
        var endMin = Math.Clamp(WindowEndHour, 1, 24) * 60;
        if (endMin <= startMin)
        {
            endMin = startMin + 60;
        }

        var directionId = SelectedDirection?.Id ?? "all";
        var trips = new List<BildfahrplanTripGeometry>();
        var hinCount = 0;
        var rueckCount = 0;
        var totalMeters = Math.Max(1, corridorAxis.TotalMeters);

        foreach (var key in routeKeys)
        {
            var stops = editor.GetStops(key);
            var points = new List<BildfahrplanPoint>();
            foreach (var stop in stops)
            {
                if (!BildfahrplanStopAxis.IsAxisLabelStop(stop))
                {
                    continue;
                }

                if (!TryParseTimeToMinutes(stop.Time, out var minutes))
                {
                    // Pause o. Ä. ohne Uhrzeit: nicht plotten, aber Achse hat das Label
                    continue;
                }

                if (!BildfahrplanCorridorBuilder.TryResolveMeters(
                        stop, corridorAxis.MetersByLookupKey, snappedShape: null, out var meters))
                {
                    continue;
                }

                var name = BildfahrplanStopAxis.AxisDisplayName(stop);
                points.Add(new BildfahrplanPoint(minutes, meters, name));
            }

            if (points.Count < 2)
            {
                continue;
            }

            // Nach Schnur-Orientierung: Start oben (hohe Meter) = Hin
            var isHin = points[0].DistanceMeters >= totalMeters * 0.45;
            if (isHin)
            {
                hinCount++;
            }
            else
            {
                rueckCount++;
            }

            if (directionId == "hin" && !isHin)
            {
                continue;
            }

            if (directionId == "rueck" && isHin)
            {
                continue;
            }

            var canonical = RouteDisplayHelper.ToCanonicalRouteKey(key);
            if (!chainColorByRoute.TryGetValue(key, out var color) &&
                !chainColorByRoute.TryGetValue(canonical, out color))
            {
                color = Palette[colorIdx % Palette.Length];
                colorIdx++;
            }

            var draft = RoutePathDraftRepository.LoadOrCreate(key, stops, editor.PackageRoot);
            var draftColor = NormalizeHex(draft.RouteLineColor);
            if (!draftColor.Equals("#2196f3", StringComparison.OrdinalIgnoreCase))
            {
                color = draftColor;
            }

            var parsed = RouteDisplayHelper.Parse(key);
            var label = string.IsNullOrWhiteSpace(parsed.TripNumber)
                ? key
                : parsed.TripNumber.Trim();

            var visible = points
                .Where(p => p.TimeMinutes >= startMin - 30 && p.TimeMinutes <= endMin + 30)
                .ToList();
            if (visible.Count < 2)
            {
                continue;
            }

            var dirLabel = isHin ? "Hin" : "Rück";
            trips.Add(new BildfahrplanTripGeometry(key, label, color, visible, isHin));
            TripLegend.Add(new BildfahrplanTripLegendItem(label, color, key, dirLabel));
        }

        if (trips.Count == 0)
        {
            StatusMessage = directionId == "all"
                ? $"Keine Fahrten mit Zeiten im Fenster {WindowStartHour:00}:00–{WindowEndHour:00}:00."
                : $"Keine Fahrten in dieser Richtung im Zeitfenster ({hinCount} Hin / {rueckCount} Rück gesamt).";
            return;
        }

        var flipAxis = directionId == "rueck";
        var axisStops = corridorAxis.Stations
            .Select(s => new BildfahrplanAxisStop(
                s.DisplayName,
                flipAxis ? totalMeters - s.DistanceMeters : s.DistanceMeters))
            .ToList();
        if (flipAxis)
        {
            axisStops.Reverse();
            trips = trips.Select(t => t with
            {
                Points = t.Points
                    .Select(p => p with { DistanceMeters = totalMeters - p.DistanceMeters })
                    .ToList()
            }).ToList();
        }

        // Achse und Fahrten auf denselben Meter-Bereich (kein Überstand unter der letzten Haltlinie)
        var axisMin = axisStops.Min(s => s.DistanceMeters);
        var axisMax = axisStops.Max(s => s.DistanceMeters);
        if (axisMin > 0.5)
        {
            axisStops = axisStops
                .Select(s => s with { DistanceMeters = s.DistanceMeters - axisMin })
                .ToList();
            trips = trips.Select(t => t with
            {
                Points = t.Points
                    .Select(p => p with { DistanceMeters = p.DistanceMeters - axisMin })
                    .ToList()
            }).ToList();
            axisMax -= axisMin;
            axisMin = 0;
        }

        totalMeters = Math.Max(1, axisMax);
        trips = trips.Select(t => t with
        {
            Points = t.Points
                .Select(p => p with
                {
                    DistanceMeters = Math.Clamp(p.DistanceMeters, axisMin, axisMax)
                })
                .ToList()
        }).ToList();

        Chart = new BildfahrplanChartModel(
            axisStops,
            totalMeters,
            startMin,
            endMin,
            trips,
            corridorAxis.UsedSnappedPath,
            corridorAxis.ReferenceRouteKey,
            corridorAxis.FlippedForChain || flipAxis);

        var snap = corridorAxis.UsedSnappedPath
            ? $"Y aus Snap ({totalMeters / 1000.0:0.##} km)"
            : "Y ohne Snap (Fallback)";
        var chainInfo = $"{corridorAxis.Chains.Count} Schnur(en)";
        var orient = corridorAxis.FlippedForChain ? " · Achse an Verknüpfung" : "";
        StatusMessage =
            $"{trips.Count} Fahrt(en) · {snap} · {chainInfo} · {hinCount} Hin / {rueckCount} Rück{orient}";
    }

    private static string NormalizeHex(string? hex)
    {
        var h = (hex ?? string.Empty).Trim();
        if (h.Length == 0)
        {
            return "#2196f3";
        }

        if (!h.StartsWith('#'))
        {
            h = "#" + h;
        }

        return h;
    }

    private static bool TryParseTimeToMinutes(string? time, out int minutes)
    {
        minutes = 0;
        var t = (time ?? string.Empty).Trim();
        if (t.Length == 0)
        {
            return false;
        }

        if (TimeSpan.TryParseExact(t, @"h\:mm", CultureInfo.InvariantCulture, out var ts) ||
            TimeSpan.TryParseExact(t, @"hh\:mm", CultureInfo.InvariantCulture, out ts) ||
            TimeSpan.TryParse(t, CultureInfo.InvariantCulture, out ts))
        {
            minutes = (int)ts.TotalMinutes;
            if (minutes >= 0 && minutes < 24 * 60)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record BildfahrplanDirectionOption(string Id, string Label);

public sealed record BildfahrplanAxisStop(string Name, double DistanceMeters);

public sealed record BildfahrplanPoint(int TimeMinutes, double DistanceMeters, string StopName);

public sealed record BildfahrplanTripGeometry(
    string RouteKey,
    string Label,
    string ColorHex,
    IReadOnlyList<BildfahrplanPoint> Points,
    bool IsOutbound);

public sealed record BildfahrplanChartModel(
    IReadOnlyList<BildfahrplanAxisStop> AxisStops,
    double TotalMeters,
    int WindowStartMinutes,
    int WindowEndMinutes,
    IReadOnlyList<BildfahrplanTripGeometry> Trips,
    bool UsedSnappedPath,
    string ReferenceRouteKey,
    bool AxisFlipped);

public sealed class BildfahrplanTripLegendItem(string label, string colorHex, string routeKey, string directionLabel)
{
    public string Label { get; } = label;
    public string ColorHex { get; } = colorHex;
    public string RouteKey { get; } = routeKey;
    public string DirectionLabel { get; } = directionLabel;
    public string DisplayLine => $"{Label} · {DirectionLabel}";

    public Brush ColorBrush
    {
        get
        {
            try
            {
                return (Brush)new BrushConverter().ConvertFromString(ColorHex)!;
            }
            catch
            {
                return Brushes.Gray;
            }
        }
    }
}
