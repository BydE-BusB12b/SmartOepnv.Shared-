using System.Windows;
using System.Windows.Controls;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Themes;

public partial class SmartOepnvTheme : ResourceDictionary
{
    public SmartOepnvTheme()
    {
        InitializeComponent();
    }

    private void ComboBoxItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        ComboBoxItemBehaviors.SuppressHoverBringIntoView(sender, e);
    }
}
