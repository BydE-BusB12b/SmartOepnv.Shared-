using System.Text;

namespace SmartOepnv.Core.RoutePackage;

/// <summary>HTML-Fahrplantabelle wie GPSAnsagen <c>createSchedulePdf</c>.</summary>
public static class RouteScheduleHtmlExporter
{
    public static string BuildHtml(string routeName, IEnumerable<RouteStopItem> stops)
    {
        var routeStops = stops
            .Where(s => !s.IsWaypoint)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html>");
        builder.AppendLine("<head>");
        builder.AppendLine("    <meta charset=\"UTF-8\">");
        builder.AppendLine($"    <title>Fahrplan - {Escape(routeName)}</title>");
        builder.AppendLine("    <style>");
        builder.AppendLine("        body { font-family: Arial, sans-serif; margin: 20px; }");
        builder.AppendLine("        .header { text-align: center; font-size: 24px; font-weight: bold; margin-bottom: 20px; }");
        builder.AppendLine("        .route-info { font-size: 18px; font-weight: bold; margin-bottom: 15px; }");
        builder.AppendLine("        table { width: 100%; border-collapse: collapse; margin-top: 20px; }");
        builder.AppendLine("        th, td { border: 1px solid #ddd; padding: 12px; text-align: left; }");
        builder.AppendLine("        th { background-color: #f2f2f2; font-weight: bold; }");
        builder.AppendLine("        tr:nth-child(even) { background-color: #f9f9f9; }");
        builder.AppendLine("    </style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("    <div class=\"header\">Fahrplan</div>");
        builder.AppendLine($"    <div class=\"route-info\">Linie: {Escape(routeName)}</div>");
        builder.AppendLine("    <table>");
        builder.AppendLine("        <thead>");
        builder.AppendLine("            <tr>");
        builder.AppendLine("                <th>Haltestelle</th>");
        builder.AppendLine("                <th>Zeit</th>");
        builder.AppendLine("            </tr>");
        builder.AppendLine("        </thead>");
        builder.AppendLine("        <tbody>");

        foreach (var stop in routeStops)
        {
            var stopTime = string.IsNullOrWhiteSpace(stop.Time) ? "--:--" : stop.Time.Trim();
            builder.AppendLine("            <tr>");
            builder.AppendLine($"                <td>{Escape(stop.Name)}</td>");
            builder.AppendLine($"                <td>{Escape(stopTime)}</td>");
            builder.AppendLine("            </tr>");
        }

        builder.AppendLine("        </tbody>");
        builder.AppendLine("    </table>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    public static string BuildFileName(string routeName)
    {
        var safe = SanitizeFileName(routeName);
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "Route";
        }

        // Windows MAX_PATH-freundlich halten
        if (safe.Length > 120)
        {
            safe = safe[..120].TrimEnd('_', '-', '.');
        }

        return $"Fahrplan_{safe}.html";
    }

    /// <summary>Entfernt/ersetzt Zeichen, die unter Windows in Dateinamen ungültig sind.</summary>
    public static string SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (ch is ' ' or ',' or ';' or ':')
            {
                builder.Append('_');
                continue;
            }

            if (ch is '/' or '\\' or '>' or '<' or '|' or '?' or '*' or '"' or '\0')
            {
                builder.Append('-');
                continue;
            }

            if (Array.IndexOf(invalid, ch) >= 0)
            {
                builder.Append('_');
                continue;
            }

            builder.Append(ch);
        }

        var result = builder.ToString();
        while (result.Contains("__", StringComparison.Ordinal))
        {
            result = result.Replace("__", "_", StringComparison.Ordinal);
        }

        while (result.Contains("--", StringComparison.Ordinal))
        {
            result = result.Replace("--", "-", StringComparison.Ordinal);
        }

        return result.Trim('_', '-', '.');
    }

    private static string Escape(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
