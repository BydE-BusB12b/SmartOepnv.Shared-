using System.ComponentModel;
using System.Windows;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared;

public partial class MainShellWindow : Window
{
    private bool _closeConfirmed;

    public MainShellWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || !AppServices.IsInitialized)
        {
            return;
        }

        e.Cancel = true;

        var savingDialog = new AppExitSavingDialog
        {
            Owner = this
        };
        savingDialog.Show();
        IsEnabled = false;

        try
        {
            await Task.Run(() =>
            {
                AppServices.FlushAllPendingEdits();
                SmartOepnvDataBackupService.BackupAllProfiles("app-exit");
            }).ConfigureAwait(true);
        }
        catch
        {
            // Beenden trotzdem erlauben
        }

        savingDialog.PrepareToClose();
        savingDialog.Close();
        _closeConfirmed = true;
        Close();
    }
}
