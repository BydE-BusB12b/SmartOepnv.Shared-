using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using SmartOepnv.AppShared.ViewModels;

namespace SmartOepnv.AppShared.Views;

public partial class DienstvorlagenView : UserControl
{
    public DienstvorlagenView()
    {
        InitializeComponent();
    }

    private void CaptureGridSelectionBeforeCommand(DataGrid? grid)
    {
        if (DataContext is not DienstvorlagenViewModel viewModel || grid is null)
        {
            return;
        }

        if (ReferenceEquals(grid, Part2RowsGrid))
        {
            viewModel.SetActiveDutyPart(2);
        }
        else if (ReferenceEquals(grid, Part3RowsGrid))
        {
            viewModel.SetActiveDutyPart(3);
        }
        else
        {
            viewModel.SetActiveDutyPart(1);
        }

        viewModel.CaptureActiveGridSelection(grid.SelectedItems.Cast<DutyTemplateRowItem>().ToList());
    }

    private void IntelligentSplitButton_PreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        CaptureGridSelectionBeforeCommand(Part1RowsGrid);

    private void SplitDutyButton_PreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        CaptureGridSelectionBeforeCommand(GetGridWithSelection());

    private void SplitShiftButton_PreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        CaptureGridSelectionBeforeCommand(Part1RowsGrid);

    private DataGrid GetGridWithSelection()
    {
        if (Part3RowsGrid.SelectedItems.Count > 0)
        {
            return Part3RowsGrid;
        }

        if (Part2RowsGrid.SelectedItems.Count > 0)
        {
            return Part2RowsGrid;
        }

        if (Part1RowsGrid.SelectedItems.Count > 0)
        {
            return Part1RowsGrid;
        }

        return Part1RowsGrid;
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
