using System.Collections.ObjectModel;
using System.Windows;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public partial class LineReplacementListDialog : Window
{
    private readonly ObservableCollection<LineReplacementRule> _rules = [];

    public LineReplacementListDialog(IEnumerable<LineReplacementRule> rules)
    {
        InitializeComponent();
        foreach (var rule in rules)
        {
            _rules.Add(rule);
        }

        RuleList.ItemsSource = _rules;
    }

    public IReadOnlyList<LineReplacementRule> Rules => _rules.ToList();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (!LineReplacementRule.TryCreate(
                FromLineBox.Text,
                ToLineBox.Text,
                FromDisplayBox.Text,
                ToDisplayBox.Text,
                out var rule,
                out var error) ||
            rule is null)
        {
            ErrorText.Text = error;
            return;
        }

        if (_rules.Any(existing =>
                existing.FromLine == rule.FromLine &&
                existing.ToLine == rule.ToLine &&
                existing.FromDisplay == rule.FromDisplay &&
                existing.ToDisplay == rule.ToDisplay))
        {
            ErrorText.Text = "Dieser Bereich steht schon in der Liste.";
            return;
        }

        _rules.Add(rule);
        ErrorText.Text = string.Empty;
        FromLineBox.Clear();
        ToLineBox.Clear();
        FromDisplayBox.Clear();
        ToDisplayBox.Clear();
        FromLineBox.Focus();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (RuleList.SelectedItem is LineReplacementRule rule)
        {
            _rules.Remove(rule);
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
