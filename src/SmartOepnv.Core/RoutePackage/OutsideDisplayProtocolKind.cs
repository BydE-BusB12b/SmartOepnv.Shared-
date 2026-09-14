namespace SmartOepnv.Core.RoutePackage;

/// <summary>Außenanzeigen-Protokoll (wie GPSAnsagen <c>OutsideDisplayProtocolHelper</c>).</summary>
public enum OutsideDisplayProtocolKind
{
    Ds021T = 0,
    Ds021Neu = 1,
    Ds003aKrefeld = 2,
    FmaS1 = 3,
    Zielnummer = 4,
    /** Mobitec ICU402-Ersatz über USB-RS485 (4800 8N1). */
    Mobitec = 5
}
