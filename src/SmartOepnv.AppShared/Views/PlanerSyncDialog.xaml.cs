using System.ComponentModel;
using System.Windows;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Views;

public partial class PlanerSyncDialog : Window
{
    private bool _allowClose;

    public PlanerSyncDialog()
    {
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        InitializeComponent();
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
        ContentRendered += (_, _) =>
        {
            Activate();
            BusAnimation.StartAnimation();
        };
    }

    public void ShowLoginPhase(string phase = "Anmeldung bei Dropbox…")
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowLoginPhase(phase));
            return;
        }

        TransferProgress.Reset(phase);
    }

    public void ShowSyncPhase(string phase = "Arbeitsstand wird mit Dropbox abgeglichen…") =>
        ShowLoginPhase(phase);

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
