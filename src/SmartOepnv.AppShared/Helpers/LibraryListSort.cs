namespace SmartOepnv.AppShared.Helpers;

/// <summary>Sortierung für Ansagen- und Haltestellenlisten (ID / Name, auf-/absteigend).</summary>
public enum LibraryListSortField
{
    Id,
    Name
}

public enum LibraryListSortDirection
{
    Ascending,
    Descending
}

public static class LibraryListSort
{
    public static string FormatButtonLabel(
        string fieldLabel,
        LibraryListSortField field,
        LibraryListSortField activeField,
        LibraryListSortDirection direction)
    {
        if (field != activeField)
        {
            return fieldLabel;
        }

        return direction == LibraryListSortDirection.Ascending
            ? $"{fieldLabel} ↑"
            : $"{fieldLabel} ↓";
    }

    /// <summary>
    /// Gleicher Button erneut: Richtung umkehren; anderer Button: Feld wechseln (aufsteigend).
    /// </summary>
    public static void Toggle(
        ref LibraryListSortField activeField,
        ref LibraryListSortDirection direction,
        LibraryListSortField clicked)
    {
        if (activeField == clicked)
        {
            direction = direction == LibraryListSortDirection.Ascending
                ? LibraryListSortDirection.Descending
                : LibraryListSortDirection.Ascending;
            return;
        }

        activeField = clicked;
        direction = LibraryListSortDirection.Ascending;
    }

    public static IOrderedEnumerable<T> OrderByField<T>(
        IEnumerable<T> source,
        LibraryListSortField field,
        LibraryListSortDirection direction,
        Func<T, string?> idSelector,
        Func<T, string?> nameSelector)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        return field switch
        {
            LibraryListSortField.Name when direction == LibraryListSortDirection.Ascending =>
                source.OrderBy(x => nameSelector(x) ?? string.Empty, cmp)
                    .ThenBy(x => idSelector(x) ?? string.Empty, cmp),
            LibraryListSortField.Name =>
                source.OrderByDescending(x => nameSelector(x) ?? string.Empty, cmp)
                    .ThenByDescending(x => idSelector(x) ?? string.Empty, cmp),
            LibraryListSortField.Id when direction == LibraryListSortDirection.Descending =>
                source.OrderByDescending(x => idSelector(x) ?? string.Empty, cmp)
                    .ThenByDescending(x => nameSelector(x) ?? string.Empty, cmp),
            _ =>
                source.OrderBy(x => idSelector(x) ?? string.Empty, cmp)
                    .ThenBy(x => nameSelector(x) ?? string.Empty, cmp)
        };
    }
}
