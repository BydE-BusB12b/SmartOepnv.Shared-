namespace SmartOepnv.Core.RoutePath;

/// <summary>
/// Hilfslogik für manuell gesetzte Navi-Hinweise (Doppelklick auf Linie).
/// </summary>
public static class NavManeuverHelper
{
    public const string ManualInstruction = "Manuell";

    public static bool IsManualManeuver(RoutePathSnapManeuver? maneuver)
    {
        if (maneuver is null)
        {
            return false;
        }

        return (maneuver.Instruction ?? string.Empty)
            .Contains(ManualInstruction, StringComparison.OrdinalIgnoreCase);
    }

    public static void EnsureManualInstruction(RoutePathSnapManeuver maneuver)
    {
        maneuver.Instruction = ManualInstruction;
    }

    /// <summary>
    /// Blendet automatische OSRM-Hinweise in der Nähe eines manuellen Hinweises aus,
    /// damit nach Symbolwechsel nicht ein zweiter Hinweis an der Kreuzung erscheint.
    /// </summary>
    public static void SuppressNearbyAutoManeuvers(
        IList<RoutePathSnapManeuver> maneuvers,
        double manualDistanceM,
        double withinMeters = 45)
    {
        foreach (var maneuver in maneuvers)
        {
            if (IsManualManeuver(maneuver))
            {
                continue;
            }

            if (Math.Abs(maneuver.DistanceM - manualDistanceM) <= withinMeters)
            {
                maneuver.NavSymbolType = NavSymbolCatalog.Hidden;
            }
        }
    }

    public static bool SymbolImpliesDirection(string? symbolType, string direction)
    {
        var symbol = (symbolType ?? string.Empty).Trim().ToLowerInvariant();
        return direction switch
        {
            "left" => symbol is "left" or "t_left" or "cross_4_left" or "cross_5_left" or "fork_left"
                or "slight_left" or "keep_left" or "left_lane_exit",
            "right" => symbol is "right" or "t_right" or "cross_4_right" or "cross_5_right" or "fork_right"
                or "slight_right" or "keep_right" or "right_lane_exit" or "motorway_exit",
            _ => false
        };
    }
}
