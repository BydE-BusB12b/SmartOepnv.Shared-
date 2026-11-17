using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core;
using SmartOepnv.Core.Session;

namespace SmartOepnv.AppShared.Views;

public partial class LoginWindow : Window
{
    private static readonly SolidColorBrush LedFree = Freeze(Color.FromRgb(0x43, 0xA0, 0x47));
    private static readonly SolidColorBrush LedBusy = Freeze(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly SolidColorBrush LedUnknown = Freeze(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly SolidColorBrush TextFree = Freeze(Color.FromRgb(0xA5, 0xD6, 0xA7));
    private static readonly SolidColorBrush TextBusy = Freeze(Color.FromRgb(0xEF, 0x9A, 0x9A));
    private static readonly SolidColorBrush TextUnknown = Freeze(Color.FromRgb(0xBD, 0xBD, 0xBD));

    private readonly DispatcherTimer _sessionLedTimer;
    private int _sessionLedBusy;

    public string? PendingUsername { get; private set; }

    public string? PendingPassword { get; private set; }

    public LoginWindow(string? lockWarning = null)
    {
        InitializeComponent();
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
        if (!string.IsNullOrWhiteSpace(lockWarning))
        {
            LockWarningText.Text = lockWarning;
            LockWarningText.Visibility = Visibility.Visible;
            ForceReleaseLockButton.Visibility = Visibility.Visible;
            PaintSessionLed(PlanerSessionAvailability.InUseByOther, activeUser: null);
        }

        _sessionLedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _sessionLedTimer.Tick += async (_, _) => await RefreshSessionLedAsync();
        Loaded += async (_, _) =>
        {
            UsernameBox.Focus();
            await RefreshSessionLedAsync();
            _sessionLedTimer.Start();
        };
        Closed += (_, _) => _sessionLedTimer.Stop();
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
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
            await RefreshSessionLedAsync();
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

    private Task TryLoginAsync()
    {
        ErrorText.Text = string.Empty;
        LoginButton.IsEnabled = false;

        try
        {
            if (!AppServices.Dropbox.Settings.IsConnected)
            {
                ErrorText.Text = "Dropbox ist nicht verbunden. Bitte zuerst „Dropbox einrichten…“ öffnen.";
                return Task.CompletedTask;
            }

            if (AppServices.PlanerSession is null)
            {
                ErrorText.Text = "Anmeldung im Planer nicht verfügbar.";
                return Task.CompletedTask;
            }

            if (!PlanerCredentialValidator.TryValidate(UsernameBox.Text, PasswordBox.Password, out var authenticatedName))
            {
                ErrorText.Text =
                    "Benutzername oder Passwort ist falsch.\n\n" +
                    "Anmeldung nur für Hauptnutzer mit Planerpasswort (Personalverwaltung).";
                return Task.CompletedTask;
            }

            PendingUsername = authenticatedName;
            PendingPassword = PasswordBox.Password;
            DialogResult = true;
            return Task.CompletedTask;
        }
        finally
        {
            LoginButton.IsEnabled = SessionLed.Background != LedBusy;
        }
    }

    private async Task RefreshSessionLedAsync()
    {
        if (Interlocked.Exchange(ref _sessionLedBusy, 1) == 1)
        {
            return;
        }

        try
        {
            if (!IsLoaded && !IsVisible)
            {
                return;
            }

            if (!AppServices.Dropbox.Settings.IsConnected || AppServices.PlanerSession is null)
            {
                PaintSessionLed(PlanerSessionAvailability.DropboxUnavailable, activeUser: null);
                return;
            }

            var (availability, activeUser) = await AppServices.PlanerSession
                .InspectLockAsync()
                .ConfigureAwait(true);
            if (!IsLoaded)
            {
                return;
            }

            PaintSessionLed(availability, activeUser);
        }
        catch
        {
            if (IsLoaded)
            {
                PaintSessionLed(PlanerSessionAvailability.DropboxUnavailable, activeUser: null);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sessionLedBusy, 0);
        }
    }

    private void PaintSessionLed(PlanerSessionAvailability availability, string? activeUser)
    {
        switch (availability)
        {
            case PlanerSessionAvailability.Available:
                SessionLed.Background = LedFree;
                SessionStatusText.Foreground = TextFree;
                SessionStatusText.Text = "Frei";
                LockWarningText.Visibility = Visibility.Collapsed;
                ForceReleaseLockButton.Visibility = Visibility.Collapsed;
                LoginButton.IsEnabled = true;
                break;
            case PlanerSessionAvailability.InUseByOther:
                SessionLed.Background = LedBusy;
                SessionStatusText.Foreground = TextBusy;
                SessionStatusText.Text = string.IsNullOrWhiteSpace(activeUser)
                    ? "Besetzt"
                    : $"Besetzt – {activeUser.Trim()}";
                LockWarningText.Text =
                    "Planer gesperrt – anderer Nutzer ist angemeldet" +
                    (string.IsNullOrWhiteSpace(activeUser) ? "." : $": {activeUser.Trim()}.") +
                    " Bitte dort abmelden oder Sperre freigeben.";
                LockWarningText.Visibility = Visibility.Visible;
                ForceReleaseLockButton.Visibility = Visibility.Visible;
                LoginButton.IsEnabled = false;
                break;
            default:
                SessionLed.Background = LedUnknown;
                SessionStatusText.Foreground = TextUnknown;
                SessionStatusText.Text = "Status unbekannt";
                LoginButton.IsEnabled = true;
                break;
        }
    }
}
