using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.Kom;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core.Voip;

namespace SmartOepnv.AppShared.Voip;

public sealed class VoipFunkDialog : Window
{
    private static readonly Brush PanelBackground = FrozenRgb(0x00, 0x21, 0x71);
    private static readonly Brush GreenBorder = FrozenRgb(0x43, 0xA0, 0x47);
    private static readonly Brush DefaultBorder = FrozenRgb(0x42, 0xA5, 0xF5);

    private readonly VoipLeitstelleHost _host;
    private readonly VehicleListItemViewModel _vehicle;
    private readonly string? _phone;
    private readonly bool _online;

    private readonly Border _chromeBorder;
    private readonly StackPanel _fullPanel;
    private readonly StackPanel _compactPanel;
    private readonly TextBlock _compactPartnerText;
    private readonly TextBlock _compactStatusText;
    private readonly Button _callButton;
    private readonly Button _hangUpButton;
    private readonly TextBlock _callStatusText;

    private bool _isCompact;
    private bool _spaceTransmitActive;
    private DispatcherTimer? _spaceReleaseTimer;

    public VoipFunkDialog(
        VehicleListItemViewModel vehicle,
        VoipLeitstelleHost host,
        Window owner,
        Func<string, string?>? resolveVehicleName = null)
    {
        _host = host;
        _vehicle = vehicle;
        Owner = owner;
        Title = "Funk";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = FrozenRgb(0x0A, 0x10, 0x20);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        var phone = VoipPhone.Normalize(vehicle.ResolvePhoneNumber());
        _phone = phone;
        var onlinePeers = host.OnlinePeers.ToList();
        var vehiclePeers = onlinePeers
            .Where(p => !string.Equals(p.Role, VoipConstants.RoleDispatch, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(p.PeerId, VoipConstants.RoleDispatch, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => ResolvePeerLabel(p, resolveVehicleName), StringComparer.OrdinalIgnoreCase)
            .ToList();
        var dispatchOnline = host.Settings.UsesCloudSignaling()
            ? host.IsSignalingReady
            : onlinePeers.Any(p =>
                string.Equals(p.Role, VoipConstants.RoleDispatch, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.PeerId, VoipConstants.RoleDispatch, StringComparison.OrdinalIgnoreCase));
        _online = !string.IsNullOrEmpty(phone) &&
                  vehiclePeers.Any(p => VoipPhone.Match(p.PeerId, phone));
        var status = host.StatusMessage ?? "—";
        var vehicleOnlineCount = vehiclePeers.Count;
        var lte = host.LteReachability;

        _fullPanel = new StackPanel();
        _fullPanel.Children.Add(VehicleKomUi.MakeText($"Funk – {vehicle.DisplayName}", 17, FontWeights.SemiBold));
        _fullPanel.Children.Add(VehicleKomUi.MakeText(
            _online ? "Status: online" : "Status: offline (Tablet-App läuft, VoIP-Config geladen, unterwegs erreichbar?)",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 8)));
        if (lte.IsRelevant)
        {
            _fullPanel.Children.Add(VehicleKomUi.MakeText(
                lte.Summary,
                13,
                muted: !lte.IsReady,
                margin: new Thickness(0, 0, 0, 8)));
        }
        _fullPanel.Children.Add(VehicleKomUi.MakeText(
            host.IsSignalingReady
                ? host.Settings.UsesCloudSignaling()
                    ? "Signaling: Cloud verbunden"
                    : "Signaling-Server: läuft auf Port 8787"
                : host.Settings.UsesCloudSignaling()
                    ? "Signaling: Cloud nicht verbunden"
                    : "Signaling-Server: nicht gestartet",
            13,
            muted: true,
            margin: new Thickness(0, 0, 0, 4)));
        _fullPanel.Children.Add(VehicleKomUi.MakeText(status, 12, muted: true, margin: new Thickness(0, 0, 0, 8)));
        _fullPanel.Children.Add(VehicleKomUi.MakeText(
            vehicleOnlineCount > 0
                ? $"Fahrzeuge online: {vehicleOnlineCount}"
                : "Fahrzeuge online: keins",
            12,
            muted: true,
            margin: new Thickness(0, 0, 0, 4)));
        _fullPanel.Children.Add(VehicleKomUi.MakeText(
            dispatchOnline
                ? "Leitstelle (intern): verbunden"
                : "Leitstelle (intern): nicht verbunden",
            12,
            muted: true,
            margin: new Thickness(0, 0, 0, vehicleOnlineCount > 0 ? 8 : 12)));

        if (vehicleOnlineCount > 0)
        {
            _fullPanel.Children.Add(VehicleKomUi.MakeText("Registrierte Fahrzeuge:", 12, FontWeights.SemiBold,
                margin: new Thickness(0, 0, 0, 4)));
            foreach (var peer in vehiclePeers)
            {
                var label = ResolvePeerLabel(peer, resolveVehicleName);
                _fullPanel.Children.Add(VehicleKomUi.MakeText(
                    $"• {label}",
                    12,
                    muted: true,
                    margin: new Thickness(8, 0, 0, 2)));
            }

            _fullPanel.Children.Add(new Border { Height = 8 });
        }

        if (!host.IsSignalingReady ||
            (!host.Settings.UsesCloudSignaling() &&
             VoipWindowsPortSetup.LooksLikeAccessDenied(status)))
        {
            var fixPort = VehicleKomUi.MakeButton(
                "VoIP-Port freigeben (Administrator)",
                primary: true,
                horizontalAlignment: HorizontalAlignment.Stretch);
            fixPort.Margin = new Thickness(0, 0, 0, 8);
            fixPort.Click += async (_, _) =>
            {
                fixPort.IsEnabled = false;
                try
                {
                    await VoipWindowsPortSetup.TryEnsurePortReadyAsync(host.Settings).ConfigureAwait(true);
                    await host.StartAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    VehicleKomUi.SafeShowMessage(this, ex.Message, Title, MessageBoxImage.Warning);
                }
                finally
                {
                    fixPort.IsEnabled = true;
                }
            };
            _fullPanel.Children.Add(fixPort);
        }

        _callButton = VehicleKomUi.MakeButton(
            "Anrufen",
            primary: true,
            horizontalAlignment: HorizontalAlignment.Stretch);
        _callButton.IsEnabled = _online && !string.IsNullOrEmpty(phone);

        _hangUpButton = VehicleKomUi.MakeButton(
            "Auflegen",
            primary: true,
            horizontalAlignment: HorizontalAlignment.Stretch);
        _hangUpButton.Margin = new Thickness(0, 0, 0, 8);
        _hangUpButton.Visibility = Visibility.Collapsed;

        _callStatusText = VehicleKomUi.MakeText(string.Empty, 13, FontWeights.SemiBold,
            margin: new Thickness(0, 8, 0, 0));
        _callStatusText.Visibility = Visibility.Collapsed;

        _callButton.Click += async (_, _) =>
        {
            try
            {
                await host.CallVehicleAsync(phone!, vehicle.DisplayName).ConfigureAwait(true);
                RefreshCallStatus();
            }
            catch (Exception ex)
            {
                VehicleKomUi.SafeShowMessage(this, ex.Message, Title, MessageBoxImage.Warning);
            }
        };
        _hangUpButton.Click += (_, _) => host.HangUpActiveCall();

        _fullPanel.Children.Add(_callButton);
        _fullPanel.Children.Add(_hangUpButton);
        _fullPanel.Children.Add(_callStatusText);

        if (!host.IsSignalingReady)
        {
            _ = RestartVoipSafeAsync(host);
        }

        var close = VehicleKomUi.MakeButton("Schließen", isCancel: true, horizontalAlignment: HorizontalAlignment.Right);
        close.Margin = new Thickness(0, 16, 0, 0);
        close.Click += (_, _) => Close();
        _fullPanel.Children.Add(close);

        _compactPartnerText = VehicleKomUi.MakeText(vehicle.DisplayName, 15, FontWeights.SemiBold);
        _compactStatusText = VehicleKomUi.MakeText("Verbindung aktiv", 12, muted: true, margin: new Thickness(0, 0, 0, 10));
        var compactHangUp = VehicleKomUi.MakeButton("Auflegen", primary: true, horizontalAlignment: HorizontalAlignment.Stretch);
        compactHangUp.Click += (_, _) => host.HangUpActiveCall();
        _compactPanel = new StackPanel();
        _compactPanel.Children.Add(_compactPartnerText);
        _compactPanel.Children.Add(_compactStatusText);
        _compactPanel.Children.Add(compactHangUp);

        _chromeBorder = new Border
        {
            Background = PanelBackground,
            Padding = new Thickness(24),
            BorderThickness = new Thickness(2),
            BorderBrush = DefaultBorder,
            Child = _fullPanel
        };
        Content = _chromeBorder;

        Focusable = true;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;

        host.CallStatusChanged += RefreshCallStatus;
        VoipFunkMapAnchor.MapBoundsChanged += OnMapBoundsChanged;
        Closed += (_, _) =>
        {
            CancelSpaceReleaseTimer();
            StopSpaceTransmitImmediate();
            host.CallStatusChanged -= RefreshCallStatus;
            VoipFunkMapAnchor.MapBoundsChanged -= OnMapBoundsChanged;
            PreviewKeyDown -= OnPreviewKeyDown;
            PreviewKeyUp -= OnPreviewKeyUp;
        };
        RefreshCallStatus();
    }

    private void RefreshCallStatus()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(RefreshCallStatus);
            return;
        }

