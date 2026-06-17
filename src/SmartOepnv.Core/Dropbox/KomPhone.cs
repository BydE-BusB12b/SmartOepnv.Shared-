namespace SmartOepnv.Core.Dropbox;

public static class KomPhone
{
    public static string Normalize(string? raw) =>
        new string((raw ?? string.Empty).Where(char.IsDigit).ToArray());

    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = Normalize(raw);
        return !string.IsNullOrEmpty(normalized);
    }
}
