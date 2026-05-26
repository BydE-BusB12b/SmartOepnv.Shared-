using System.Windows;

namespace SmartOepnv.AppShared;

public partial class MainShellWindow : Window
{
    public MainShellWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 32;

        var targetWidth = Math.Min(Width, workArea.Width - margin);
        var targetHeight = Math.Min(Height, workArea.Height - margin);

        Width = Math.Max(targetWidth, MinWidth);
        Height = Math.Max(targetHeight, MinHeight);

        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }
}
