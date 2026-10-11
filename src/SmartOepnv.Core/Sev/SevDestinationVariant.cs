namespace SmartOepnv.Core.Sev;

/// <summary>
/// Variante 1: großes Ziel (ggf. zweizeilig per Komma-Split), Pfeil links vor dem Zielblock.
/// Variante 2: Zeile 1 Start, Zeile 2 mittig Pfeile (&lt; &gt;), Zeile 3 Ziel.
/// </summary>
public enum SevDestinationVariant
{
    DestinationOnly = 0,
    StartAndDestination = 1
}
