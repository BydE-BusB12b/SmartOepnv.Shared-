using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartOepnv.Core.Vrr;

/// <summary>
/// Lokaler VRR-Haltestellen-Katalog (stops_vrr.csv) – wie Android <see cref="VrrStopCatalog"/>.
/// </summary>
public static class VrrStopCatalog
{
    private const string CsvFileName = "stops_vrr.csv";
    private static readonly object LoadLock = new();
    private static List<VrrStopEntry>? _entries;

    public static int Size => _entries?.Count ?? 0;

    public static bool IsLoaded => _entries is not null;

    public static void EnsureLoaded()
    {
        if (_entries is not null)
        {
            return;
        }

        lock (LoadLock)
        {
            if (_entries is not null)
            {
                return;
            }

            var path = ResolveCsvPath();
            _entries = string.IsNullOrEmpty(path) ? [] : LoadFromCsv(path);
        }
    }

    public static IReadOnlyList<VrrStopEntry> Suggest(string query, int limit = 50)
    {
        EnsureLoaded();
        var list = _entries ?? [];
        var q = Normalize(query).Trim();
        if (q.Length == 0)
        {
            return [];
        }

        var queryTokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (queryTokens.Length == 0)
        {
            return [];
        }

        var queryTokenSet = queryTokens.ToHashSet(StringComparer.Ordinal);
        var scored = new List<(VrrStopEntry Entry, int Score)>(512);

        foreach (var e in list)
        {
            var key = e.SearchKey;
            var name = Normalize(e.Name);
            var place = Normalize(e.Place);

            var strictMatch = queryTokens.All(key.Contains);
            if (strictMatch)
            {
                var score = name == q ? 0 :
                    name.StartsWith(q, StringComparison.Ordinal) ? 1 :
                    place.StartsWith(q, StringComparison.Ordinal) && name.Contains(q, StringComparison.Ordinal) ? 2 :
                    place.StartsWith(q, StringComparison.Ordinal) ? 3 :
                    name.Contains(q, StringComparison.Ordinal) ? 4 : 5;
                scored.Add((e, score));
                if (scored.Count > 6000)
                {
                    break;
                }

                continue;
            }

            var nameTokens = e.NameTokens;
            if (nameTokens.Count >= 2 &&
                nameTokens.All(t => queryTokenSet.Contains(t) || q.Contains(t, StringComparison.Ordinal)))
            {
                var placeBonus = e.PlaceTokens.Count > 0 &&
                                 e.PlaceTokens.All(t => queryTokenSet.Contains(t) || q.Contains(t, StringComparison.Ordinal))
                    ? 0
                    : 2;
                scored.Add((e, 10 + placeBonus + (15 - Math.Min(15, nameTokens.Count))));
                if (scored.Count > 6000)
                {
                    break;
                }

                continue;
            }

            if (queryTokens.Length == 1 &&
                queryTokens[0].Length >= 4 &&
                queryTokens[0].All(char.IsDigit) &&
                (e.Id == queryTokens[0] || e.GlobalId.Contains(queryTokens[0], StringComparison.Ordinal)))
            {
                scored.Add((e, 8));
                if (scored.Count > 6000)
                {
                    break;
                }
            }
        }

        return scored
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Entry.Name.Length)
            .ThenBy(x => x.Entry.Name, StringComparer.Ordinal)
            .Take(Math.Max(1, limit))
            .Select(x => x.Entry)
            .ToList();
    }

    public static VrrStopEntry? FindById(string id)
    {
        var clean = id.Trim();
        if (clean.Length == 0)
        {
            return null;
        }

        EnsureLoaded();
        return _entries?.FirstOrDefault(e => e.Id == clean || e.GlobalId == clean);
    }

    private static string? ResolveCsvPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", CsvFileName),
            Path.Combine(AppContext.BaseDirectory, CsvFileName)
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static List<VrrStopEntry> LoadFromCsv(string path)
    {
        var outByKey = new Dictionary<string, VrrStopEntry>(40_000, StringComparer.Ordinal);
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.Latin1);

        var first = true;
        while (reader.ReadLine() is { } line)
        {
            if (first)
            {
                first = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var f = line.Split(';');
            if (f.Length < 17)
            {
                continue;
            }

            var stopNr = f[1].Trim();
            if (stopNr.Length == 0)
            {
                continue;
            }

            var stopName = f[3].Trim();
            var stopShort = f[4].Trim();
            var lon = double.TryParse(f[6].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lonVal)
                ? lonVal
                : (double?)null;
            var lat = double.TryParse(f[7].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latVal)
                ? latVal
                : (double?)null;
            var place = f[8].Trim();
            var globalId = f[16].Trim();
            var key = string.IsNullOrWhiteSpace(globalId) ? stopNr : globalId;

            outByKey.TryAdd(
                key,
                new VrrStopEntry(stopNr, globalId, stopName, stopShort, place, lat, lon));
        }

        return outByKey.Values.ToList();
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s.Length + 4);
        foreach (var ch in s.ToLower(CultureInfo.GetCultureInfo("de-DE")))
        {
            switch (ch)
            {
                case 'ä': sb.Append('a'); break;
                case 'ö': sb.Append('o'); break;
                case 'ü': sb.Append('u'); break;
                case 'ß': sb.Append("ss"); break;
                case 'é' or 'è' or 'ê': sb.Append('e'); break;
                case 'á' or 'à' or 'â': sb.Append('a'); break;
                case 'ó' or 'ò' or 'ô': sb.Append('o'); break;
                case '.' or ',' or '-' or '_' or '/' or '\\' or '(' or ')' or '\'' or '"': sb.Append(' '); break;
                default: sb.Append(ch); break;
            }
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}

