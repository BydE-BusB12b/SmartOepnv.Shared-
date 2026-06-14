using System.Globalization;
using System.Windows.Data;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.AppShared.Converters;

public sealed class RemarkDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DutyTemplateRemarkHelper.GetDisplayCode(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() ?? string.Empty;
}
