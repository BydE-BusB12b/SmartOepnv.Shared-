using System.Linq;
using System.Windows.Controls;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class DienstvorlagenView : UserControl
{
    public DienstvorlagenView()
    {
        InitializeComponent();
    }

    private void Part1RowsGrid_GotFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DienstvorlagenViewModel viewModel)
        {
            viewModel.SetActiveDutyPart(1);
        }
    }

    private void Part2RowsGrid_GotFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DienstvorlagenViewModel viewModel)
        {
            viewModel.SetActiveDutyPart(2);
        }
    }

    private void Part3RowsGrid_GotFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is DienstvorlagenViewModel viewModel)
        {
            viewModel.SetActiveDutyPart(3);
        }
    }

    private void Part1RowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DienstvorlagenViewModel viewModel && sender is DataGrid grid)
        {
            viewModel.SetActiveDutyPart(1);
            viewModel.UpdateActiveGridSelection(grid.SelectedItems.Cast<DutyTemplateRowItem>().ToList());
        }
    }

    private void Part2RowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DienstvorlagenViewModel viewModel && sender is DataGrid grid)
        {
            viewModel.SetActiveDutyPart(2);
            viewModel.UpdateActiveGridSelection(grid.SelectedItems.Cast<DutyTemplateRowItem>().ToList());
        }
    }

    private void Part3RowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is DienstvorlagenViewModel viewModel && sender is DataGrid grid)
        {
            viewModel.SetActiveDutyPart(3);
            viewModel.UpdateActiveGridSelection(grid.SelectedItems.Cast<DutyTemplateRowItem>().ToList());
        }
    }
}
