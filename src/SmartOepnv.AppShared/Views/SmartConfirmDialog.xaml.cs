using System.Windows;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

public partial class SmartConfirmDialog : Window
{
    public static bool ShowConfirm(
        Window owner,
        string title,
        string message,
        string confirmButton = "Ja",
        string cancelButton = "Nein")
    {
        var dialog = new SmartConfirmDialog(title, message, confirmButton, cancelButton, infoOnly: false)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        return dialog.ShowDialog() == true;
    }

    public static void ShowInfo(
        Window owner,
        string title,
        string message,
        string okButton = "OK")
    {
        var dialog = new SmartConfirmDialog(title, message, okButton, cancelButton: null, infoOnly: true)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog();
    }

    private SmartConfirmDialog(
        string title,
        string message,
        string confirmButton,
        string? cancelButton,
        bool infoOnly)
    {
        InitializeComponent();
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmButton;

        if (infoOnly || string.IsNullOrWhiteSpace(cancelButton))
        {
            CancelButton.Visibility = Visibility.Collapsed;
            return;
        }

        CancelButton.Content = cancelButton;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
