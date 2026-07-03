using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Leitstelle;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class LeitstelleMessagesInboxViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly Regex LegacyMailchatFile =
        new(@"^mailchat\((\d+)\)_\d+\.json$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private CancellationTokenSource? _pollCts;
    private readonly LeitstelleInboxHistoryStore _history = new();
    private readonly Dictionary<string, RegisteredVehicleInfo> _vehicleByPhone = new(StringComparer.Ordinal);
    private bool _hasInitialSync;

    public ObservableCollection<LeitstelleInboxItemViewModel> Items { get; } = [];

    [ObservableProperty] private string statusMessage = "Warte auf MailChat/SOS aus Dropbox…";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private int unreadMailCount;

    public bool HasUnreadMail => UnreadMailCount > 0;

    public bool HasHeaderAlerts => HeaderAlertLine1 is not null || HeaderAlertLine2 is not null;

    public LeitstelleInboxItemViewModel? HeaderAlertLine1 { get; private set; }

    public LeitstelleInboxItemViewModel? HeaderAlertLine2 { get; private set; }

    /// <summary>SOS eingegangen: normalisierte Telefonnummer des Fahrzeugs.</summary>
    public event Action<string>? SosAlertRaised;

    /// <summary>Meldung angeklickt: Live-Karte mit Fahrzeug-Detail öffnen.</summary>
    public event Action<string>? OpenVehicleOnMapRequested;

    public void RefreshFromEditor()
    {
        _vehicleByPhone.Clear();
        var json = AppServices.Routes.CurrentJson;
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        foreach (var v in RegisteredVehicleInfo.ParseFromJson(json))
        {
            var key = NormalizePhone(v.PhoneNumber);
            if (key.Length == 0) continue;
            _vehicleByPhone[key] = v;
        }
    }

    public void StartMonitoring()
    {
        RefreshFromEditor();
        _ = RefreshAsync();
        StopMonitoring();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(PollInterval, token);
                    await RefreshAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Nächster Poll
                }
            }
        }, token);
    }

    public void StopMonitoring()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy || !AppServices.Dropbox.Settings.IsConnected)
        {
            return;
        }

        IsBusy = true;
        try
        {
            RefreshFromEditor();
            var files = await AppServices.Dropbox.ListMailAndSosChatFilesAsync().ConfigureAwait(false);
            var sosToRaise = new List<string>();

            foreach (var file in files)
            {
                try
                {
                    var content = await AppServices.Dropbox.DownloadNamedFileAsync(file).ConfigureAwait(false);
                    var item = ParseItem(file, content);
                    if (item is null)
                    {
                        continue;
                    }

                    if (_history.IsDismissed(item.DedupeKey))
                    {
                        continue;
                    }

                    if (!_history.Contains(item.DedupeKey))
                    {
                        item.IsUnread = _hasInitialSync && !item.IsSos;

                        if (_hasInitialSync)
                        {
                            if (item.IsSos)
                            {
                                LeitstelleMessageTonePlayer.PlaySos();
                            }
                            else
                            {
                                LeitstelleMessageTonePlayer.PlayMail();
                            }
                        }

                        if (_hasInitialSync && item.IsSos && !string.IsNullOrWhiteSpace(item.PhoneNormalized))
                        {
                            sosToRaise.Add(item.PhoneNormalized);
                        }

                        _history.Add(ToRecord(item));
                    }
                }
                catch
                {
                    // Einzelne Datei ignorieren
                }
            }

            await RunOnUiAsync(() => RebindItemsFromHistory()).ConfigureAwait(false);

            _hasInitialSync = true;

            foreach (var phone in sosToRaise.Distinct(StringComparer.Ordinal))
            {
                SosAlertRaised?.Invoke(phone);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Nachrichtenabruf fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void MarkMailAsRead()
    {
        _history.MarkAllMailRead();
        foreach (var item in Items.Where(i => !i.IsSos))
        {
            item.IsUnread = false;
        }

        UnreadMailCount = 0;
        OnPropertyChanged(nameof(HasUnreadMail));
    }

    [RelayCommand]
    public void OpenOnMap(LeitstelleInboxItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.PhoneNormalized))
        {
            StatusMessage = "Keine Telefonnummer – Live-Karte kann nicht geöffnet werden.";
            return;
        }

        OpenVehicleOnMapRequested?.Invoke(item.PhoneNormalized);
    }

    [RelayCommand]
    private void DeleteItem(LeitstelleInboxItemViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.DedupeKey))
        {
            return;
        }

        _history.Dismiss(item.DedupeKey);
        Items.Remove(item);
        UnreadMailCount = Items.Count(i => !i.IsSos && i.IsUnread);
        OnPropertyChanged(nameof(HasUnreadMail));
        UpdateHeaderAlerts();
        StatusMessage = Items.Count == 0
            ? "Keine Nachrichten in der Liste."
            : $"{Items.Count} Nachricht(en) · {Items.Count(i => i.IsSos)} SOS · {UnreadMailCount} neu.";
    }

    private void RebindItemsFromHistory()
    {
        Items.Clear();
        foreach (var rec in _history.GetActiveRecords())
        {
            Items.Add(FromRecord(rec));
        }

        UnreadMailCount = Items.Count(i => !i.IsSos && i.IsUnread);
        OnPropertyChanged(nameof(HasUnreadMail));
        UpdateHeaderAlerts();

        StatusMessage = Items.Count == 0
            ? "Keine MailChat/SOS-Nachrichten – bei neuer Meldung erscheint jede Sendung als eigene Zeile."
            : $"{Items.Count} Nachricht(en) · {Items.Count(i => i.IsSos)} SOS · {UnreadMailCount} neu.";
    }

    private LeitstelleInboxItemViewModel? ParseItem(string fileName, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var isSos = fileName.StartsWith("soschat(", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ReadString(root, "type"), "soschat", StringComparison.OrdinalIgnoreCase);
        var fallbackType = isSos ? "soschat" : "mailchat";
        var phone = NormalizePhone(ReadString(root, "phoneNumber")) switch
        {
            { Length: > 0 } p => p,
            _ => ExtractPhoneFromFileName(fileName)
        };

        var message = ReadString(root, "text");
        if (string.IsNullOrWhiteSpace(message))
        {
            message = ReadString(root, "message");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var senderName = ReadString(root, "senderName");
        var time = root.TryGetProperty("timestamp", out var t) && t.TryGetInt64(out var ts)
            ? ts
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _vehicleByPhone.TryGetValue(phone, out var vehicle);
        var vehicleName = vehicle?.Name;
        var title = !string.IsNullOrWhiteSpace(vehicleName)
            ? vehicleName
            : (!string.IsNullOrWhiteSpace(senderName) ? senderName : phone);

        var dedupeKey = $"{fileName}|{time}";

        return new LeitstelleInboxItemViewModel
        {
            DedupeKey = dedupeKey,
            FileName = fileName,
            Type = fallbackType,
            IsSos = isSos,
            IsUnread = !isSos,
            TimestampEpochMs = time,
            TimestampLabel = DateTimeOffset.FromUnixTimeMilliseconds(time).ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss"),
            PhoneNormalized = phone,
            VehicleName = string.IsNullOrWhiteSpace(title) ? "Unbekannt" : title,
            Message = message.Trim()
        };
    }

    private static LeitstelleInboxHistoryRecord ToRecord(LeitstelleInboxItemViewModel item) => new()
    {
        DedupeKey = item.DedupeKey,
        FileName = item.FileName,
        Type = item.Type,
        IsSos = item.IsSos,
        IsUnread = item.IsUnread,
        TimestampEpochMs = item.TimestampEpochMs,
        PhoneNormalized = item.PhoneNormalized,
        VehicleName = item.VehicleName,
        Message = item.Message
    };

    private static LeitstelleInboxItemViewModel FromRecord(LeitstelleInboxHistoryRecord rec) => new()
    {
        DedupeKey = rec.DedupeKey,
        FileName = rec.FileName,
        Type = rec.Type,
        IsSos = rec.IsSos,
        IsUnread = rec.IsUnread,
        TimestampEpochMs = rec.TimestampEpochMs,
        TimestampLabel = DateTimeOffset.FromUnixTimeMilliseconds(rec.TimestampEpochMs)
            .ToLocalTime()
            .ToString("dd.MM.yyyy HH:mm:ss"),
        PhoneNormalized = rec.PhoneNormalized,
        VehicleName = rec.VehicleName,
        Message = rec.Message
    };

    private static string ReadString(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var p))
        {
            return string.Empty;
        }

        return p.GetString()?.Trim() ?? string.Empty;
    }

    private static string NormalizePhone(string? raw) =>
        new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string ExtractPhoneFromFileName(string fileName)
    {
        var legacy = LegacyMailchatFile.Match(fileName);
        if (legacy.Success)
        {
            return NormalizePhone(legacy.Groups[1].Value);
        }

        var start = fileName.IndexOf('(');
        var end = fileName.IndexOf(')');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return NormalizePhone(fileName[(start + 1)..end]);
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }

    private void UpdateHeaderAlerts()
    {
        var recent = Items
            .OrderByDescending(i => i.TimestampEpochMs)
            .Take(2)
            .ToList();

        HeaderAlertLine1 = recent.ElementAtOrDefault(0);
        HeaderAlertLine2 = recent.ElementAtOrDefault(1);
        OnPropertyChanged(nameof(HeaderAlertLine1));
        OnPropertyChanged(nameof(HeaderAlertLine2));
        OnPropertyChanged(nameof(HasHeaderAlerts));
    }

    public void Dispose() => StopMonitoring();
}

public sealed partial class LeitstelleInboxItemViewModel : ObservableObject
{
    public required string DedupeKey { get; init; }

    public required string FileName { get; init; }

    public required string Type { get; init; }

    public required string VehicleName { get; init; }

    public required string PhoneNormalized { get; init; }

    public required string Message { get; init; }

    public required string TimestampLabel { get; init; }

    public required long TimestampEpochMs { get; init; }

    public required bool IsSos { get; init; }

    [ObservableProperty] private bool isUnread;

    public string HeaderDisplayText
    {
        get
        {
            var kind = IsSos ? "Unfallruf" : "MailChat";
            var text = $"{kind} · {VehicleName}: {Message.Trim()}";
            return text.Length <= 120 ? text : text[..117] + "…";
        }
    }
}
