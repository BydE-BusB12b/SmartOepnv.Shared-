using System.Globalization;



namespace SmartOepnv.Core.RoutePackage;



/// <summary>Optionales Gültigkeitsdatum für Routen (zusätzlich zu Verkehrstagen).</summary>

public sealed class RouteDateRange

{

    /// <summary>Anzeige- und Eingabeformat: Tag.Monat.Jahr</summary>

    public const string DateFormat = "dd.MM.yyyy";



    private static readonly string[] AcceptedInputFormats =

    [

        "dd.MM.yyyy",

        "d.M.yyyy",

        "dd.MM.yy",

        "d.M.yy",

        "yyyy-MM-dd"

    ];



    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");



    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }



    public bool IsRestricted => From is not null || To is not null;



    public static RouteDateRange Unrestricted => new();



    public static bool TryParse(string? fromRaw, string? toRaw, out RouteDateRange range)

    {

        DateOnly? from = null;

        DateOnly? to = null;

        if (!string.IsNullOrWhiteSpace(fromRaw))

        {

            if (!TryParseDate(fromRaw, out var parsedFrom))

            {

                range = Unrestricted;

                return false;

            }



            from = parsedFrom;

        }



        if (!string.IsNullOrWhiteSpace(toRaw))

        {

            if (!TryParseDate(toRaw, out var parsedTo))

            {

                range = Unrestricted;

                return false;

            }



            to = parsedTo;

        }



        if (from is not null && to is not null && from.Value > to.Value)

        {

            range = Unrestricted;

            return false;

        }



        range = new RouteDateRange { From = from, To = to };

        return true;

    }



    public static bool TryParseDate(string? raw, out DateOnly date)

    {

        date = default;

        var trimmed = (raw ?? string.Empty).Trim();

        if (trimmed.Length == 0)

        {

            return false;

        }



        foreach (var format in AcceptedInputFormats)

        {

            if (DateOnly.TryParseExact(trimmed, format, GermanCulture, DateTimeStyles.None, out date))

            {

                return true;

            }

        }



        return DateOnly.TryParse(trimmed, GermanCulture, DateTimeStyles.None, out date);

    }



    public static string FormatDate(DateOnly date) =>

        date.ToString(DateFormat, GermanCulture);



    public static string FormatDisplay(RouteDateRange? range)

    {

        if (range is null || !range.IsRestricted)

        {

            return string.Empty;

        }



        if (range.From is { } from && range.To is { } to)

        {

            return $"{FormatDate(from)} – {FormatDate(to)}";

        }



        if (range.From is { } onlyFrom)

        {

            return $"ab {FormatDate(onlyFrom)}";

        }



        return range.To is { } onlyTo

            ? $"bis {FormatDate(onlyTo)}"

            : string.Empty;

    }



    public static bool RangesOverlap(RouteDateRange? left, RouteDateRange? right)

    {

        if (left is null || !left.IsRestricted || right is null || !right.IsRestricted)

        {

            return true;

        }



        var leftFrom = left.From ?? DateOnly.MinValue;

        var leftTo = left.To ?? DateOnly.MaxValue;

        var rightFrom = right.From ?? DateOnly.MinValue;

        var rightTo = right.To ?? DateOnly.MaxValue;

        return leftFrom <= rightTo && rightFrom <= leftTo;

    }



    public bool Contains(DateOnly date)

    {

        if (!IsRestricted)

        {

            return true;

        }



        if (From is { } from && date < from)

        {

            return false;

        }



        if (To is { } to && date > to)

        {

            return false;

        }



        return true;

    }

}


