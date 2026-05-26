using System.Windows.Media;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Models;

public sealed class DriverCredentialWarningItem
{
    public required string Message { get; init; }
    public string PersonnelNumberNormalized { get; init; } = string.Empty;
    public DriverCredentialWarningLevel Level { get; init; }
    public Brush BackgroundBrush { get; init; } = Brushes.Transparent;
    public Brush ForegroundBrush { get; init; } = Brushes.White;

    public static DriverCredentialWarningItem FromWarning(DriverCredentialWarning warning)
    {
        var (bg, fg) = warning.Level switch
        {
            DriverCredentialWarningLevel.ExpiringSoon => ("#FFF176", "#1A1A1A"),
            DriverCredentialWarningLevel.Expired => ("#E53935", "#FFFFFF"),
            _ => ("#455A64", "#FFFFFF")
        };

        return new DriverCredentialWarningItem
        {
            Message = warning.Message,
            PersonnelNumberNormalized = warning.PersonnelNumberNormalized,
            Level = warning.Level,
            BackgroundBrush = CreateBrush(bg),
            ForegroundBrush = CreateBrush(fg)
        };
    }

    private static SolidColorBrush CreateBrush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex)!);
}
