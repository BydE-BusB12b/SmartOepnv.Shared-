using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;

namespace SmartOepnv.AppShared.Views;

public enum FahrplanActionChoice
{
    Cancel,
    AutoCreate,
    OpenHtml,
    CopyTravelSegment,
    UmlaufImport
}

/// <summary>Auswahl: automatische Fahrplanerstellung, Umlauf-Import, HTML-/PDF-Fahrplan oder Fahrbereich kopieren.</summary>
public sealed class FahrplanActionDialog : Window
{
    private static readonly Brush DialogBackground = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));

    public FahrplanActionChoice Choice { get; private set; } = FahrplanActionChoice.Cancel;

    public static FahrplanActionChoice Show(Window? owner, bool canCopyTravelSegment = false)
    {
        var dialog = new FahrplanActionDialog(canCopyTravelSegment)
        {
            Owner = owner,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    public FahrplanActionDialog(bool canCopyTravelSegment = false)
    {
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        Title = "Fahrplan";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Background = DialogBackground;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Was möchten Sie tun?",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Automatisch neue Fahrten aus einer Vorlage erzeugen, einen Umlauf importieren, den Fahrplan als HTML öffnen oder einen Fahrbereich in eine neue Route übernehmen.",
            Foreground = Brushes.White,
            Opacity = 0.8,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var createButton = CreateActionButton(
            "Automatisch erstellen…",
            filled: true,
            margin: new Thickness(0, 0, 0, 8));
        createButton.Click += (_, _) =>
        {
            Choice = FahrplanActionChoice.AutoCreate;
            DialogResult = true;
            Close();
        };
        root.Children.Add(createButton);

        var umlaufButton = CreateActionButton(
            "Umlauf importieren…",
            filled: false,
            margin: new Thickness(0, 0, 0, 8));
        umlaufButton.ToolTip = "Fahrtenliste (Richtung;Fahrt;Startzeit) auf Hin-/Rück-Vorlagen legen.";
        umlaufButton.Click += (_, _) =>
        {
            Choice = FahrplanActionChoice.UmlaufImport;
            DialogResult = true;
            Close();
        };
        root.Children.Add(umlaufButton);

        var htmlButton = CreateActionButton(
            "Als HTML / PDF öffnen",
            filled: false,
            margin: new Thickness(0, 0, 0, 8));
        htmlButton.Click += (_, _) =>
        {
            Choice = FahrplanActionChoice.OpenHtml;
            DialogResult = true;
            Close();
        };
        root.Children.Add(htmlButton);

        var copyButton = CreateActionButton(
            "Fahrbereich kopieren",
            filled: false,
            margin: new Thickness(0, 0, 0, 8));
        copyButton.IsEnabled = canCopyTravelSegment;
        copyButton.Opacity = canCopyTravelSegment ? 1.0 : 0.45;
        copyButton.ToolTip = canCopyTravelSegment
            ? "Haltestellenbereich der aktuellen Route in eine neue Route übernehmen."
            : "Bitte zuerst eine Route auswählen.";
        copyButton.Click += (_, _) =>
        {
            if (!canCopyTravelSegment)
            {
                return;
            }

            Choice = FahrplanActionChoice.CopyTravelSegment;
            DialogResult = true;
            Close();
        };
        root.Children.Add(copyButton);

        var cancelButton = new Button
        {
            Content = "Abbrechen",
            MinHeight = 36,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9)),
            Cursor = System.Windows.Input.Cursors.Hand,
            IsCancel = true
        };
        cancelButton.Click += (_, _) =>
        {
            Choice = FahrplanActionChoice.Cancel;
            DialogResult = false;
            Close();
        };
        root.Children.Add(cancelButton);

        Content = root;
    }

    private static Button CreateActionButton(string text, bool filled, Thickness margin)
    {
        var accent = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
        return new Button
        {
            Content = text,
            Margin = margin,
            MinHeight = 44,
            Padding = new Thickness(16, 12, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = filled ? accent : Brushes.Transparent,
            Foreground = filled ? Brushes.White : accent,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
    }
}
