using System.ComponentModel;
using System.Windows;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

public partial class AppExitSavingDialog : Window
{
    private bool _allowClose;

    public AppExitSavingDialog(string? message = null)
    {
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        InitializeComponent();
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
        if (!string.IsNullOrWhiteSpace(message))
        {
            MessageText.Text = message;
        }
    }

    public void PrepareToClose()
    {
        _allowClose = true;
        BusAnimation.StopAnimation();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
