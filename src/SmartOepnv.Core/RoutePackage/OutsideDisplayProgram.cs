using System.Text;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>
/// Eintrag in <c>outsideDisplays</c> (Pipe-Format wie GPSAnsagen SharedPreferences „programs“).
/// </summary>
public sealed class OutsideDisplayProgram
{
    public string Name { get; set; } = string.Empty;
    public string FrontLine1 { get; set; } = string.Empty;
    public string FrontLine2 { get; set; } = string.Empty;
    public string SideLine1 { get; set; } = string.Empty;
    public string SideLine2 { get; set; } = string.Empty;
    public int IntervalSeconds { get; set; } = 3;
    public string Ds001Type { get; set; } = "line";
    public string Ds001Value { get; set; } = "001";
    public string Ds001Spec { get; set; } = "E00";
    public string ControlCodes { get; set; } = string.Empty;
    public bool UseZa4 { get; set; } = true;
    public bool UseZa5 { get; set; }
    public bool IsListEnabled { get; set; } = true;
    public bool IsStartTarget { get; set; }
    public bool IsKrefeld { get; set; }

    public string DisplayLabel =>
        IsKrefeld ? $"{Name} (DS003a Krefeld)" : $"{Name} (DS021T)";

    public string ProtocolLabel => IsKrefeld ? "DS003a Krefeld" : "DS021T";

    public string FrontPreview =>
        string.IsNullOrWhiteSpace(FrontLine2)
            ? FrontLine1
            : $"{FrontLine1} · {FrontLine2}";

    public string SidePreview =>
        string.IsNullOrWhiteSpace(SideLine1) && string.IsNullOrWhiteSpace(SideLine2)
            ? "—"
            : string.IsNullOrWhiteSpace(SideLine2)
                ? SideLine1
                : $"{SideLine1} · {SideLine2}";

    public static OutsideDisplayProgram CreateDs021t(string? name = null) =>
        new()
        {
            Name = name ?? "Neues Ziel",
            FrontLine1 = string.Empty,
            Ds001Type = "line",
            Ds001Value = "001",
            IntervalSeconds = 3,
            IsKrefeld = false
        };

    public static OutsideDisplayProgram CreateKrefeld(string? name = null) =>
        new()
        {
            Name = name ?? "Neues Ziel",
            Ds001Type = "line",
            Ds001Value = "001",
            Ds001Spec = "E00",
            UseZa4 = true,
            IsKrefeld = true
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
            Name = parts[0],
            IsKrefeld = parts.Length >= 9
        };

        var frontLog = DecodeUtf8(parts.ElementAtOrDefault(3));
        var sideLog = DecodeUtf8(parts.ElementAtOrDefault(4));
        ApplyLogLines(program, frontLog, isFront: true);
        ApplyLogLines(program, sideLog, isFront: false);

        var ds001Type = DecodeUtf8(parts.ElementAtOrDefault(5));
        var ds001Value = DecodeUtf8(parts.ElementAtOrDefault(6));
        if (!string.IsNullOrWhiteSpace(ds001Type))
        {
            program.Ds001Type = ds001Type;
        }

        if (!string.IsNullOrWhiteSpace(ds001Value))
        {
            program.Ds001Value = ds001Value;
        }

        if (parts.Length >= 8 && bool.TryParse(parts[7], out var listEnabled))
        {
            program.IsListEnabled = listEnabled;
        }

        if (parts.Length >= 9)
        {
            program.Ds001Spec = DecodeUtf8(parts[8]);
            if (string.IsNullOrWhiteSpace(program.Ds001Spec))
            {
                program.Ds001Spec = "E00";
            }
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

        var (frontBytes, sideBytes) = IsKrefeld
            ? OutsideDisplayTelegramFactory.BuildKrefeldTelegrams(this)
            : OutsideDisplayTelegramFactory.BuildDs021tTelegrams(this);

        return BuildEntry(frontBytes, sideBytes);
    }

    private string BuildEntry(byte[] frontBytes, byte[] sideBytes)
    {
        var frontLog = BuildLogString(FrontLine1, FrontLine2);
        var sideLog = BuildLogString(SideLine1, SideLine2);
        var ds001Type = EncodeUtf8(IsKrefeld ? "line" : Ds001Type);
        var ds001Value = EncodeUtf8(
            IsKrefeld ? NormalizeKrefeldLine(Ds001Value) : Ds001Value.Trim());

        if (IsKrefeld)
        {
            return string.Join('|',
                Name,
                EncodeBytes(frontBytes),
                EncodeBytes(sideBytes),
                EncodeUtf8(frontLog),
                EncodeUtf8(sideLog),
                ds001Type,
                ds001Value,
                IsListEnabled.ToString().ToLowerInvariant(),
                EncodeUtf8(NormalizeKrefeldSpec(Ds001Spec)));
        }

        var ds001Val = Ds001Type == "line"
            ? NormalizeKrefeldLine(Ds001Value)
            : Ds001Value.Trim().ToUpperInvariant();

        return string.Join('|',
            Name,
            EncodeBytes(frontBytes),
            EncodeBytes(sideBytes),
            EncodeUtf8(frontLog),
            EncodeUtf8(sideLog),
            EncodeUtf8(Ds001Type),
            EncodeUtf8(ds001Val),
            IsListEnabled.ToString().ToLowerInvariant());
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
}
