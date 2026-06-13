using System.ComponentModel;
using System.Windows;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

public partial class PlanerSyncDialog : Window
{
    private bool _allowClose;

    public PlanerSyncDialog()
    {
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        InitializeComponent();
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
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
