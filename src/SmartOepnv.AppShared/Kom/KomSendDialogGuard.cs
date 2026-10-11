using System.Windows;

namespace SmartOepnv.AppShared.Kom;

/// <summary>Verhindert Schließen während Upload/Ack-Wartezeit (sonst crasht Status-Fenster).</summary>
internal sealed class KomSendDialogGuard
{
    private readonly Window _window;
    private bool _sendInProgress;

    public KomSendDialogGuard(Window window)
    {
        _window = window;
        _window.Closing += OnClosing;
    }

    public bool IsSendInProgress => _sendInProgress;

    public void BeginSend() => _sendInProgress = true;

    public void EndSend() => _sendInProgress = false;

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_sendInProgress)
        {
            e.Cancel = true;
        }
    }
}
