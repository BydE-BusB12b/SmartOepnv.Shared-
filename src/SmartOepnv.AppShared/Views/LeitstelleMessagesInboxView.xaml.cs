using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class LeitstelleMessagesInboxView : UserControl
{
    public LeitstelleMessagesInboxView()
    {
        InitializeComponent();
    }

    private void MessageList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsFromDeleteButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is not ListBox listBox)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (listBox.ContainerFromElement(source) is not ListBoxItem listItem)
        {
            return;
        }

        if (listItem.Content is not LeitstelleInboxItemViewModel item)
        {
            return;
        }

        if (DataContext is LeitstelleMessagesInboxViewModel viewModel)
        {
            viewModel.OpenOnMapCommand.Execute(item);
        }
    }

    private static bool IsFromDeleteButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
            {
                return true;
            }

            source = GetParentSafe(source);
        }

        return false;
    }

    private static DependencyObject? GetParentSafe(DependencyObject current) =>
        current switch
        {
            Visual => VisualTreeHelper.GetParent(current),
            FrameworkContentElement fce => fce.Parent as DependencyObject,
            _ => LogicalTreeHelper.GetParent(current)
        };
}
