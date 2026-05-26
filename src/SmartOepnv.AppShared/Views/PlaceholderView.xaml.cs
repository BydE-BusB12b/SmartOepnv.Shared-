using System.Windows.Controls;

namespace SmartOepnv.AppShared.Views;

public partial class PlaceholderView : UserControl
{
    public PlaceholderView()
    {
        InitializeComponent();
    }

    public PlaceholderView(string title, string description) : this()
    {
        TitleText.Text = title;
        DescriptionText.Text = description;
    }
}
