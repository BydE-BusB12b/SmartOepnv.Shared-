using System.Windows;
using System.Windows.Input;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core;
using SmartOepnv.Core.Session;

namespace SmartOepnv.AppShared.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(string? lockWarning = null)
    {
        InitializeComponent();
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
        if (!string.IsNullOrWhiteSpace(lockWarning))
        {
            LockWarningText.Text = lockWarning;
            LockWarningText.Visibility = Visibility.Visible;
            ForceReleaseLockButton.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) => UsernameBox.Focus();
    }

    private void SetupDropbox_Click(object sender, RoutedEventArgs e)
    {
        var setup = new DropboxSetupWindow
        {
            Owner = this
        };
        setup.ShowDialog();
    }

    private async void Login_Click(object sender, RoutedEventArgs e) => await TryLoginAsync();

    private async void PasswordBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await TryLoginAsync();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void ForceReleaseLock_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (!AppServices.Dropbox.Settings.IsConnected)
        {
            ErrorText.Text = "Dropbox ist nicht verbunden.";
            return;
        }

        if (!PlanerCredentialValidator.TryValidate(UsernameBox.Text, PasswordBox.Password, out _))
        {
            ErrorText.Text = "Bitte zuerst Benutzername und Passwort eines Hauptnutzers eingeben.";
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Hängende Planer-Sperre in Dropbox wirklich freigeben?\n\n" +
            "Nur verwenden, wenn kein anderer Planer mehr offen ist (z. B. nach Absturz).",
            Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        ForceReleaseLockButton.IsEnabled = false;
        try
        {
            var session = AppServices.PlanerSession;
            if (session is null)
            {
                ErrorText.Text = "Sperre kann im Planer nicht freigegeben werden.";
                return;
            }

            await session.ForceClearLockAsync().ConfigureAwait(true);
            LockWarningText.Visibility = Visibility.Collapsed;
            ForceReleaseLockButton.Visibility = Visibility.Collapsed;
            ErrorText.Text = "Sperre freigegeben – jetzt anmelden.";
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Sperre freigeben fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            ForceReleaseLockButton.IsEnabled = true;
        }
    }

    private async Task TryLoginAsync()
    {
        ErrorText.Text = string.Empty;
        LoginButton.IsEnabled = false;

        try
        {
            if (!AppServices.Dropbox.Settings.IsConnected)
            {
                ErrorText.Text = "Dropbox ist nicht verbunden. Bitte zuerst „Dropbox einrichten…“ öffnen.";
                return;
            }

            var session = AppServices.PlanerSession;
            if (session is null)
            {
                ErrorText.Text = "Anmeldung im Planer nicht verfügbar.";
                return;
            }

            var result = await session.TryLoginAsync(UsernameBox.Text, PasswordBox.Password);
            if (!result.Success)
            {
                ErrorText.Text = result.Message;
                return;
            }

            DialogResult = true;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }
}
