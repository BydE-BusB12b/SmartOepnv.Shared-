namespace SmartOepnv.Core.RoutePackage;

/// <summary>Außenanzeigen-Protokoll (wie GPSAnsagen <c>OutsideDisplayProtocolHelper</c>).</summary>
public enum OutsideDisplayProtocolKind
{
    Ds021T = 0,
    Ds021Neu = 1,
    Ds003aKrefeld = 2,
    FmaS1 = 3,
    /// <summary>
    /// Klassisches IBIS-DS003: nur Zielnummer <c>z001</c>… (CR + XOR-Parität), kein Klartext/DS001.
    /// Speicher-Tag „DS003“ (ältere Pakete: „Zielnummer“).
    /// </summary>
    Ds003 = 4,
    /// <summary>Mobitec ICU402-Ersatz über USB-RS485 (4800 8N1).</summary>
    Mobitec = 5
}
