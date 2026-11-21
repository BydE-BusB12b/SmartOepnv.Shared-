using System.Text.RegularExpressions;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.Fahrgeraet;

/// <summary>Routenliste wie auf dem Tablet: Linie/Kurs/Fahrt, nicht der ganze Kurs ungeordnet.</summary>
internal static class RoutenListe
{
    public static string Beschriftung(string route)
    {
        var parsed = RouteDisplayHelper.Parse(route);
        var linie = parsed.LineCourse.Trim();
        var fahrt = parsed.TripNumber.Trim();
        var name = parsed.Name.Trim();
        var prefix = linie.Length > 0 && fahrt.Length > 0
            ? linie + "/" + fahrt
            : linie.Length > 0 ? linie : fahrt;
        if (prefix.Length == 0)
        {
            return name.Length == 0 ? route : name;
        }

        return name.Length == 0 ? prefix : prefix + " " + name;
    }

    public static string Uhr(FahrSession session, string route)
    {
        var zeit = ErsteZeit(session, route);
        return string.IsNullOrWhiteSpace(zeit) ? "" : zeit;
    }

    public static int Abfahrt(FahrSession session, string route)
    {
        var zeit = ErsteZeit(session, route);
        if (string.IsNullOrWhiteSpace(zeit))
        {
            return int.MaxValue;
        }

        var teile = zeit.Split(':');
        if (teile.Length < 2
            || !int.TryParse(teile[0], out var stunde)
            || !int.TryParse(teile[1], out var minute))
        {
            return int.MaxValue;
        }

        var minuten = stunde * 60 + minute;
        return stunde < 3 ? minuten + 24 * 60 : minuten;
    }

    /// <summary>
    /// Index der nächsten Abfahrt ab jetzt. Zeiten nach Mitternacht zählen zum laufenden Abend.
    /// Ohne Uhrzeit gibt es keine Markierung.
    /// </summary>
    public static int? Naechste(FahrSession session, IReadOnlyList<string> routen)
    {
        var jetzt = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
        int? bester = null;
        var distanz = int.MaxValue;
        for (var i = 0; i < routen.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(Uhr(session, routen[i])))
            {
                continue;
            }

            var abfahrt = Abfahrt(session, routen[i]);
            if (abfahrt == int.MaxValue)
            {
                continue;
            }

            var minuten = abfahrt >= 24 * 60 ? abfahrt - 24 * 60 : abfahrt;
            var rest = minuten >= jetzt ? minuten - jetzt : minuten + 24 * 60 - jetzt;
            if (rest < distanz)
            {
                distanz = rest;
                bester = i;
            }
        }

        return bester;
    }

    public static int Fahrt(string route)
    {
        var nummer = RouteDisplayHelper.Parse(route).TripNumber.Trim();
        return int.TryParse(nummer, out var wert) ? wert : int.MaxValue;
    }

    /// <summary>
    /// Betriebstag 03:00 bis 02:59. Vergangene Tageskopien (Datum im Namen) und
    /// Fahrten außerhalb von Verkehrstag, Datumsbereich oder Betriebstagen bleiben draußen.
    /// Die gerade gewählte Route bleibt sichtbar.
    /// </summary>
    public static bool Heute(FahrSession session, string route)
    {
        if (session.RouteName is { Length: > 0 } aktiv
            && string.Equals(aktiv, route, StringComparison.Ordinal))
        {
            return true;
        }

        var paket = session.Package;
        if (paket is null)
        {
            return false;
        }

        var tag = Betriebstag(DateTime.Now);
        if (!DatumsPraefixPasst(route, tag))
        {
            return false;
        }

        if (!paket.GetRouteDateRange(route).Contains(tag))
        {
            return false;
        }

        if (!RouteOperatingDatesEditor.Contains(paket.GetRouteOperatingDates(route), tag))
        {
            return false;
        }

        var verkehr = paket.GetRouteOperatingDays(route);
        return RouteOperatingDaysEditor.EffectiveDaySet(verkehr)
            .Contains(DutyOperatingDayHelper.FromDate(tag));
    }

    public static bool GleicheLinie(string route, string linie)
    {
        var vorhanden = Schluessel(RouteDisplayHelper.Parse(route).LineCourse);
        var gesucht = Schluessel(linie);
        return vorhanden.Linie == gesucht.Linie && vorhanden.Kurs == gesucht.Kurs;
    }

    public static IEnumerable<string> Sortieren(
        IEnumerable<string> routen,
        string modus,
        Func<string, int> haltezahl)
    {
        return modus switch
        {
            "name" => routen.OrderBy(Name, StringComparer.CurrentCultureIgnoreCase),
            "haltestellen" => routen
                .OrderByDescending(haltezahl)
                .ThenBy(route => Schluessel(RouteDisplayHelper.Parse(route).LineCourse).Linie)
                .ThenBy(Fahrt),
            _ => routen
                .OrderBy(route => Schluessel(RouteDisplayHelper.Parse(route).LineCourse).Linie)
                .ThenBy(route => Schluessel(RouteDisplayHelper.Parse(route).LineCourse).Kurs)
                .ThenBy(Fahrt)
                .ThenBy(route => route, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string Name(string route)
    {
        var name = RouteDisplayHelper.Parse(route).Name.Trim();
        return name.Length == 0 ? route : name;
    }

    private static string? ErsteZeit(FahrSession session, string route)
    {
        return session.Package?.GetStops(route)
            .Select(stop => stop.Time)
            .FirstOrDefault(zeit => !string.IsNullOrWhiteSpace(zeit));
    }

    private static DateOnly Betriebstag(DateTime jetzt) =>
        jetzt.Hour < 3
            ? DateOnly.FromDateTime(jetzt.Date.AddDays(-1))
            : DateOnly.FromDateTime(jetzt.Date);

    private static readonly Regex DatumVorne = new(
        @"^(\d{1,2})\.(\d{1,2})\.(\d{2,4})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool DatumsPraefixPasst(string route, DateOnly tag)
    {
        var name = RouteDisplayHelper.Parse(route).Name.Trim();
        var fund = DatumVorne.Match(name);
        if (!fund.Success
            || !int.TryParse(fund.Groups[1].Value, out var t)
            || !int.TryParse(fund.Groups[2].Value, out var m)
            || !int.TryParse(fund.Groups[3].Value, out var j))
        {
            return true;
        }

        if (j < 100)
        {
            j += 2000;
        }

        try
        {
            return new DateOnly(j, m, t) == tag;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    private static (int Linie, int Kurs) Schluessel(string? linieKurs)
    {
        var teile = (linieKurs ?? "").Split('/');
        var linie = teile.Length > 0 && int.TryParse(teile[0].Trim(), out var linieZahl)
            ? linieZahl
            : int.MaxValue;
        var kurs = teile.Length > 1 && int.TryParse(teile[1].Trim(), out var kursZahl)
            ? kursZahl
            : 0;
        return (linie, kurs);
    }
}
