using System.Windows.Media;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Models;

public sealed class EmployeeDocumentCheckWarningItem
{
    public required string Message { get; init; }

    public string PersonnelNumberNormalized { get; init; } = string.Empty;

    public Brush BackgroundBrush { get; init; } = Brushes.Transparent;

    public Brush ForegroundBrush { get; init; } = Brushes.White;

    public static EmployeeDocumentCheckWarningItem FromWarning(EmployeeDocumentCheckWarning warning) =>
        new()
        {
            Message = warning.Message,
            PersonnelNumberNormalized = warning.PersonnelNumberNormalized,
            BackgroundBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800")!),
            ForegroundBrush = Brushes.White
        };
}
