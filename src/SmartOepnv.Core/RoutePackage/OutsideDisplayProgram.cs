using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Eintrag in <c>outsideDisplays</c> (Pipe-Format wie GPSAnsagen SharedPreferences „programs“).
/// </summary>
public sealed class OutsideDisplayProgram : INotifyPropertyChanged
{
    private bool _isListEnabled = true;
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _frontLine1 = string.Empty;
    private string _frontLine2 = string.Empty;
    private string _sideLine1 = string.Empty;
    private string _sideLine2 = string.Empty;
    private bool _isStartTarget;

    public OutsideDisplayProgram()
    {
        _id = OutsideDisplayId.NewId();
        for (var i = 0; i < OutsideDisplayCycleParser.MaxCycles; i++)
        {
            var front = new OutsideDisplayTextCycle();
            var side = new OutsideDisplayTextCycle();
            front.PropertyChanged += (_, _) => OnCycleChanged();
            side.PropertyChanged += (_, _) => OnCycleChanged();
            FrontCycles.Add(front);
            SideCycles.Add(side);
        }
    }

    /// <summary>Stabile Ziel-ID (überlebt Umbenennungen).</summary>
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, OutsideDisplayId.Ensure(value));
    }

    /// <summary>Wechseltext 1–4 (Front), wie Slider im Handy-Dialog.</summary>
    public IList<OutsideDisplayTextCycle> FrontCycles { get; } = [];

    /// <summary>Wechseltext 1–4 (Seite); leer = Front übernehmen.</summary>
    public IList<OutsideDisplayTextCycle> SideCycles { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnCycleChanged()
    {
        SyncLegacyLinesFromCycles();
        OnPropertyChanged(nameof(FrontPreview));
        OnPropertyChanged(nameof(SidePreview));
        OnPropertyChanged(nameof(WechseltextPreview));
        OnPropertyChanged(nameof(WechseltextCount));
    }

    private void SyncLegacyLinesFromCycles()
    {
        var firstFront = FrontCycles.FirstOrDefault();
        var firstSide = SideCycles.FirstOrDefault();
        _frontLine1 = firstFront?.Line1 ?? string.Empty;
        _frontLine2 = firstFront?.Line2 ?? string.Empty;
        _sideLine1 = firstSide?.Line1 ?? string.Empty;
        _sideLine2 = firstSide?.Line2 ?? string.Empty;
    }

    private void SyncCyclesFromLegacyLines()
    {
        if (FrontCycles.Count == 0)
        {
            return;
        }

        FrontCycles[0].SetFromPair(_frontLine1, _frontLine2);
        SideCycles[0].SetFromPair(_sideLine1, _sideLine2);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value, nameof(DisplayLabel));
    }

    public string FrontLine1
    {
        get => _frontLine1;
        set => SetProperty(ref _frontLine1, value, nameof(FrontPreview));
    }

    public string FrontLine2
    {
        get => _frontLine2;
        set => SetProperty(ref _frontLine2, value, nameof(FrontPreview));
    }

    public string SideLine1
    {
        get => _sideLine1;
        set => SetProperty(ref _sideLine1, value, nameof(SidePreview));
    }

    public string SideLine2
    {
        get => _sideLine2;
        set => SetProperty(ref _sideLine2, value, nameof(SidePreview));
    }

    public int IntervalSeconds { get; set; } = 3;
    public string Ds001Type { get; set; } = "line";
    public string Ds001Value { get; set; } = "001";
    public string Ds001Spec { get; set; } = "E00";
    public string ControlCodes { get; set; } = string.Empty;
    public bool UseZa4 { get; set; } = true;
    public bool UseZa5 { get; set; }
    public bool IsListEnabled
    {
        get => _isListEnabled;
        set
        {
            if (_isListEnabled == value)
            {
                return;
            }

            _isListEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsStartTarget
    {
        get => _isStartTarget;
        set => SetProperty(ref _isStartTarget, value);
    }

    private OutsideDisplayProtocolKind _protocol = OutsideDisplayProtocolKind.Ds021T;

    public OutsideDisplayProtocolKind Protocol
    {
        get => _protocol;
        set => SetProtocol(value);
    }

    public bool IsKrefeld
    {
        get => Protocol == OutsideDisplayProtocolKind.Ds003aKrefeld;
        set => SetProtocol(value ? OutsideDisplayProtocolKind.Ds003aKrefeld : OutsideDisplayProtocolKind.Ds021T);
    }

    public bool IsDs021Neu
    {
        get => Protocol == OutsideDisplayProtocolKind.Ds021Neu;
        set => SetProtocol(value ? OutsideDisplayProtocolKind.Ds021Neu : OutsideDisplayProtocolKind.Ds021T);
    }

    public bool IsFmaS1
    {
        get => Protocol == OutsideDisplayProtocolKind.FmaS1;
        set => SetProtocol(value ? OutsideDisplayProtocolKind.FmaS1 : OutsideDisplayProtocolKind.Ds021T);
    }

    public bool IsZielnummer
    {
        get => Protocol == OutsideDisplayProtocolKind.Zielnummer;
        set => SetProtocol(value ? OutsideDisplayProtocolKind.Zielnummer : OutsideDisplayProtocolKind.Ds021T);
    }

    public bool IsDs021T => Protocol == OutsideDisplayProtocolKind.Ds021T;

    /// <summary>DS021T und FMA-S1: bis zu 4 Wechseltexte; DS021neu/Krefeld: ein Ziel.</summary>
    public bool UsesCycleEditor =>
        Protocol is OutsideDisplayProtocolKind.Ds021T or OutsideDisplayProtocolKind.FmaS1;

    private void SetProtocol(OutsideDisplayProtocolKind value)
    {
        if (_protocol == value)
        {
            return;
        }

        _protocol = value;
        OnPropertyChanged(nameof(Protocol));
        OnPropertyChanged(nameof(IsKrefeld));
        OnPropertyChanged(nameof(IsDs021Neu));
        OnPropertyChanged(nameof(IsFmaS1));
        OnPropertyChanged(nameof(IsZielnummer));
        OnPropertyChanged(nameof(IsDs021T));
        OnPropertyChanged(nameof(UsesCycleEditor));
        OnPropertyChanged(nameof(ProtocolLabel));
        OnPropertyChanged(nameof(DisplayLabel));
    }

    public Ds021NeuFontControl FontControl { get; set; } = Ds021NeuFontControl.Default;

    public string FontLine1Weight
    {
        get => FontControl.Line1Weight == Ds021NeuFontControl.Weight.Bold ? "Fett" : "Normal";
        set
        {
            FontControl.Line1Weight = string.Equals(value, "Fett", StringComparison.OrdinalIgnoreCase)
                ? Ds021NeuFontControl.Weight.Bold
                : Ds021NeuFontControl.Weight.Normal;
            OnPropertyChanged();
        }
    }

    public int FontLine1Height
    {
        get => FontControl.Line1Height;
        set
        {
            FontControl.Line1Height = Math.Clamp(value, 0, 9);
            OnPropertyChanged();
        }
    }

    public string FontLine2Weight
    {
        get => FontControl.Line2Weight == Ds021NeuFontControl.Weight.Bold ? "Fett" : "Normal";
        set
        {
            FontControl.Line2Weight = string.Equals(value, "Fett", StringComparison.OrdinalIgnoreCase)
                ? Ds021NeuFontControl.Weight.Bold
                : Ds021NeuFontControl.Weight.Normal;
            OnPropertyChanged();
        }
    }

    public int FontLine2Height
    {
        get => FontControl.Line2Height;
        set
        {
            FontControl.Line2Height = Math.Clamp(value, 0, 9);
            OnPropertyChanged();
        }
    }

    public string DisplayLabel => Protocol switch
    {
        OutsideDisplayProtocolKind.Ds003aKrefeld => $"{Name} (DS003a Krefeld)",
        OutsideDisplayProtocolKind.Ds021Neu => $"{Name} (DS021neu)",
        OutsideDisplayProtocolKind.FmaS1 => $"{Name} (FMA-S1)",
        OutsideDisplayProtocolKind.Zielnummer => $"{Name} (Zielnummer)",
        _ => $"{Name} (DS021T)"
    };

    public string ProtocolLabel => Protocol switch
    {
        OutsideDisplayProtocolKind.Ds003aKrefeld => "DS003a Krefeld",
        OutsideDisplayProtocolKind.Ds021Neu => "DS021neu",
        OutsideDisplayProtocolKind.FmaS1 => "FMA-S1",
        OutsideDisplayProtocolKind.Zielnummer => "Zielnummer",
        _ => "DS021T"
    };

    /// <summary>DS021T → DS021neu → DS003a, innerhalb der Gruppe Startziel zuerst, dann Name.</summary>
    public static int CompareForZielliste(OutsideDisplayProgram? left, OutsideDisplayProgram? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        var protocolOrder = left.Protocol.CompareTo(right.Protocol);
        if (protocolOrder != 0)
        {
            return protocolOrder;
        }

        var leftStart = IsStartzielEntry(left);
        var rightStart = IsStartzielEntry(right);
        if (leftStart != rightStart)
        {
            return leftStart ? -1 : 1;
        }

        return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool IsStartzielEntry(OutsideDisplayProgram program) =>
        program.IsStartTarget ||
        string.Equals(program.Name, "Startziel", StringComparison.Ordinal);

    public string FrontPreview =>
        BuildCyclesPreview(FrontCycles, fallbackSingle: string.IsNullOrWhiteSpace(FrontLine2)
            ? FrontLine1
            : $"{FrontLine1} · {FrontLine2}");

    public string SidePreview =>
        FormatWechseltextListPreview(SideCycles, fallbackSingle:
            string.IsNullOrWhiteSpace(SideLine1) && string.IsNullOrWhiteSpace(SideLine2)
                ? "—"
                : string.IsNullOrWhiteSpace(SideLine2)
                    ? SideLine1
                    : $"{SideLine1} · {SideLine2}");

    public int WechseltextCount =>
        FrontCycles.Count(c => c.HasContent);

    public string WechseltextPreview =>
        FormatWechseltextListPreview(FrontCycles, fallbackSingle: FrontPreview);

    private static string FormatWechseltextListPreview(
        IEnumerable<OutsideDisplayTextCycle> cycles,
        string fallbackSingle)
    {
        var active = cycles.Where(c => c.HasContent).Select(c => c.Preview).ToList();
        return active.Count switch
        {
            0 => fallbackSingle,
            1 => active[0],
            _ => $"{active.Count} Wechseltexte: {string.Join(" → ", active)}"
        };
    }

    private static string BuildCyclesPreview(IEnumerable<OutsideDisplayTextCycle> cycles, string fallbackSingle)
    {
        var active = cycles.Where(c => c.HasContent).Select(c => c.Preview).ToList();
        return active.Count switch
        {
            0 => "—",
            1 => active[0],
            _ => string.Join(" → ", active)
        };
    }

    public static OutsideDisplayProgram CreateDs021t(string? name = null) =>
        new()
        {
            Id = OutsideDisplayId.NewId(),
            Name = name ?? "Neues Ziel",
            FrontLine1 = string.Empty,
            Ds001Type = "line",
            Ds001Value = "001",
            Ds001Spec = "E00",
            IntervalSeconds = 3,
            Protocol = OutsideDisplayProtocolKind.Ds021T
        };

    public static OutsideDisplayProgram CreateDs021Neu(string? name = null) =>
        new()
        {
            Id = OutsideDisplayId.NewId(),
            Name = name ?? "Neues Ziel",
            FrontLine1 = string.Empty,
            Ds001Type = "line",
            Ds001Value = "001",
            Ds001Spec = "E00",
            Protocol = OutsideDisplayProtocolKind.Ds021Neu
        };

    public static OutsideDisplayProgram CreateFmaS1(string? name = null) =>
        new()
        {
            Id = OutsideDisplayId.NewId(),
            Name = name ?? "Neues Ziel",
            FrontLine1 = string.Empty,
            Ds001Type = "line",
            Ds001Value = "001",
            Ds001Spec = "E00",
            Protocol = OutsideDisplayProtocolKind.FmaS1
        };

    public static OutsideDisplayProgram CreateZielnummer(string? name = null) =>
        new()
        {
            Id = OutsideDisplayId.NewId(),
            Name = name ?? "Neues Ziel",
            FrontLine1 = "001",
            Ds001Type = "line",
            Ds001Value = "001",
            Ds001Spec = "E00",
            Protocol = OutsideDisplayProtocolKind.Zielnummer
        };

    public static OutsideDisplayProgram CreateKrefeld(string? name = null) =>
        new()
        {
            Id = OutsideDisplayId.NewId(),
            Name = name ?? "Neues Ziel",
            Ds001Type = "line",
            Ds001Value = "001",
            Ds001Spec = "E00",
            UseZa4 = true,
            Protocol = OutsideDisplayProtocolKind.Ds003aKrefeld
        };

    public static OutsideDisplayProgram? TryParse(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return null;
        }

        var parts = entry.Split('|');
        if (parts.Length < 3)
        {
            return null;
        }

        var program = new OutsideDisplayProgram
        {
            Name = parts[0]
        };

        var frontLog = DecodeUtf8(parts.ElementAtOrDefault(3));
        var sideLog = DecodeUtf8(parts.ElementAtOrDefault(4));
        byte[]? frontBytes = null;
        byte[]? sideBytes = null;
        try
        {
            var frontB64 = parts.ElementAtOrDefault(1);
            if (!string.IsNullOrWhiteSpace(frontB64))
            {
                frontBytes = Convert.FromBase64String(frontB64);
            }

            var sideB64 = parts.ElementAtOrDefault(2);
            if (!string.IsNullOrWhiteSpace(sideB64))
            {
                sideBytes = Convert.FromBase64String(sideB64);
            }
        }
        catch
        {
            // Telegramm-Bytes optional für Zyklus-Parsing
        }

        OutsideDisplayCycleParser.ApplyToCycles(program.FrontCycles, frontLog, frontBytes);
        OutsideDisplayCycleParser.ApplyToCycles(program.SideCycles, sideLog, sideBytes);
        program.SyncLegacyLinesFromCycles();

        if (program.FrontCycles.All(c => !c.HasContent))
        {
            ApplyLogLines(program, frontLog, isFront: true);
            ApplyLogLines(program, sideLog, isFront: false);
            program.SyncCyclesFromLegacyLines();
        }

        var ds001Type = DecodeUtf8(parts.ElementAtOrDefault(5));
        var ds001Value = DecodeUtf8(parts.ElementAtOrDefault(6));
        if (string.Equals(ds001Type, "special", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ds001Value))
        {
            program.Ds001Spec = ds001Value.Trim().ToUpperInvariant();
            program.Ds001Type = "line";
            program.Ds001Value = "001";
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(ds001Type))
            {
                program.Ds001Type = ds001Type;
            }

            if (!string.IsNullOrWhiteSpace(ds001Value))
            {
                program.Ds001Value = ds001Value;
            }
        }

        if (parts.Length >= 8 && bool.TryParse(parts[7], out var listEnabled))
        {
            program.IsListEnabled = listEnabled;
        }
        else if (parts.Length >= 6 && parts.Length < 8 && bool.TryParse(parts[5], out var legacyListEnabled))
        {
            program.IsListEnabled = legacyListEnabled;
        }

        if (parts.Length >= 9)
        {
            program.Ds001Spec = DecodeUtf8(parts[8]);
            if (string.IsNullOrWhiteSpace(program.Ds001Spec))
            {
                program.Ds001Spec = "E00";
            }
        }

        program.Protocol = InferProtocol(frontBytes, sideBytes, parts);
        var storedId = OutsideDisplayId.FromStorageEntry(entry);
        program.Id = OutsideDisplayId.IsValid(storedId)
            ? storedId
            : OutsideDisplayId.LegacyStable(program.Name, program.Protocol);
        if (program.Protocol == OutsideDisplayProtocolKind.FmaS1)
        {
            FmaS1CycleLog.ApplyToCycles(program.FrontCycles, frontLog);
            FmaS1CycleLog.ApplyToCycles(program.SideCycles, sideLog);
            program.SyncLegacyLinesFromCycles();
        }

        if (parts.Length >= 12)
        {
            program.FontControl = Ds021NeuFontControl.ParseStored(DecodeUtf8(parts[11]));
        }

        if (string.Equals(program.Name, "Startziel", StringComparison.Ordinal))
        {
            program.IsStartTarget = true;
        }

        return program;
    }

    public void ApplyStartTargetName()
    {
        if (IsStartTarget)
        {
            Name = "Startziel";
        }
        else if (string.Equals(Name, "Startziel", StringComparison.Ordinal))
        {
            Name = "Neues Ziel";
        }
    }

    public string ToStorageEntry()
    {
        ApplyStartTargetName();

        var (frontBytes, sideBytes) = Protocol switch
        {
            OutsideDisplayProtocolKind.Ds003aKrefeld => OutsideDisplayTelegramFactory.BuildKrefeldTelegrams(this),
            OutsideDisplayProtocolKind.Ds021Neu => OutsideDisplayTelegramFactory.BuildDs021NeuTelegrams(this),
            OutsideDisplayProtocolKind.FmaS1 => OutsideDisplayTelegramFactory.BuildFmaS1Telegrams(this),
            OutsideDisplayProtocolKind.Zielnummer => OutsideDisplayTelegramFactory.BuildZielnummerTelegrams(this),
            _ => OutsideDisplayTelegramFactory.BuildDs021tTelegrams(this)
        };

        return BuildEntry(frontBytes, sideBytes);
    }

    private string BuildEntry(byte[] frontBytes, byte[] sideBytes)
    {
        IReadOnlyList<(string Line1, string Line2)> frontGoals;
        IReadOnlyList<(string Line1, string Line2)> sideGoals;
        if (Protocol == OutsideDisplayProtocolKind.Ds021Neu)
        {
            frontGoals = [(FrontLine1, FrontLine2)];
            sideGoals = string.IsNullOrWhiteSpace(SideLine1) && string.IsNullOrWhiteSpace(SideLine2)
                ? frontGoals
                : [(SideLine1, SideLine2)];
        }
        else if (Protocol == OutsideDisplayProtocolKind.FmaS1)
        {
            frontGoals = OutsideDisplayCycleParser.CollectFrontGoals(FrontCycles);
            if (frontGoals.Count == 0)
            {
                frontGoals = [(FrontLine1, FrontLine2)];
            }

            sideGoals = OutsideDisplayCycleParser.CollectSideGoals(SideCycles, frontGoals);
        }
        else if (Protocol == OutsideDisplayProtocolKind.Zielnummer)
        {
            frontGoals = [(FrontLine1, string.Empty)];
            sideGoals = frontGoals;
        }
        else
        {
            frontGoals = OutsideDisplayCycleParser.CollectFrontGoals(FrontCycles);
            if (frontGoals.Count == 0)
            {
                frontGoals = [(FrontLine1, FrontLine2)];
            }

            sideGoals = OutsideDisplayCycleParser.CollectSideGoals(SideCycles, frontGoals);
        }

        var frontLog = Protocol switch
        {
            OutsideDisplayProtocolKind.FmaS1 => FmaS1CycleLog.Encode(frontGoals),
            OutsideDisplayProtocolKind.Zielnummer => OutsideDisplayTelegramFactory.NormalizeZielnummer(FrontLine1),
            _ => OutsideDisplayCycleParser.BuildLogString(frontGoals)
        };
        var sideLog = Protocol switch
        {
            OutsideDisplayProtocolKind.FmaS1 => FmaS1CycleLog.Encode(sideGoals),
            OutsideDisplayProtocolKind.Zielnummer => OutsideDisplayTelegramFactory.NormalizeZielnummer(FrontLine1),
            _ => OutsideDisplayCycleParser.BuildLogString(sideGoals)
        };

        var parts = new List<string>
        {
            Name,
            EncodeBytes(frontBytes),
            EncodeBytes(sideBytes),
            EncodeUtf8(frontLog),
            EncodeUtf8(sideLog),
            EncodeUtf8("line"),
            EncodeUtf8(NormalizeKrefeldLine(Ds001Value)),
            IsListEnabled.ToString().ToLowerInvariant(),
            EncodeUtf8(NormalizeKrefeldSpec(Ds001Spec))
        };

        while (parts.Count < 12)
        {
            parts.Add(string.Empty);
        }

        var fontStored = Ds021NeuFontControl.EncodeStored(FontControl);
        if (!string.IsNullOrEmpty(fontStored))
        {
            parts[11] = EncodeUtf8(fontStored);
        }

        parts.Add(EncodeUtf8(ProtocolStorageTag(Protocol)));
        parts.Add(EncodeUtf8(OutsideDisplayId.Ensure(Id)));
        return string.Join('|', parts);
    }

    public static string ProtocolStorageTag(OutsideDisplayProtocolKind protocol) => protocol switch
    {
        OutsideDisplayProtocolKind.Ds003aKrefeld => "DS003a_Krefeld",
        OutsideDisplayProtocolKind.Ds021Neu => "DS021neu",
        OutsideDisplayProtocolKind.FmaS1 => "FMA-S1",
        OutsideDisplayProtocolKind.Zielnummer => "Zielnummer",
        _ => "DS021T"
    };

    private static OutsideDisplayProtocolKind InferProtocol(byte[]? frontBytes, byte[]? sideBytes, string[] parts)
    {
        var tag = DecodeUtf8(parts.ElementAtOrDefault(12));
        if (string.Equals(tag, "DS003a_Krefeld", StringComparison.OrdinalIgnoreCase))
        {
            return OutsideDisplayProtocolKind.Ds003aKrefeld;
        }

        if (string.Equals(tag, "DS021neu", StringComparison.OrdinalIgnoreCase))
        {
            return OutsideDisplayProtocolKind.Ds021Neu;
        }

        if (string.Equals(tag, "FMA-S1", StringComparison.OrdinalIgnoreCase))
        {
            return OutsideDisplayProtocolKind.FmaS1;
        }

        if (string.Equals(tag, "Zielnummer", StringComparison.OrdinalIgnoreCase))
        {
            return OutsideDisplayProtocolKind.Zielnummer;
        }

        if (string.Equals(tag, "DS021T", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag, "DS021", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tag, "DS003a_UESTRA", StringComparison.OrdinalIgnoreCase))
        {
            return OutsideDisplayProtocolKind.Ds021T;
        }

        foreach (var bytes in new[] { frontBytes, sideBytes })
        {
            if (bytes is null or { Length: 0 })
            {
                continue;
            }

            var ascii = Encoding.ASCII.GetString(bytes);
            if (ascii.Contains("zA4", StringComparison.Ordinal) ||
                ascii.Contains("zA5", StringComparison.Ordinal))
            {
                return OutsideDisplayProtocolKind.Ds003aKrefeld;
            }

            if (Ds021NeuProgramBuilder.IsDs021NeuPayloadAscii(ascii))
            {
                return OutsideDisplayProtocolKind.Ds021Neu;
            }

            if (FmaS1ProgramBuilder.IsFmaS1PayloadAscii(ascii))
            {
                return OutsideDisplayProtocolKind.FmaS1;
            }

            if (Regex.IsMatch(ascii, @"^z[0-9]") &&
                !ascii.StartsWith("zA4", StringComparison.Ordinal) &&
                !ascii.StartsWith("zA5", StringComparison.Ordinal))
            {
                return OutsideDisplayProtocolKind.Zielnummer;
            }

            if (ascii.Contains("aA", StringComparison.Ordinal))
            {
                return OutsideDisplayProtocolKind.Ds021T;
            }
        }

        return OutsideDisplayProtocolKind.Ds021T;
    }

    private static string NormalizeKrefeldLine(string value)
    {
        var raw = value.Trim().ToUpperInvariant();
        return Regex.IsMatch(raw, @"^[0-9]{1,3}$") ? raw.PadLeft(3, '0') : "001";
    }

    private static string NormalizeKrefeldSpec(string value)
    {
        var raw = value.Trim().ToUpperInvariant();
        return Regex.IsMatch(raw, @"^[A-Z][0-9]{2}$") ? raw : "E00";
    }

    private static void ApplyLogLines(OutsideDisplayProgram program, string log, bool isFront)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return;
        }

        var lines = log.Split('\n');
        var l1 = lines.Length > 0 ? lines[0].Trim() : string.Empty;
        var l2 = lines.Length > 1 ? lines[1].Trim() : string.Empty;
        if (isFront)
        {
            program.FrontLine1 = l1;
            program.FrontLine2 = l2;
        }
        else
        {
            program.SideLine1 = l1;
            program.SideLine2 = l2;
        }
    }

    private static string BuildLogString(string line1, string line2) =>
        string.IsNullOrWhiteSpace(line2) ? line1 : $"{line1}\n{line2}";

    private static string DecodeUtf8(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64))
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string EncodeBytes(byte[] bytes) =>
        Convert.ToBase64String(bytes);

    private static string EncodeUtf8(string text) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetProperty<T>(ref T field, T value, params string[] additionalPropertyNames)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged();
        foreach (var name in additionalPropertyNames)
        {
            OnPropertyChanged(name);
        }
    }

    public void RefreshListDisplayProperties()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(FrontPreview));
        OnPropertyChanged(nameof(SidePreview));
        OnPropertyChanged(nameof(WechseltextPreview));
        OnPropertyChanged(nameof(WechseltextCount));
        OnPropertyChanged(nameof(ProtocolLabel));
        OnPropertyChanged(nameof(Protocol));
        OnPropertyChanged(nameof(IsDs021Neu));
        OnPropertyChanged(nameof(IsFmaS1));
        OnPropertyChanged(nameof(IsZielnummer));
        OnPropertyChanged(nameof(IsDs021T));
        OnPropertyChanged(nameof(UsesCycleEditor));
        OnPropertyChanged(nameof(IsKrefeld));
        OnPropertyChanged(nameof(IsListEnabled));
    }
}
