using System.Windows;
using System.Windows.Input;

namespace SmartOepnv.AppShared.Helpers;

/// <summary>
/// Verhindert das automatische Scrollen in ComboBox-Dropdowns beim Maus-Hover über Einträge.
/// </summary>
public static class ComboBoxItemBehaviors
{
    public static void SuppressHoverBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (IsKeyboardNavigation())
            return;

        e.Handled = true;
    }

    private static bool IsKeyboardNavigation()
    {
        return Keyboard.IsKeyDown(Key.Down)
               || Keyboard.IsKeyDown(Key.Up)
               || Keyboard.IsKeyDown(Key.PageDown)
               || Keyboard.IsKeyDown(Key.PageUp)
               || Keyboard.IsKeyDown(Key.Home)
               || Keyboard.IsKeyDown(Key.End);
    }
}
