namespace SmartOepnv.Core.Zeitwirtschaft;



public sealed class ZeitwirtschaftMergedEntry

{

    public required string EntryId { get; init; }

    public required string PersonnelNumber { get; init; }

    public required string Name { get; init; }

    public required string DevicePhone { get; init; }

    public long StartEpochMs { get; init; }

    public long? EndEpochMs { get; init; }

    public string? StartIso { get; init; }

    public string? EndIso { get; init; }

    public long? CorrectedStartEpochMs { get; init; }

    public long? CorrectedEndEpochMs { get; init; }

    public string? CorrectedStartIso { get; init; }

    public string? CorrectedEndIso { get; init; }

    public long? CorrectedAtMs { get; init; }

    public string? CorrectedBy { get; init; }

}



public sealed class ZeitwirtschaftMergedEmployee

{

    public required string PersonnelNumber { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<ZeitwirtschaftMergedEntry> Entries { get; init; }



    public string DisplayLine =>

        string.IsNullOrWhiteSpace(Name)

            ? PersonnelNumber

            : $"{Name}  ({PersonnelNumber})";

}



public sealed class ZeitwirtschaftMergedData

{

    public required IReadOnlyList<ZeitwirtschaftMergedEmployee> Employees { get; init; }

    public int SourceFileCount { get; init; }

    public int TotalEntryCount { get; init; }

}



public sealed class ZeitwirtschaftTimeTableRow

{

    public required string VehiclePhone { get; init; }

    public required string VehicleDisplayName { get; init; }

    public required string PersonnelNumber { get; init; }

    public required string Kommen { get; init; }

    public required string Gehen { get; init; }

    public required string Arbeitszeit { get; init; }

    public required string Lohnstunden { get; init; }

    public required string EntryId { get; init; }

    public bool HasCorrection { get; init; }

}



public sealed class ZeitwirtschaftMonthOption

{

    public required int Year { get; init; }

    public required int Month { get; init; }



    public string Label { get; init; } = string.Empty;

}