        var callStatus = _host.CallStatus;
        var partnerName = string.IsNullOrWhiteSpace(callStatus.RemoteDisplayName)
            ? _vehicle.DisplayName
            : callStatus.RemoteDisplayName!;
        _compactPartnerText.Text = partnerName;

        if (callStatus.State == VoipCallConnectionState.Connected)
        {
            ApplyCompactLayout(true);
            UpdateConnectedStatusText();
            Focus();
            return;
        }

        StopSpaceTransmitImmediate();

        if (_isCompact)
        {
            ApplyCompactLayout(false);
        }

        if (string.IsNullOrWhiteSpace(callStatus.StatusText))
        {
            _callStatusText.Visibility = Visibility.Collapsed;
            _callStatusText.Text = string.Empty;
            _hangUpButton.Visibility = Visibility.Collapsed;
            _callButton.IsEnabled = _online && !string.IsNullOrEmpty(_phone) && !callStatus.IsActive;
            _callButton.Visibility = Visibility.Visible;
            return;
        }

        _callStatusText.Visibility = Visibility.Visible;
        _callStatusText.Text = callStatus.StatusText;
        _callStatusText.Foreground = callStatus.State switch
        {
            VoipCallConnectionState.Failed => new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
            _ => new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0))
        };
        var active = callStatus.IsActive;
        _hangUpButton.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _callButton.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        _callButton.IsEnabled = _online && !string.IsNullOrEmpty(_phone) && !active;
    }

    private void UpdateConnectedStatusText()
    {
        _compactStatusText.Text = _spaceTransmitActive
            ? "Senden… (Leertaste)"
            : "Leertaste halten zum Sprechen";
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || _host.CallStatus.State != VoipCallConnectionState.Connected)
        {
            return;
        }

        e.Handled = true;
        CancelSpaceReleaseTimer();
        if (_spaceTransmitActive)
        {
            return;
        }

        _spaceTransmitActive = true;
        _host.SendDispatchPtt(true);
        _host.SetMicrophoneTransmitEnabled(true);
        UpdateConnectedStatusText();
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space)
        {
            return;
        }

        e.Handled = true;
        ScheduleSpaceRelease();
    }

    private void ScheduleSpaceRelease()
    {
        if (!_spaceTransmitActive)
        {
            return;
        }

        CancelSpaceReleaseTimer();
        _spaceReleaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _spaceReleaseTimer.Tick += OnSpaceReleaseTimerTick;
        _spaceReleaseTimer.Start();
    }

    private void CancelSpaceReleaseTimer()
    {
        if (_spaceReleaseTimer is null)
        {
            return;
        }

        _spaceReleaseTimer.Stop();
        _spaceReleaseTimer.Tick -= OnSpaceReleaseTimerTick;
        _spaceReleaseTimer = null;
    }

    private void OnSpaceReleaseTimerTick(object? sender, EventArgs e)
    {
        CancelSpaceReleaseTimer();
        StopSpaceTransmitImmediate();
    }

    private void StopSpaceTransmitImmediate()
    {
        if (!_spaceTransmitActive)
        {
            return;
        }

        _spaceTransmitActive = false;
        _host.SendDispatchPtt(false);
        _host.SetMicrophoneTransmitEnabled(false);
        if (_host.CallStatus.State == VoipCallConnectionState.Connected)
        {
            UpdateConnectedStatusText();
        }
    }

    private void ApplyCompactLayout(bool compact)
    {
        _isCompact = compact;
        if (compact)
        {
            _chromeBorder.Child = _compactPanel;
            _chromeBorder.Padding = new Thickness(14);
            _chromeBorder.BorderBrush = GreenBorder;
            Width = 248;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            PositionOnMapTopRight();
            return;
        }

        _chromeBorder.Child = _fullPanel;
        _chromeBorder.Padding = new Thickness(24);
        _chromeBorder.BorderBrush = DefaultBorder;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        Topmost = false;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (Owner is not null)
        {
            Left = Owner.Left + (Owner.Width - ActualWidth) / 2;
            Top = Owner.Top + (Owner.Height - ActualHeight) / 2;
        }
    }

    private void OnMapBoundsChanged()
    {
        if (!_isCompact || !IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(PositionOnMapTopRight, DispatcherPriority.Background);
    }

    private void PositionOnMapTopRight()
    {
        UpdateLayout();
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : 200;
        var margin = 14.0;

        var mapRect = VoipFunkMapAnchor.TryGetMapScreenRect();
        if (mapRect is not null)
        {
            Left = mapRect.Value.Right - width - margin;
            Top = mapRect.Value.Top + margin;
            return;
        }

        if (Owner is Window owner)
        {
            Left = owner.Left + owner.ActualWidth - width - margin;
            Top = owner.Top + margin + 48;
        }
    }

    private static string ResolvePeerLabel(VoipOnlinePeerInfo peer, Func<string, string?>? resolveVehicleName)
    {
        var knownName = resolveVehicleName?.Invoke(peer.PeerId)?.Trim();
        if (!string.IsNullOrWhiteSpace(knownName))
        {
            return knownName;
        }

        if (!string.IsNullOrWhiteSpace(peer.DisplayName) && !VoipPhone.Match(peer.DisplayName, peer.PeerId))
        {
            return peer.DisplayName.Trim();
        }

        return peer.PeerId;
    }

    private static SolidColorBrush FrozenRgb(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private async Task RestartVoipSafeAsync(VoipLeitstelleHost host)
    {
        try
        {
            await host.EnsurePortAndStartAsync().ConfigureAwait(false);
            if (host.IsSignalingReady)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
                VehicleKomUi.SafeShowMessage(
                    this,
                    host.StatusMessage ?? "VoIP-Signaling konnte nicht gestartet werden.",
                    Title,
                    MessageBoxImage.Warning));
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                VehicleKomUi.SafeShowMessage(this, ex.Message, Title, MessageBoxImage.Warning));
        }
    }
}
