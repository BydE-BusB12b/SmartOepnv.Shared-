using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public partial class TicketingLibraryViewModel : EditorStatusViewModelBase, IEditorAreaViewModel
{
    private const double PreviewScale = 6.0; // 1 mm → 6 px in Vorschau

    private readonly ObservableCollection<TicketProductItem> _products = [];
    private bool _dirty;
    private bool _loading;
    private TicketProductItem? _subscribedProduct;
    private TicketTemplateFieldItem? _subscribedField;
    private TicketTemplateDocument _template = TicketTemplateDocument.CreateDefault();

    [ObservableProperty] private TicketProductItem? selectedProduct;
    [ObservableProperty] private TicketTemplateFieldItem? selectedField;
    [ObservableProperty] private double pageWidthMm = 58;
    [ObservableProperty] private double pageHeightMm = 50;
    [ObservableProperty] private string backgroundColor = "#FFFFFF";
    [ObservableProperty] private double previewWidthPx = 58 * PreviewScale;
    [ObservableProperty] private double previewHeightPx = 50 * PreviewScale;
    [ObservableProperty] private Brush previewBackground = Brushes.White;

    public ObservableCollection<TicketProductItem> Products => _products;
    public ObservableCollection<TicketTemplateFieldItem> TemplateFields { get; } = [];

    /// <summary>Anzeige Deutsch, Wert bleibt technisch (JSON).</summary>
    public IReadOnlyList<TicketingChoiceOption> FieldTypeOptions { get; } =
    [
        new("logo", "Logo"),
        new("ticketName", "Ticketname"),
        new("price", "Preis"),
        new("priceLevel", "Preisstufe-Label"),
        new("numberChain", "Zahlenkette"),
        new("staticText", "Fester Text"),
        new("stampStripes", "Stempelstreifen")
    ];

    public IReadOnlyList<TicketingChoiceOption> AlignOptions { get; } =
    [
        new("left", "Links"),
        new("center", "Mittig"),
        new("right", "Rechts")
    ];

    public IReadOnlyList<TicketingChoiceOption> FontWeightOptions { get; } =
    [
        new("normal", "Normal"),
        new("bold", "Fett")
    ];

    public TicketingLibraryViewModel()
        : base("Museums-Ticketprodukte und Druckvorlage (Ticketing).")
    {
        TemplateFields.CollectionChanged += OnTemplateFieldsCollectionChanged;
    }

    public bool HasPendingChanges => _dirty;

    public void RefreshFromEditorIfNeeded() => RefreshFromEditor();

    public void RefreshFromEditor()
    {
        UnsubscribeProduct();
        UnsubscribeField();
        _products.Clear();
        SelectedProduct = null;
        TemplateFields.Clear();
        SelectedField = null;
        _dirty = false;
        var editor = AppServices.IsInitialized ? AppServices.Routes.Editor : null;
        if (editor is null)
        {
            StatusMessage = "Kein Paket geladen.";
            return;
        }

        var sourceProducts = editor.TicketProducts;
        if (sourceProducts.Count == 0)
        {
            foreach (var p in TicketProductEditor.CreateDefaultProducts())
            {
                sourceProducts.Add(p);
            }
            editor.ReplaceTicketProducts(sourceProducts.ToList());
            _dirty = true;
        }

        foreach (var p in sourceProducts
                     .OrderBy(x => x.SortOrder)
                     .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            _products.Add(CloneProduct(p));
        }

        SelectedProduct = _products.FirstOrDefault(p => p.IsDefault) ?? _products.FirstOrDefault();

        var seededProducts = _dirty;
        _template = TicketTemplateEditor.Clone(editor.TicketTemplate);
        MaterializeLogoAssetsToWorkspace(_template);
        ApplyTemplateToUi(_template);
        if (seededProducts) _dirty = true;

        StatusMessage = _dirty
            ? $"{_products.Count} Standard-Ticket(s) geladen – bitte Speichern."
            : $"{_products.Count} Produkt(e), Druckvorlage {_template.Fields.Count} Feld(er).";
    }

    public void CommitChangesIfDirty()
    {
        if (!_dirty) return;
        var editor = AppServices.IsInitialized ? AppServices.Routes.Editor : null;
        if (editor is null)
        {
            ReportSaveError("Kein Paket geladen.");
            return;
        }

        TicketProductEditor.EnsureSingleDefault(_products);
        editor.ReplaceTicketProducts(_products.Select(CloneProduct).ToList());
        editor.ReplaceTicketTemplate(BuildTemplateFromUi());
        AppServices.Routes.ApplyEditorChanges("ticketing", rebuildEmbeddedMedia: false);
        _dirty = false;
        ReportSaveSuccess(
            $"{_products.Count} Produkt(e) + Druckvorlage gespeichert (ticketProducts / ticketTemplate).");
    }

    [RelayCommand]
    private void AddProduct()
    {
        var p = new TicketProductItem
        {
            Name = "Neues Ticket",
            PriceCents = 800,
            IsDefault = _products.Count == 0,
            Active = true,
            SortOrder = _products.Count
        };
        _products.Add(p);
        SelectedProduct = p;
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveProduct()
    {
        if (SelectedProduct is null) return;
        var wasDefault = SelectedProduct.IsDefault;
        _products.Remove(SelectedProduct);
        SelectedProduct = _products.FirstOrDefault();
        if (wasDefault && SelectedProduct is not null)
        {
            SelectedProduct.IsDefault = true;
        }
        MarkDirty();
    }

    [RelayCommand]
    private void SetAsDefault()
    {
        if (SelectedProduct is null) return;
        foreach (var p in _products)
        {
            p.IsDefault = ReferenceEquals(p, SelectedProduct);
        }
        MarkDirty();
    }

    [RelayCommand]
    private void Save()
    {
        StatusMessage = "Speichern…";
        UiDispatch.RunAfterPaint(CommitChangesIfDirty);
    }

    [RelayCommand]
    private void AddTemplateField()
    {
        var f = new TicketTemplateFieldItem
        {
            Type = "staticText",
            X = 4,
            Y = 4,
            Width = 20,
            Height = 6,
            FontSize = 10,
            StaticText = "Text"
        };
        TemplateFields.Add(f);
        SelectedField = f;
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveTemplateField()
    {
        if (SelectedField is null) return;
        TemplateFields.Remove(SelectedField);
        SelectedField = TemplateFields.FirstOrDefault();
        MarkDirty();
    }

    [RelayCommand]
    private void ResetTemplateDefault()
    {
        ApplyTemplateToUi(TicketTemplateDocument.CreateDefault());
        MarkDirty();
        StatusMessage = "Standard-Druckvorlage geladen (58×50 mm, Stempelbereich rechts mit Trennlinie). Speichern nicht vergessen.";
    }

    /// <summary>Bilddatei wählen und dem Logo-Feld zuweisen (erscheint in der Vorschau).</summary>
    [RelayCommand]
    private void ChooseTicketLogo()
    {
        if (SelectedField is null)
        {
            StatusMessage = "Zuerst das Logo-Feld in der Liste auswählen.";
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Ticket-Logo auswählen",
            Filter = "Bilder (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Alle Dateien (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var preferred = string.IsNullOrWhiteSpace(SelectedField.AssetKey)
                ? PlanerTicketLogoWorkspace.DefaultFileName
                : SelectedField.AssetKey;
            var fileName = PlanerTicketLogoWorkspace.SaveFromFile(
                AppServices.SettingsSubfolder,
                dialog.FileName,
                preferred);
            if (!string.Equals(SelectedField.Type, "logo", StringComparison.OrdinalIgnoreCase))
                SelectedField.Type = "logo";
            // AssetKey kurz leeren, damit die Vorschau die Datei neu lädt (gleicher Name möglich)
            SelectedField.AssetKey = string.Empty;
            SelectedField.AssetKey = fileName;
            MarkDirty();
            StatusMessage = $"Logo „{fileName}“ übernommen – Speichern nicht vergessen.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Logo konnte nicht geladen werden: {ex.Message}";
        }
    }

    partial void OnSelectedProductChanged(TicketProductItem? value)
    {
        UnsubscribeProduct();
        _subscribedProduct = value;
        if (_subscribedProduct is not null)
        {
            _subscribedProduct.PropertyChanged += OnProductPropertyChanged;
        }
    }

    partial void OnSelectedFieldChanged(TicketTemplateFieldItem? value)
    {
        UnsubscribeField();
        _subscribedField = value;
        if (_subscribedField is not null)
        {
            _subscribedField.PropertyChanged += OnFieldPropertyChanged;
        }
    }

    partial void OnPageWidthMmChanged(double value)
    {
        PreviewWidthPx = Math.Max(40, value * PreviewScale);
        if (!_loading) MarkDirty();
    }

    partial void OnPageHeightMmChanged(double value)
    {
        PreviewHeightPx = Math.Max(40, value * PreviewScale);
        if (!_loading) MarkDirty();
    }

    partial void OnBackgroundColorChanged(string value)
    {
        PreviewBackground = TryParseBrush(value);
        if (!_loading) MarkDirty();
    }

    private void ApplyTemplateToUi(TicketTemplateDocument doc)
    {
        _loading = true;
        try
        {
            UnsubscribeField();
            SelectedField = null;
            TemplateFields.Clear();
            foreach (var f in doc.Fields)
            {
                TemplateFields.Add(TicketTemplateEditor.CloneField(f));
            }

            PageWidthMm = doc.PageWidthMm;
            PageHeightMm = doc.PageHeightMm;
            BackgroundColor = doc.BackgroundColor;
            PreviewWidthPx = doc.PageWidthMm * PreviewScale;
            PreviewHeightPx = doc.PageHeightMm * PreviewScale;
            PreviewBackground = TryParseBrush(doc.BackgroundColor);
            SelectedField = TemplateFields.FirstOrDefault();
            _dirty = false;
        }
        finally
        {
            _loading = false;
        }
    }

    private TicketTemplateDocument BuildTemplateFromUi()
    {
        var doc = new TicketTemplateDocument
        {
            Version = 1,
            PageWidthMm = PageWidthMm,
            PageHeightMm = PageHeightMm,
            BackgroundColor = BackgroundColor
        };
        foreach (var f in TemplateFields)
        {
            doc.Fields.Add(TicketTemplateEditor.CloneField(f));
        }

        // Logo-Dateien aus dem Workspace in die Vorlage einbetten (Sync zum Fahrzeug).
        foreach (var f in TemplateFields.Where(x =>
                     string.Equals(x.Type, "logo", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(x.AssetKey)))
        {
            var bytes = PlanerTicketLogoWorkspace.TryReadBytes(AppServices.SettingsSubfolder, f.AssetKey);
            if (bytes is { Length: > 0 })
                doc.LogoAssets[Path.GetFileName(f.AssetKey.Trim())] = bytes;
        }

        return doc;
    }

    private static void MaterializeLogoAssetsToWorkspace(TicketTemplateDocument doc)
    {
        foreach (var kv in doc.LogoAssets)
        {
            if (kv.Value is null || kv.Value.Length == 0) continue;
            PlanerTicketLogoWorkspace.SaveBytes(AppServices.SettingsSubfolder, kv.Key, kv.Value);
        }
    }

    private void OnTemplateFieldsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (TicketTemplateFieldItem f in e.NewItems)
            {
                f.PropertyChanged += OnFieldPropertyChanged;
            }
        }
        if (e.OldItems is not null)
        {
            foreach (TicketTemplateFieldItem f in e.OldItems)
            {
                f.PropertyChanged -= OnFieldPropertyChanged;
            }
        }
        if (!_loading && e.Action != NotifyCollectionChangedAction.Reset)
        {
            MarkDirty();
        }
    }

    private void UnsubscribeProduct()
    {
        if (_subscribedProduct is null) return;
        _subscribedProduct.PropertyChanged -= OnProductPropertyChanged;
        _subscribedProduct = null;
    }

    private void UnsubscribeField()
    {
        if (_subscribedField is null) return;
        _subscribedField.PropertyChanged -= OnFieldPropertyChanged;
        _subscribedField = null;
    }

    private void OnProductPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TicketProductItem.IsDefault) &&
            sender is TicketProductItem changed &&
            changed.IsDefault)
        {
            foreach (var p in _products)
            {
                if (!ReferenceEquals(p, changed) && p.IsDefault)
                {
                    p.IsDefault = false;
                }
            }
        }
        MarkDirty();
    }

    private void OnFieldPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        MarkDirty();

    private void MarkDirty()
    {
        if (_loading) return;
        _dirty = true;
        StatusMessage = "Ungespeicherte Änderungen – Speichern oder Bereich wechseln.";
    }

    private static Brush TryParseBrush(string? hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex ?? "#FFFFFF");
            return new SolidColorBrush(c);
        }
        catch
        {
            return Brushes.White;
        }
    }

    private static TicketProductItem CloneProduct(TicketProductItem source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        PriceCents = source.PriceCents,
        IsDefault = source.IsDefault,
        Active = source.Active,
        SortOrder = source.SortOrder
    };
}

/// <summary>Auswahl mit deutscher Beschriftung und technischem Speicherwert.</summary>
public sealed record TicketingChoiceOption(string Value, string Label)
{
    public override string ToString() => Label;
}
