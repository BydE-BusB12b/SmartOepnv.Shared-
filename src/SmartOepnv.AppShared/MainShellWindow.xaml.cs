using System.ComponentModel;
using System.Windows;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared;

public partial class MainShellWindow : Window
{
    public MainShellWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!AppServices.IsInitialized)
        {
            return;
        }

        AppServices.FlushAllPendingEdits();
        SmartOepnvDataBackupService.BackupAllProfiles("app-exit");
    }
}
