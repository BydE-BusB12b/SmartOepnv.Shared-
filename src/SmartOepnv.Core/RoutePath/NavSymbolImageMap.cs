namespace SmartOepnv.Core.RoutePath;

/// <summary>Dateinamen der Navi-Grafiken (Ordner Assets/navi_grafiken).</summary>
public static class NavSymbolImageMap
{
    private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        ["roundabout_2_4"] = "Navi Kreisverkehr gerade aus.png",
        ["roundabout_2_5"] = "Navi Kreisverkehr gerade aus.png",
        ["roundabout_3_4"] = "Navi Kreisverkehr 3.Ausfahrt.png",
        ["roundabout_3_5"] = "Navi Kreisverkehr 3.Ausfahrt.png",
        ["roundabout_4_4"] = "Navi Kreisverkehr 4.Ausfahrt bei 4.png",
        ["roundabout_4_5"] = "Navi Kreisverkehr 4.Ausfahrt bei 5.png",
        ["roundabout_5_5"] = "Navi Kreisverkehr 5.Ausfahrt bei 5.png",
        ["roundabout_1_4"] = "Navi Kreisverkehr gerade aus.png",
        ["roundabout_1_5"] = "Navi Kreisverkehr gerade aus.png",
        ["t_left"] = "Navi T-Kreuzung links.png",
        ["t_right"] = "Navi T-Kreuzung rechts.png",
        ["cross_4_left"] = "Navi Kreuzung links.png",
        ["left"] = "Navi Kreuzung links.png",
        ["cross_4_right"] = "Navi Kreuzung rechts.png",
        ["right"] = "Navi Kreuzung rechts.png",
        ["cross_4_straight"] = "Navi Kreuzung gerade aus.png",
        ["cross_5"] = "Navi 5 armige Kreuzung halb rechts.png",
        ["cross_5_left"] = "Navi Y-Kreuzung 5 Richtungen halb links.png",
        ["cross_5_right"] = "Navi 5 armige Kreuzung halb rechts.png",
        ["kink_t_right"] = "Navi links gekippte T-Kreuzung rechts.png",
        ["kink_t_left"] = "Navi rechts gekippte T-Kreuzung links.png",
        ["kink_t_left_straight"] = "Navi links gekippte T-Kreuzung gerade aus.png",
        ["kink_t_right_straight"] = "Navi rechts gekippte T-Kreuzung gerade aus.png",
        ["priority_follow_left"] = "Navi Vorfahrtsstrasse nach links folgen.png",
        ["priority_follow_right"] = "Navi Vorfahrtsstrasse nach rechts folgen.png",
        ["priority_leave_left_straight"] = "Navi Vorfahrtsstrasse nach links geradeaus verlassen.png",
        ["priority_leave_right_straight"] = "Navi Vorfahrtsstrasse nach rechts geradeaus verlassen.png",
        ["double_t_right_left"] = "Navi Doppel-T Kreuzung rechts-links.png",
        ["double_t_left_right"] = "Navi Doppel-T Kreuzung links-rechts.png",
        ["shifted_double_t_left_right"] = "Navi Verschobene Doppel-T-Kreuzung links+rechts.png",
        ["shifted_double_t_right_left"] = "Navi Verschobene Doppel-T-Kreuzung rechts+links.png",
        ["shifted_t_to_cross_left_right"] = "Navi Verschobene T-Kreuzung zur 4 armige Kreuzung links+rechts.png",
        ["special_cross_left"] = "Navi Sonderform Kreuzung links abbiegen.png",
        ["special_cross_right"] = "Navi Sonderform Kreuzung rechts abbiegen.png",
        ["special_cross_left_plain"] = "Navi Sonderform Kreuzung links.png",
        ["special_cross_right_plain"] = "Navi Sonderform Kreuzung rechts.png",
        ["u_turn_custom"] = "Navi U-Turn Zusatz.png",
        ["u_turn"] = "Navi U-Turn.png",
        ["fork_left"] = "Navi Y-Kreuzung links.png",
        ["fork_right"] = "Navi Y-Kreuzung rechts.png",
        ["off_route"] = "Navi Route verlassen.png",
        ["straight"] = "Navi gerade aus.png",
        ["straight_stop"] = "Navi gerade aus.png",
        ["haltestelle"] = "Navi Haltesstelle.png",
        ["goal"] = "Navi Ziel.png",
        ["keep_left"] = "Navi Linke Spur - Links abfahren.png",
        ["slight_left"] = "Navi Linke Spur - Links abfahren.png",
        ["left_lane_exit"] = "Navi Linke Spur - Links abfahren.png",
        ["keep_right"] = "Navi rechts Spur - rechts abfahren.png",
        ["slight_right"] = "Navi rechts Spur - rechts abfahren.png",
        ["right_lane_exit"] = "Navi rechts Spur - rechts abfahren.png",
        ["motorway_exit"] = "Navi rechts Spur - rechts abfahren.png"
    };

    public static string? GetFileName(string? symbolType)
    {
        var id = (symbolType ?? string.Empty).Trim();
        return string.IsNullOrEmpty(id) ? null : Files.GetValueOrDefault(id);
    }
}
