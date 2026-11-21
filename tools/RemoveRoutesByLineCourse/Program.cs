using SmartOepnv.Core.RoutePackage;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: RemoveRoutesByLineCourse <routes_cache.json> <lineCourse> [<lineCourse>...]");
    return 1;
}

var path = args[0];
var targets = args.Skip(1)
    .Select(RouteDisplayHelper.NormalizeLineCourse)
    .Where(s => !string.IsNullOrWhiteSpace(s))
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

if (!File.Exists(path) || targets.Count == 0)
{
    Console.Error.WriteLine("Datei oder Linie/Kurs fehlt.");
    return 1;
}

var bak = path + $".bak_remove_{DateTime.Now:yyyyMMdd_HHmmss}";
File.Copy(path, bak, overwrite: false);
Console.WriteLine($"Backup: {bak}");

var json = File.ReadAllText(path);
var package = EditableRoutePackage.FromJson(json);
var before = package.RouteNames.Count;

var toRemove = package.RouteNames
    .Where(key => MatchesTarget(key, targets))
    .Distinct(StringComparer.Ordinal)
    .OrderBy(k => k, StringComparer.Ordinal)
    .ToList();

Console.WriteLine($"Zu entfernen: {toRemove.Count}");
foreach (var key in toRemove)
{
    Console.WriteLine($"  - {key}");
    package.RemoveRoute(key);
}

var orphanStops = package.StopsByRoute.Keys
    .Where(key => MatchesTarget(key, targets))
    .ToList();
foreach (var key in orphanStops)
{
    Console.WriteLine($"  - (stops) {key}");
    package.StopsByRoute.Remove(key);
}

var outJson = package.ToJson(indented: false, rebuildEmbeddedMedia: false, includeHeavyMedia: false);
File.WriteAllText(path, outJson);
Console.WriteLine(
    $"Fertig: {before} → {package.RouteNames.Count} Routen " +
    $"({toRemove.Count} Routen, {orphanStops.Count} Stop-Buckets entfernt).");
return 0;

static bool MatchesTarget(string key, HashSet<string> lineCourses)
{
    var lc = RouteDisplayHelper.NormalizeLineCourse(RouteDisplayHelper.Parse(key).LineCourse);
    if (lineCourses.Contains(lc))
    {
        return true;
    }

    foreach (var t in lineCourses)
    {
        if (key.StartsWith(t + " ", StringComparison.OrdinalIgnoreCase) ||
            key.Equals(t, StringComparison.OrdinalIgnoreCase) ||
            key.Contains($"(Linie: {t}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}
