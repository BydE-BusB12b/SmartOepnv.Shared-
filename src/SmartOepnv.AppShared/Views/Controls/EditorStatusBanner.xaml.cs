using System.Windows;
using System.Windows.Controls;

namespace SmartOepnv.AppShared.Views.Controls;

public partial class EditorStatusBanner : UserControl
{
    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(EditorStatusBanner), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsSuccessProperty =
        DependencyProperty.Register(nameof(IsSuccess), typeof(bool), typeof(EditorStatusBanner), new PropertyMetadata(false));

    public static readonly DependencyProperty LightOnDarkProperty =
        DependencyProperty.Register(nameof(LightOnDark), typeof(bool), typeof(EditorStatusBanner), new PropertyMetadata(false));

    public static readonly DependencyProperty MessageFontSizeProperty =
        DependencyProperty.Register(nameof(MessageFontSize), typeof(double), typeof(EditorStatusBanner), new PropertyMetadata(12d));

    public EditorStatusBanner()
    {
        InitializeComponent();
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public bool IsSuccess
    {
        get => (bool)GetValue(IsSuccessProperty);
        set => SetValue(IsSuccessProperty, value);
    }

    public bool LightOnDark
    {
        get => (bool)GetValue(LightOnDarkProperty);
        set => SetValue(LightOnDarkProperty, value);
    }

    public double MessageFontSize
    {
        get => (double)GetValue(MessageFontSizeProperty);
        set => SetValue(MessageFontSizeProperty, value);
    }
}
