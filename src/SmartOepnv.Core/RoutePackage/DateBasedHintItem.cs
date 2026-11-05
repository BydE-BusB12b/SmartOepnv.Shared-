using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace SmartOepnv.Core.RoutePackage;

public sealed class DateBasedHintItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString();

    private string _name = string.Empty;

    /// <summary>Kurzname für die Auswahlliste. Steht nicht im Tablet-Popup.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListTitle)));
            }
        }
    }

    private string _hintText = string.Empty;
    public string HintText
    {
        get => _hintText;
        set
        {
            if (SetField(ref _hintText, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListTitle)));
            }
        }
    }

    /// <summary>Eine Zeile in der Liste: Name, sonst die erste Zeile des Hinweistexts.</summary>
    public string ListTitle
    {
        get
        {
            var name = Name.Trim();
            if (name.Length > 0)
            {
                return name;
            }

            var line = FirstLine(HintText);
            return line.Length > 0 ? line : "Hinweis";
        }
    }

    public static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var line = normalized.Split('\n')[0].Trim();
        return line;
    }

    private string _startDate = string.Empty;
    public string StartDate
    {
        get => _startDate;
        set => SetField(ref _startDate, value);
    }

    private string _endDate = string.Empty;
    public string EndDate
    {
        get => _endDate;
        set => SetField(ref _endDate, value);
    }

    private bool _isShown;
    public bool IsShown
    {
        get => _isShown;
        set => SetField(ref _isShown, value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public static DateBasedHintItem FromJson(JsonObject obj) =>
        new()
        {
            Id = JsonNodeReading.GetString(obj["id"], Guid.NewGuid().ToString()),
            Name = JsonNodeReading.GetString(obj["name"]),
            HintText = JsonNodeReading.GetString(obj["hintText"]),
            StartDate = JsonNodeReading.GetString(obj["startDate"]),
            EndDate = JsonNodeReading.GetString(obj["endDate"]),
            IsShown = JsonNodeReading.GetBoolean(obj["isShown"])
        };

    public JsonObject ToJson() =>
        new()
        {
            ["id"] = Id,
            ["name"] = Name,
            ["hintText"] = HintText,
            ["startDate"] = StartDate,
            ["endDate"] = EndDate,
            ["isShown"] = IsShown
        };
}

public static class DateBasedHintsEditor
{
    public static IList<DateBasedHintItem> LoadFromRoot(JsonObject root)
    {
        if (root["dateBasedHints"] is not JsonArray arr)
        {
            return [];
        }

        var list = new List<DateBasedHintItem>();
        foreach (var node in arr)
        {
            if (node is JsonObject obj)
            {
                list.Add(DateBasedHintItem.FromJson(obj));
            }
        }

        return list;
    }

    public static void SaveToRoot(JsonObject root, IList<DateBasedHintItem> hints)
    {
        if (hints.Count == 0)
        {
            root.Remove("dateBasedHints");
            return;
        }

        var arr = new JsonArray();
        foreach (var hint in hints)
        {
            arr.Add(hint.ToJson());
        }

        root["dateBasedHints"] = arr;
    }
}
