using System.ComponentModel;
using System.Windows;

namespace SmartOepnv.AppShared.Views;

public partial class AppExitSavingDialog : Window
{
    private bool _allowClose;

    public AppExitSavingDialog()
    {
        InitializeComponent();
    }

    public void PrepareToClose()
    {
        _allowClose = true;
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
