using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>Gelbe Hervorhebung für Zusammenfüge-Ansagen (<c>…_zusammen.wav</c>).</summary>
public static class MergedAnnouncementUiHighlight
{
    public static readonly Brush RowBackground = CreateFrozenBrush(0x4D, 0xFF, 0xEB, 0x3B);
    public static readonly Brush RowBorder = CreateFrozenBrush(0x99, 0xFF, 0xEB, 0x3B);

    public static void ApplyToListBox(ListBox listBox) =>
        listBox.ItemContainerStyle = CreateListBoxItemStyle(listBox.ItemContainerStyle);

    private static Style CreateListBoxItemStyle(Style? basedOn)
    {
        var baseStyle = basedOn ?? Application.Current?.TryFindResource("MaterialDesignListBoxItem") as Style;
        var style = baseStyle is null
            ? new Style(typeof(ListBoxItem))
            : new Style(typeof(ListBoxItem), baseStyle);

        var trigger = new DataTrigger
        {
            Binding = new Binding { Converter = MergedAnnouncementHighlightConverter.Instance },
            Value = true
        };
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, RowBackground));
        trigger.Setters.Add(new Setter(Control.BorderBrushProperty, RowBorder));
        trigger.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Triggers.Add(trigger);
        return style;
    }

    private static SolidColorBrush CreateFrozenBrush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}

internal sealed class MergedAnnouncementHighlightConverter : IValueConverter
{
    public static MergedAnnouncementHighlightConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            string fileName => AnnouncementSequenceExport.IsLegacyMergedFileName(fileName),
            ManagedAnnouncementTemplateItem announcement =>
                AnnouncementSequenceExport.IsLegacyMergedFileName(announcement.EmbeddedSoundFileName),
            _ => false
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
