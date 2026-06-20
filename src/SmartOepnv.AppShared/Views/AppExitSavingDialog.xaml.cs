using System.ComponentModel;
using System.Windows;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.Dropbox;

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

        ContentRendered += (_, _) =>
        {
            Activate();
            BusAnimation.StartAnimation();
            TransferProgress.Reset("Arbeitsstand wird vorbereitet…");
        };
    }

    public void StartBusAnimation() => BusAnimation.StartAnimation();

    public void UpdateTransferProgress(DropboxTransferProgress progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateTransferProgress(progress));
            return;
        }

        TransferProgress.Update(progress);
    }

    public void PrepareToClose()
    {
        _allowClose = true;
        BusAnimation.StopAnimation();
        TransferProgress.Hide();
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
