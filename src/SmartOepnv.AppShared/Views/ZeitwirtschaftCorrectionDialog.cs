using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.Core.Zeitwirtschaft;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

public sealed class ZeitwirtschaftCorrectionDialog : Window
{
    private static readonly Brush InputBackground = Brushes.White;
    private static readonly Brush InputForeground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
    private static Brush LabelForeground => UiTheme.ForegroundBrush;

    private readonly ZeitwirtschaftMergedEntry _entry;
    private readonly TextBox _correctedStartBox;
    private readonly TextBox _correctedEndBox;
    private readonly List<PauseEditor> _pauses = [];
    private readonly TextBlock _errorText;

    public long? CorrectedStartMs { get; private set; }
    public long? CorrectedEndMs { get; private set; }
    public IReadOnlyList<ZeitwirtschaftDocumentEditor.PauseCorrection> PauseCorrections { get; private set; } = [];

    public ZeitwirtschaftCorrectionDialog(
        ZeitwirtschaftTimeTableRow row,
        ZeitwirtschaftMergedEntry entry)
    {
        _entry = entry;
        Title = "Zeitkorrektur";
        Width = 520;
        MinWidth = 480;
        MaxHeight = 720;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = UiTheme.BackgroundBrush;

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "Zeitkorrektur",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Nur eine geänderte Zeit wird gespeichert. Ein leeres Feld setzt diese eine Zeit auf die Stempelzeit zurück.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = LabelForeground,
            Opacity = 0.88,
            Margin = new Thickness(0, 0, 0, 16)
        });

        root.Children.Add(MakeLabel("Original Kommen"));
        root.Children.Add(MakeReadOnly(row.Kommen.Split('\n')[0]));
        root.Children.Add(MakeLabel("Original Gehen"));
        root.Children.Add(MakeReadOnly(row.Gehen.Split('\n')[0]));

        var effectiveStart = ZeitwirtschaftMergeService.EffectiveStartMs(entry);
        var effectiveEnd = ZeitwirtschaftMergeService.EffectiveEndMs(entry);

        root.Children.Add(MakeLabel("Korrigiert Kommen"));
        _correctedStartBox = MakeInput(ZeitwirtschaftMergeService.FormatStamp(
            effectiveStart, entry.CorrectedStartIso ?? entry.StartIso));
        root.Children.Add(_correctedStartBox);

        root.Children.Add(MakeLabel("Korrigiert Gehen"));
        _correctedEndBox = MakeInput(effectiveEnd is > 0
            ? ZeitwirtschaftMergeService.FormatStamp(effectiveEnd.Value, entry.CorrectedEndIso ?? entry.EndIso)
            : string.Empty);
        root.Children.Add(_correctedEndBox);

        root.Children.Add(MakeLabel("Pausen"));
        if (entry.Pauses.Count == 0)
        {
            root.Children.Add(new TextBlock
            {
                Text = "Keine Pause gestempelt.",
                Foreground = LabelForeground,
                Opacity = 0.8
            });
        }

        foreach (var pause in entry.Pauses.Where(p => p.StartEpochMs > 0).OrderBy(p => p.StartEpochMs))
        {
            var original = Range(pause.StartEpochMs, pause.EndEpochMs);
            root.Children.Add(new TextBlock
            {
                Text = "Original " + original,
                Foreground = LabelForeground,
                Opacity = 0.92,
                Margin = new Thickness(0, 6, 0, 4)
            });
            var startBox = MakeInput(ZeitwirtschaftMergeService.FormatStamp(pause.EffectiveStartMs, null));
            var endBox = MakeInput(pause.EffectiveEndMs is > 0
                ? ZeitwirtschaftMergeService.FormatStamp(pause.EffectiveEndMs.Value, null)
                : string.Empty);
            root.Children.Add(MakeLabel("Beginn"));
            root.Children.Add(startBox);
            root.Children.Add(MakeLabel("Ende"));
            root.Children.Add(endBox);
            _pauses.Add(new PauseEditor(pause.StartEpochMs, pause.EndEpochMs ?? 0, startBox, endBox));
        }

        _errorText = new TextBlock
        {
            Foreground = Brushes.OrangeRed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        var cancel = new Button
        {
            Content = "Abbrechen",
            MinWidth = 100,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var save = new Button
        {
            Content = "Speichern",
            MinWidth = 100,
            MinHeight = 36,
            IsDefault = true
        };
        save.Click += (_, _) =>
        {
            if (!TryParseInput(out var error))
            {
                _errorText.Text = error;
                _errorText.Visibility = Visibility.Visible;
                return;
            }

            DialogResult = true;
            Close();
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        root.Children.Add(buttons);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = root
        };
    }

    private bool TryParseInput(out string error)
    {
        error = string.Empty;
        if (!TryField(_correctedStartBox.Text, _entry.StartEpochMs, "Kommen", out var startCorrection, out error))
        {
            return false;
        }

        if (!TryField(_correctedEndBox.Text, _entry.EndEpochMs ?? 0, "Gehen", out var endCorrection, out error))
        {
            return false;
        }

        var shiftStart = startCorrection ?? _entry.StartEpochMs;
        var shiftEnd = endCorrection ?? _entry.EndEpochMs;
        if (shiftStart <= 0)
        {
            error = "Kommen fehlt.";
            return false;
        }

        if (shiftEnd is > 0 && shiftEnd.Value <= shiftStart)
        {
            error = "Gehen muss nach Kommen liegen.";
            return false;
        }

        var pauses = new List<ZeitwirtschaftDocumentEditor.PauseCorrection>();
        var intervals = new List<(long Start, long End)>();
        foreach (var pause in _pauses)
        {
            if (!TryField(pause.StartBox.Text, pause.OriginalStartMs, "Pausenbeginn", out var pauseStart, out error) ||
                !TryField(pause.EndBox.Text, pause.OriginalEndMs, "Pausenende", out var pauseEnd, out error))
            {
                return false;
            }

            var effectiveStart = pauseStart ?? pause.OriginalStartMs;
            var effectiveEnd = pauseEnd ?? (pause.OriginalEndMs > 0 ? pause.OriginalEndMs : (long?)null);
            if (effectiveEnd is > 0 && effectiveEnd.Value <= effectiveStart)
            {
                error = "Pausenende muss nach dem Pausenbeginn liegen.";
                return false;
            }

            if (shiftEnd is > 0 && effectiveEnd is > 0 &&
                (effectiveStart < shiftStart || effectiveEnd > shiftEnd))
            {
                error = "Die Pause muss zwischen Kommen und Gehen liegen.";
                return false;
            }

            if (effectiveEnd is > 0)
            {
                intervals.Add((effectiveStart, effectiveEnd.Value));
            }

            pauses.Add(new ZeitwirtschaftDocumentEditor.PauseCorrection(
                pause.OriginalStartMs, pauseStart, pauseEnd));
        }

        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (var i = 1; i < intervals.Count; i++)
        {
            if (intervals[i].Start < intervals[i - 1].End)
            {
                error = "Pausen dürfen sich nicht überschneiden.";
                return false;
            }
        }

        if (!Changed(startCorrection, endCorrection, pauses))
        {
            error = "Nichts geändert.";
            return false;
        }

        CorrectedStartMs = startCorrection;
        CorrectedEndMs = endCorrection;
        PauseCorrections = pauses;
        return true;
    }

    private bool Changed(
        long? startCorrection,
        long? endCorrection,
        IReadOnlyList<ZeitwirtschaftDocumentEditor.PauseCorrection> pauses)
    {
        if (Differs(_entry.StartEpochMs, _entry.CorrectedStartEpochMs, startCorrection) ||
            Differs(_entry.EndEpochMs ?? 0, _entry.CorrectedEndEpochMs, endCorrection))
        {
            return true;
        }

        foreach (var pause in pauses)
        {
            var stored = _entry.Pauses.FirstOrDefault(item => item.StartEpochMs == pause.OriginalStartMs);
            if (stored is null)
            {
                return true;
            }

            if (Differs(stored.StartEpochMs, stored.CorrectedStartEpochMs, pause.CorrectedStartMs) ||
                Differs(stored.EndEpochMs ?? 0, stored.CorrectedEndEpochMs, pause.CorrectedEndMs))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Differs(long originalMs, long? storedMs, long? desiredMs)
    {
        var stored = ZeitwirtschaftMergeService.IsRealCorrection(originalMs, storedMs) ? storedMs : null;
        if (stored is null && desiredMs is null)
        {
            return false;
        }

        if (stored is null || desiredMs is null)
        {
            return true;
        }

        return !ZeitwirtschaftMergeService.SameMinute(stored.Value, desiredMs.Value);
    }

    /// <summary>Leer oder gleiche Minute wie die Stempelzeit ergibt keine Korrektur.</summary>
    private static bool TryField(
        string text,
        long originalMs,
        string label,
        out long? correctionMs,
        out string error)
    {
        correctionMs = null;
        error = string.Empty;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (!TryParseGermanDateTime(trimmed, out var parsed))
        {
            error = label + " ist ungültig (Format: dd.MM.yyyy HH:mm).";
            return false;
        }

        var epochMs = new DateTimeOffset(parsed).ToUnixTimeMilliseconds();
        if (originalMs > 0 && ZeitwirtschaftMergeService.SameMinute(originalMs, epochMs))
        {
            return true;
        }

        correctionMs = epochMs;
        return true;
    }

    private static string Range(long startMs, long? endMs)
    {
        var start = ZeitwirtschaftMergeService.FormatStamp(startMs, null);
        if (endMs is not > 0)
        {
            return start + " – offen";
        }

        return start + " – " + ZeitwirtschaftMergeService.FormatStamp(endMs.Value, null);
    }

    private static bool TryParseGermanDateTime(string text, out DateTime result)
    {
        result = default;
        return DateTime.TryParseExact(
            text.Trim(),
            "dd.MM.yyyy HH:mm",
            CultureInfo.GetCultureInfo("de-DE"),
            DateTimeStyles.AssumeLocal,
            out result);
    }

    private static TextBlock MakeLabel(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 8, 0, 4)
        };

    private static TextBlock MakeReadOnly(string text) =>
        new()
        {
            Text = text,
            Foreground = LabelForeground,
            Opacity = 0.92,
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF))
        };

    private static TextBox MakeInput(string initial) =>
        new()
        {
            Text = initial,
            Background = InputBackground,
            Foreground = InputForeground,
            Padding = new Thickness(10, 6, 10, 6),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x44, 0x66))
        };

    private sealed class PauseEditor(
        long originalStartMs,
        long originalEndMs,
        TextBox startBox,
        TextBox endBox)
    {
        public long OriginalStartMs { get; } = originalStartMs;
        public long OriginalEndMs { get; } = originalEndMs;
        public TextBox StartBox { get; } = startBox;
        public TextBox EndBox { get; } = endBox;
    }
}
