using System.Text.Json.Nodes;
using System.Windows;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.RoutePath;

namespace SmartOepnv.AppShared.RoutePath;

/// <summary>
/// Prüft nach Haltestellenänderungen, ob Navidaten aus anderen Routen übernommen werden können.
/// </summary>
public static class RoutePathNavReusePrompt
{
    public static bool TryOffer(
        Window? owner,
        EditableRoutePackage editor,
        string targetRouteKey,
        out int appliedEdgeCount)
    {
        appliedEdgeCount = 0;
        if (editor.PackageRoot is not JsonObject root)
        {
            return false;
        }

        var matches = RoutePathNavReuseFinder.Find(editor, root, targetRouteKey);
        if (matches.Count == 0)
        {
            return false;
        }

        var dialog = new RoutePathNavReuseDialog(matches) { Owner = owner };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var toApply = RoutePathNavReuseFinder.SelectNonOverlappingBest(dialog.SelectedMatches);
        appliedEdgeCount = RoutePathNavReuseApplier.Apply(
            root,
            targetRouteKey,
            editor.GetStops(targetRouteKey),
            toApply,
            editor.GetStops);
        return appliedEdgeCount > 0;
    }
}
