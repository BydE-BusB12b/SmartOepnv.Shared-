using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Planer-Eingabelimit für DS009-Texte (Haltestellenanzeige / Zieltext): 16 oder 20 Zeichen.
/// Entspricht der App-Einstellung <c>ds009_text_length</c>; speichert nur die Planer-UI-Länge.
/// </summary>
public static class PlanerDs009TextLength
{
    public const int Length16 = 16;
    public const int Length20 = 20;

    public static event Action? Changed;

    public static int Normalize(int length) =>
        length <= Length16 ? Length16 : Length20;

    public static int Read()
    {
        if (!AppServices.IsPlannerApp || AppServices.PlanerAppSettings is null)
        {
            return Length20;
        }

        return Normalize(AppServices.PlanerAppSettings.Load().Ds009TextLength);
    }

    public static void Save(int length)
    {
        if (!AppServices.IsPlannerApp || AppServices.PlanerAppSettings is null)
        {
            return;
        }

        var normalized = Normalize(length);
        var stored = AppServices.PlanerAppSettings.Load();
        if (Normalize(stored.Ds009TextLength) == normalized)
        {
            return;
        }

        stored.Ds009TextLength = normalized;
        AppServices.PlanerAppSettings.Save(stored);
        Changed?.Invoke();
    }
}
