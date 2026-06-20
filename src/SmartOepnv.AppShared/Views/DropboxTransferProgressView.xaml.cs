using System.Windows;
using System.Windows.Controls;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Views;

public partial class DropboxTransferProgressView : UserControl
{
    public DropboxTransferProgressView()
    {
        InitializeComponent();
    }

    public void Reset(string phase)
    {
        Visibility = Visibility.Visible;
        PhaseText.Text = phase;
        TransferProgressBar.Value = 0;
        PercentText.Text = "0%";
        EtaText.Text = string.Empty;
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        TransferProgressBar.Value = 0;
        PercentText.Text = string.Empty;
        EtaText.Text = string.Empty;
        PhaseText.Text = string.Empty;
    }

    public void Update(DropboxTransferProgress progress)
    {
        Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(progress.Phase))
        {
            PhaseText.Text = progress.Phase;
        }

        TransferProgressBar.Value = progress.Percent;
        PercentText.Text = progress.PercentDisplay;
        EtaText.Text = progress.EtaDisplay;
    }
}