public sealed class VrrStopEntry
{
    public VrrStopEntry(string id, string globalId, string name, string shortName, string place, double? lat, double? lon)
    {
        Id = id;
        GlobalId = globalId;
        Name = name;
        ShortName = shortName;
        Place = place;
        Lat = lat;
        Lon = lon;
        SearchKey = BuildSearchKey();
        NameTokens = Tokenize(NormalizeStatic(name));
        PlaceTokens = Tokenize(NormalizeStatic(place));
    }

    public string Id { get; }
    public string GlobalId { get; }
    public string Name { get; }
    public string ShortName { get; }
    public string Place { get; }
    public double? Lat { get; }
    public double? Lon { get; }

    internal string SearchKey { get; }
    internal IReadOnlyList<string> NameTokens { get; }
    internal IReadOnlyList<string> PlaceTokens { get; }

    public string DisplayLine =>
        !string.IsNullOrWhiteSpace(Name) ? Name.Trim() : $"{Place} {ShortName}".Trim();

    public string Subtitle =>
        string.Join(" · ", new[] { Place, $"ID {Id}", GlobalId }.Where(x => !string.IsNullOrWhiteSpace(x)));

    private string BuildSearchKey() =>
        NormalizeStatic($"{Name} {Place} {ShortName} {Id} {GlobalId}");

    private static IReadOnlyList<string> Tokenize(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 2)
            .ToList();

    private static string NormalizeStatic(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s.Length + 4);
        foreach (var ch in s.ToLower(CultureInfo.GetCultureInfo("de-DE")))
        {
            switch (ch)
            {
                case 'ä': sb.Append('a'); break;
                case 'ö': sb.Append('o'); break;
                case 'ü': sb.Append('u'); break;
                case 'ß': sb.Append("ss"); break;
                case 'é' or 'è' or 'ê': sb.Append('e'); break;
                case 'á' or 'à' or 'â': sb.Append('a'); break;
                case 'ó' or 'ò' or 'ô': sb.Append('o'); break;
                case '.' or ',' or '-' or '_' or '/' or '\\' or '(' or ')' or '\'' or '"': sb.Append(' '); break;
                default: sb.Append(ch); break;
            }
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
