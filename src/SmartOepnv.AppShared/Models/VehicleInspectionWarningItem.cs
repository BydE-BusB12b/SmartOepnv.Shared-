using System.Windows.Media;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Models;

public sealed class VehicleInspectionWarningItem
{
    public required string Message { get; init; }
    /// <summary>Nur Ziffern – Zuordnung zur Fahrzeugliste.</summary>
    public string PhoneNormalized { get; init; } = string.Empty;
    public VehicleInspectionWarningLevel Level { get; init; }
    public Brush BackgroundBrush { get; init; } = Brushes.Transparent;
    public Brush ForegroundBrush { get; init; } = Brushes.White;

    public static VehicleInspectionWarningItem FromWarning(VehicleInspectionWarning warning)
    {
        var (bg, fg) = warning.Level switch
        {
            VehicleInspectionWarningLevel.Notice30To16Days => ("#FFF176", "#1A1A1A"),
            VehicleInspectionWarningLevel.Notice15To5Days => ("#FF9800", "#FFFFFF"),
            VehicleInspectionWarningLevel.Urgent4To1Days => ("#E53935", "#FFFFFF"),
            VehicleInspectionWarningLevel.Overdue => ("#9C27B0", "#FFFFFF"),
            _ => ("#455A64", "#FFFFFF")
        };

        return new VehicleInspectionWarningItem
        {
            Message = warning.Message,
            PhoneNormalized = warning.PhoneNormalized,
            Level = warning.Level,
            BackgroundBrush = CreateBrush(bg),
            ForegroundBrush = CreateBrush(fg)
        };
    }

    private static SolidColorBrush CreateBrush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);
}
