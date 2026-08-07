using System.Windows;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

public partial class SmartConfirmDialog : Window
{
    public static bool ShowConfirm(
        Window? owner,
        string title,
        string message,
        string confirmButton = "Ja",
        string cancelButton = "Nein",
        bool preferCancel = false,
        double width = 520)
    {
        var dialog = new SmartConfirmDialog(title, message, confirmButton, cancelButton, infoOnly: false, preferCancel)
        {
            Owner = owner,
            Width = width,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };
        return dialog.ShowDialog() == true;
    }

    public static void ShowInfo(
        Window? owner,
        string title,
        string message,
        string okButton = "OK",
        double width = 520)
    {
        var dialog = new SmartConfirmDialog(title, message, okButton, cancelButton: null, infoOnly: true, preferCancel: false)
        {
            Owner = owner,
            Width = width,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };
        dialog.ShowDialog();
    }

    private SmartConfirmDialog(
        string title,
        string message,
        string confirmButton,
        string? cancelButton,
        bool infoOnly,
        bool preferCancel)
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
        if (preferCancel)
        {
            ConfirmButton.IsDefault = false;
            CancelButton.IsDefault = true;
            Loaded += (_, _) => CancelButton.Focus();
        }
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
