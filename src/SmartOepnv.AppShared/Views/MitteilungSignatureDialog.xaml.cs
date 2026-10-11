using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.AppShared.ViewModels;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

public partial class MitteilungSignatureDialog : Window
{
    private readonly MitteilungViewModel _ownerVm;

    public string? SelectedSignatureId { get; private set; }

    public MitteilungSignatureDialog(MitteilungViewModel ownerVm)
    {
        _ownerVm = ownerVm;
        InitializeComponent();
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);
        SignatureInk.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Colors.Black,
            Width = 2.4,
            Height = 2.4,
            FitToCurve = true,
            IgnorePressure = false
        };
        ReloadList();
    }

    private void ReloadList()
    {
        SignatureList.Items.Clear();
        if (!AppServices.IsInitialized)
        {
            return;
        }

        foreach (var signature in PlanerMitteilungSignaturesWorkspace.GetSignatures(AppServices.SettingsSubfolder))
        {
            SignatureList.Items.Add(signature);
        }
    }

    private void SelectEntry(MitteilungSignatureEntry entry)
    {
        foreach (MitteilungSignatureEntry item in SignatureList.Items)
        {
            if (string.Equals(item.Id, entry.Id, StringComparison.Ordinal))
            {
                SignatureList.SelectedItem = item;
                break;
            }
        }
    }

    private void SaveDrawingButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AppServices.IsInitialized)
        {
            MessageBox.Show(this, "Planer ist nicht bereit.", "Unterschrift", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SignatureInk.Strokes.Count == 0)
        {
            MessageBox.Show(
                this,
                "Bitte zuerst unterschreiben (auf dem weißen Feld zeichnen).",
                "Unterschrift",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var png = RenderInkToPng(SignatureInk);
            var name = string.IsNullOrWhiteSpace(NewNameBox.Text) ? "Unterschrift" : NewNameBox.Text.Trim();
            var entry = PlanerMitteilungSignaturesWorkspace.AddFromPngBytes(
                AppServices.SettingsSubfolder,
                png,
                name);
            ReloadList();
            _ownerVm.ReloadSignatures();
            SelectEntry(entry);
            SignatureInk.Strokes.Clear();
            NewNameBox.Text = string.Empty;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unterschrift", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ClearDrawingButton_Click(object sender, RoutedEventArgs e) =>
        SignatureInk.Strokes.Clear();

    private static byte[] RenderInkToPng(InkCanvas canvas)
    {
        var width = Math.Max(1, (int)Math.Ceiling(canvas.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(canvas.ActualHeight));
        var dpi = 96.0;
        var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(canvas);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Unterschrift als Bild wählen",
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.webp|Alle Dateien|*.*"
        };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(NewNameBox.Text)
            ? Path.GetFileNameWithoutExtension(dialog.FileName)
            : NewNameBox.Text.Trim();

        try
        {
            var entry = PlanerMitteilungSignaturesWorkspace.AddFromFile(
                AppServices.SettingsSubfolder,
                dialog.FileName,
                name);
            ReloadList();
            _ownerVm.ReloadSignatures();
            SelectEntry(entry);
            NewNameBox.Text = string.Empty;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unterschrift", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SignatureList.SelectedItem is not MitteilungSignatureEntry entry)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                $"Unterschrift „{entry.Name}“ wirklich löschen?",
                "Unterschrift",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        PlanerMitteilungSignaturesWorkspace.TryDelete(AppServices.SettingsSubfolder, entry.Id);
        ReloadList();
        _ownerVm.ReloadSignatures();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedSignatureId = SignatureList.SelectedItem is MitteilungSignatureEntry entry
            ? entry.Id
            : string.Empty;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SignatureList.SelectedItem = null;
        SelectedSignatureId = string.Empty;
        DialogResult = true;
        Close();
    }
}
