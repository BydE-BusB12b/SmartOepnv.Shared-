using System.Windows;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

public enum PlanerCloseChoice
{
    Cancel,
    SaveAndClose,
    CloseWithoutSave
}

public partial class PlanerCloseChoiceDialog : Window
{
    public PlanerCloseChoice Choice { get; private set; } = PlanerCloseChoice.Cancel;

    public static PlanerCloseChoice Show(Window owner)
    {
        var dialog = new PlanerCloseChoiceDialog
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public PlanerCloseChoiceDialog()
    {
        InitializeComponent();
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
    }

    private void SaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        Choice = PlanerCloseChoice.SaveAndClose;
        DialogResult = true;
        Close();
    }

    private void CloseWithoutSave_Click(object sender, RoutedEventArgs e)
    {
        Choice = PlanerCloseChoice.CloseWithoutSave;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Choice = PlanerCloseChoice.Cancel;
        DialogResult = false;
        Close();
    }
}
