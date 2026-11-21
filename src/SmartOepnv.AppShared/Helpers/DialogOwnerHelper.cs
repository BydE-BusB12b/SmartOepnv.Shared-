using System.Windows;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Stellt sicher, dass modale Dialoge am Planer-Hauptfenster hängen und dieses danach wieder sichtbar ist.
/// </summary>
public static class DialogOwnerHelper
{
    public static Window? ResolveOwner()
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        if (app.MainWindow is { IsLoaded: true } main)
        {
            return main;
        }

        return app.Windows.OfType<Window>()
                   .FirstOrDefault(w => w is MainShellWindow && w.IsLoaded)
               ?? app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsLoaded)
               ?? app.Windows.OfType<Window>().FirstOrDefault(w => w.IsLoaded);
    }

    public static void PrepareForModalDialog(Window? owner)
    {
        if (owner is null)
        {
            return;
        }

        if (owner.WindowState == WindowState.Minimized)
        {
            owner.WindowState = WindowState.Normal;
        }

        if (owner.Visibility != Visibility.Visible)
        {
            owner.Show();
        }

        owner.Activate();
    }

    public static void RestoreAfterModalDialog(Window? owner)
    {
        PrepareForModalDialog(owner);
        owner?.Focus();
    }

    public static bool? ShowOwnedDialog(Window dialog, Window? owner = null)
    {
        owner ??= ResolveOwner();
        dialog.ShowInTaskbar = false;
        if (owner is not null)
        {
            dialog.Owner = owner;
            PrepareForModalDialog(owner);
        }

        var result = dialog.ShowDialog();
        RestoreAfterModalDialog(owner);
        return result;
    }
}
