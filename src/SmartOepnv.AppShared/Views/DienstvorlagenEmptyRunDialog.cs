using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.Core.Dienstvorlagen;

namespace SmartOepnv.AppShared.Views;

public sealed class DienstvorlagenEmptyRunDialog : Window
{
    private static readonly Brush DialogBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush LabelForeground = Brushes.White;
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));

    private readonly StackPanel _rulesPanel;
    private readonly TextBlock _errorText;

    public IReadOnlyList<DutyTemplateEmptyRunRule> Rules { get; private set; } = [];

    public DienstvorlagenEmptyRunDialog()
    {
        Title = "Intelligente Leerfahrten";
        Width = 520;
        MinWidth = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = DialogBackground;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "Leerfahrt-Regeln",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Von- und Nach-Haltestelle sowie Dauer eingeben. Die Pause zwischen Fahrten muss mindestens so lang sein.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = LabelForeground,
            Opacity = 0.8,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        header.Children.Add(MakeHeaderLabel("Von Haltestelle", 0));
        header.Children.Add(MakeHeaderLabel("Nach Haltestelle", 2));
        header.Children.Add(MakeHeaderLabel("Min.", 4));
        root.Children.Add(header);

        _rulesPanel = new StackPanel();
        root.Children.Add(_rulesPanel);
        AddRuleRow();

        var addButton = new Button
        {
            Content = "+ Weitere Regel",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4),
            Background = Brushes.Transparent,
            Foreground = AccentBrush,
            BorderBrush = AccentBrush
        };
        addButton.Click += (_, _) => AddRuleRow();
        root.Children.Add(addButton);

        _errorText = new TextBlock
        {
            Foreground = Brushes.OrangeRed,
            FontSize = 11,
            Margin = new Thickness(0, 10, 0, 0),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button
        {
            Content = "Abbrechen",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(14, 6, 14, 6),
            IsCancel = true
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var ok = new Button
        {
            Content = "Einfügen",
            Padding = new Thickness(14, 6, 14, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x0D, 0x47, 0xA1)),
            Foreground = Brushes.White,
            IsDefault = true
        };
        ok.Click += (_, _) => TryConfirm();
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;
    }

    private void AddRuleRow()
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

        var fromBox = MakeInput("z. B. D-Gerresheim S Bstg 4");
        var toBox = MakeInput("z. B. D-Gerresheim S Bstg 3");
        var minutesBox = MakeInput("3");
        Grid.SetColumn(fromBox, 0);
        Grid.SetColumn(toBox, 2);
        Grid.SetColumn(minutesBox, 4);
        row.Children.Add(fromBox);
        row.Children.Add(toBox);
        row.Children.Add(minutesBox);

        var removeButton = new Button
        {
            Content = "×",
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = AccentBrush,
            BorderThickness = new Thickness(0),
            Visibility = _rulesPanel.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible
        };
        Grid.SetColumn(removeButton, 6);
        row.Children.Add(removeButton);

        row.Tag = new RuleRowInputs(fromBox, toBox, minutesBox);
        removeButton.Click += (_, _) =>
        {
            _rulesPanel.Children.Remove(row);
            UpdateRemoveButtons();
        };

        _rulesPanel.Children.Add(row);
        UpdateRemoveButtons();
    }

    private void UpdateRemoveButtons()
    {
        var showRemove = _rulesPanel.Children.Count > 1;
        foreach (var child in _rulesPanel.Children)
        {
            if (child is not Grid grid)
            {
                continue;
            }

            foreach (var gridChild in grid.Children)
            {
                if (gridChild is Button { Content: "×" } removeButton)
                {
                    removeButton.Visibility = showRemove ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }
    }

    private void TryConfirm()
    {
        var rules = new List<DutyTemplateEmptyRunRule>();
        foreach (var child in _rulesPanel.Children)
        {
            if (child is not Grid { Tag: RuleRowInputs inputs })
            {
                continue;
            }

            var rule = new DutyTemplateEmptyRunRule
            {
                FromStop = inputs.FromBox.Text.Trim(),
                ToStop = inputs.ToBox.Text.Trim(),
                DurationMinutes = DutyTemplateCalculator.ParseNonNegativeMinutes(inputs.MinutesBox.Text, 0)
            };
            if (rule.IsValid)
            {
                rules.Add(rule);
            }
        }

        if (rules.Count == 0)
        {
            _errorText.Text = "Bitte mindestens eine vollständige Regel angeben (von, nach, Minuten > 0).";
            _errorText.Visibility = Visibility.Visible;
            return;
        }

        Rules = rules;
        DialogResult = true;
        Close();
    }

    private static TextBlock MakeHeaderLabel(string text, int column)
    {
        var label = new TextBlock
        {
            Text = text,
            Foreground = LabelForeground,
            Opacity = 0.75,
            FontSize = 10
        };
        Grid.SetColumn(label, column);
        return label;
    }

    private static TextBox MakeInput(string placeholder)
    {
        var box = new TextBox
        {
            MinHeight = 32,
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(8, 4, 8, 4),
            ToolTip = placeholder
        };
        if (placeholder is "3")
        {
            box.Text = placeholder;
        }

        return box;
    }

    private sealed record RuleRowInputs(TextBox FromBox, TextBox ToBox, TextBox MinutesBox);
}
