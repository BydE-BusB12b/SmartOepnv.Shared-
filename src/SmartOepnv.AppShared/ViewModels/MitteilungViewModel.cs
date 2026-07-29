using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartOepnv.AppShared.Mitteilungen;
using SmartOepnv.Core;
using SmartOepnv.Core.Mitteilungen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

public sealed class MitteilungLogoOption
{
    public MitteilungLogoOption(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }

    public static MitteilungLogoOption None { get; } = new(string.Empty, "(Kein Firmenlogo)");
}

public sealed class MitteilungSignatureOption
{
    public MitteilungSignatureOption(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }

    public static MitteilungSignatureOption None { get; } = new(string.Empty, "(Keine Unterschrift)");
}

public partial class MitteilungViewModel : ObservableObject
{
    private string? _loadedDraftId;

    public MitteilungViewModel()
    {
        ReloadCompanyLogos();
        ReloadSignatures();
        ReloadDraftList();
        ValidFromText = DateTime.Today.ToString("dd.MM.yyyy");
        SignerNameAndDate = DateTime.Today.ToString("dd.MM.yyyy");
        StatusMessage = "Mitteilung ausfüllen, speichern und als PDF erstellen.";
    }

    public ObservableCollection<MitteilungLogoOption> CompanyLogoOptions { get; } = [];
    public ObservableCollection<MitteilungSignatureOption> SignatureOptions { get; } = [];
    public ObservableCollection<MitteilungDraft> SavedDrafts { get; } = [];

    public bool CanUpdateLoadedDraft => !string.IsNullOrEmpty(_loadedDraftId);

    [ObservableProperty] private string draftName = string.Empty;
    [ObservableProperty] private MitteilungDraft? selectedDraft;
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string body = string.Empty;
    [ObservableProperty] private string validFromText = string.Empty;
    [ObservableProperty] private string validToText = string.Empty;
    [ObservableProperty] private bool untilRevoked;
    [ObservableProperty] private bool showSmartOepnvLogo = true;
    [ObservableProperty] private string selectedCompanyLogoId = string.Empty;
    [ObservableProperty] private string signerNameAndDate = string.Empty;
    [ObservableProperty] private string selectedSignatureId = string.Empty;
    [ObservableProperty] private string selectedSignatureName = "(Keine Unterschrift)";
    [ObservableProperty] private string statusMessage = string.Empty;

    public bool IsValidToEnabled => !UntilRevoked;

    public string ValidToDisplayHint => UntilRevoked ? "bis auf Widerruf" : string.Empty;

    partial void OnUntilRevokedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsValidToEnabled));
        OnPropertyChanged(nameof(ValidToDisplayHint));
        if (value)
        {
            ValidToText = string.Empty;
        }
    }

    partial void OnSelectedSignatureIdChanged(string value)
    {
        SelectedSignatureName = SignatureOptions
            .FirstOrDefault(s => string.Equals(s.Id, value, StringComparison.Ordinal))
            ?.Name
            ?? MitteilungSignatureOption.None.Name;
    }

    partial void OnSelectedDraftChanged(MitteilungDraft? value)
    {
        if (value is not null)
        {
            DraftName = value.Name;
        }
    }

    public void ReloadCompanyLogos()
    {
        CompanyLogoOptions.Clear();
        CompanyLogoOptions.Add(MitteilungLogoOption.None);
        if (!AppServices.IsInitialized)
        {
            SelectedCompanyLogoId = string.Empty;
            return;
        }

        foreach (var logo in PlanerBrandingWorkspace.GetLogos(AppServices.SettingsSubfolder))
        {
            CompanyLogoOptions.Add(new MitteilungLogoOption(logo.Id, logo.Name));
        }

        if (!CompanyLogoOptions.Any(o => string.Equals(o.Id, SelectedCompanyLogoId, StringComparison.Ordinal)))
        {
            SelectedCompanyLogoId = CompanyLogoOptions.FirstOrDefault()?.Id ?? string.Empty;
        }
    }

    public void ReloadSignatures()
    {
        var previous = SelectedSignatureId;
        SignatureOptions.Clear();
        SignatureOptions.Add(MitteilungSignatureOption.None);
        if (!AppServices.IsInitialized)
        {
            SelectedSignatureId = string.Empty;
            return;
        }

        foreach (var signature in PlanerMitteilungSignaturesWorkspace.GetSignatures(AppServices.SettingsSubfolder))
        {
            SignatureOptions.Add(new MitteilungSignatureOption(signature.Id, signature.Name));
        }

        SelectedSignatureId = SignatureOptions.Any(s => string.Equals(s.Id, previous, StringComparison.Ordinal))
            ? previous
            : string.Empty;
    }

    public void RefreshFromEditor()
    {
        ReloadCompanyLogos();
        ReloadSignatures();
        ReloadDraftList();
    }

    private MitteilungDraftStore? TryGetDraftStore()
    {
        if (!AppServices.IsInitialized || AppServices.MitteilungDrafts is null)
        {
            StatusMessage = "Lokale Vorlagen sind nur im Planer verfügbar.";
            return null;
        }

        return AppServices.MitteilungDrafts;
    }

    private void ReloadDraftList()
    {
        SavedDrafts.Clear();
        if (!AppServices.IsInitialized || AppServices.MitteilungDrafts is null)
        {
            return;
        }

        foreach (var draft in AppServices.MitteilungDrafts.LoadAll())
        {
            SavedDrafts.Add(draft);
        }

        if (SelectedDraft is not null)
        {
            SelectedDraft = SavedDrafts.FirstOrDefault(d => d.Id == SelectedDraft.Id);
        }
    }

    private void NotifyDraftCommands()
    {
        OnPropertyChanged(nameof(CanUpdateLoadedDraft));
        UpdateDraftCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SaveDraftAsNew() => PersistDraft(Guid.NewGuid().ToString("N"), isNew: true);

    [RelayCommand(CanExecute = nameof(CanUpdateLoadedDraft))]
    private void UpdateDraft()
    {
        if (_loadedDraftId is null)
        {
            StatusMessage = "Keine geladene Vorlage – bitte „Speichern“ verwenden.";
            return;
        }

        PersistDraft(_loadedDraftId, isNew: false);
    }

    private void PersistDraft(string id, bool isNew)
    {
        var store = TryGetDraftStore();
        if (store is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Body))
        {
            StatusMessage = "Nichts zu speichern – bitte Überschrift oder Mitteilungstext eingeben.";
            return;
        }

        var name = DraftName.Trim();
        if (name.Length == 0)
        {
            name = MitteilungDraft.SuggestName(Title);
        }

        if (isNew && SavedDrafts.Any(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{name} ({DateTime.Now:dd.MM.yyyy HH:mm})";
        }
        else if (!isNew &&
                 SavedDrafts.Any(d =>
                     d.Id != id &&
                     string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"Der Name „{name}“ ist bereits vergeben.";
            return;
        }

        var draft = BuildDraftFromEditor(id);
        draft.Name = name;
        store.Save(draft);
        _loadedDraftId = draft.Id;
        DraftName = draft.Name;
        ReloadDraftList();
        SelectedDraft = SavedDrafts.FirstOrDefault(d => d.Id == draft.Id);
        NotifyDraftCommands();
        StatusMessage = isNew
            ? $"Mitteilung „{draft.Name}“ gespeichert."
            : $"Mitteilung „{draft.Name}“ aktualisiert.";
    }

    private MitteilungDraft BuildDraftFromEditor(string id) => new()
    {
        Id = id,
        Title = Title.Trim(),
        Body = Body.Trim(),
        ValidFrom = ValidFromText.Trim(),
        ValidTo = ValidToText.Trim(),
        UntilRevoked = UntilRevoked,
        ShowSmartOepnvLogo = ShowSmartOepnvLogo,
        CompanyLogoId = string.IsNullOrWhiteSpace(SelectedCompanyLogoId) ? null : SelectedCompanyLogoId,
        SignerNameAndDate = SignerNameAndDate.Trim(),
        SignatureId = string.IsNullOrWhiteSpace(SelectedSignatureId) ? null : SelectedSignatureId
    };

    [RelayCommand]
    private void LoadDraft()
    {
        if (SelectedDraft is null)
        {
            StatusMessage = "Bitte zuerst eine gespeicherte Mitteilung auswählen.";
            return;
        }

        ApplyDraft(SelectedDraft);
        NotifyDraftCommands();
        StatusMessage = $"Mitteilung „{SelectedDraft.Name}“ geladen.";
    }

    private void ApplyDraft(MitteilungDraft draft)
    {
        _loadedDraftId = draft.Id;
        DraftName = draft.Name;
        Title = draft.Title;
        Body = draft.Body;
        ValidFromText = draft.ValidFrom;
        ValidToText = draft.ValidTo;
        UntilRevoked = draft.UntilRevoked;
        ShowSmartOepnvLogo = draft.ShowSmartOepnvLogo;
        SelectedCompanyLogoId = draft.CompanyLogoId ?? string.Empty;
        SignerNameAndDate = draft.SignerNameAndDate;
        SelectedSignatureId = draft.SignatureId ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(SelectedSignatureId) &&
            SignatureOptions.All(s => !string.Equals(s.Id, SelectedSignatureId, StringComparison.Ordinal)))
        {
            SelectedSignatureId = string.Empty;
        }
    }

    [RelayCommand]
    private void DeleteDraft()
    {
        var store = TryGetDraftStore();
        if (store is null || SelectedDraft is null)
        {
            StatusMessage = "Bitte zuerst eine Mitteilung zum Löschen auswählen.";
            return;
        }

        var name = SelectedDraft.Name;
        var id = SelectedDraft.Id;
        if (!store.Delete(id))
        {
            StatusMessage = "Mitteilung konnte nicht gelöscht werden.";
            return;
        }

        if (string.Equals(_loadedDraftId, id, StringComparison.Ordinal))
        {
            _loadedDraftId = null;
        }

        ReloadDraftList();
        SelectedDraft = null;
        NotifyDraftCommands();
        StatusMessage = $"Mitteilung „{name}“ gelöscht.";
    }

    [RelayCommand]
    private void NewDraft()
    {
        _loadedDraftId = null;
        DraftName = string.Empty;
        SelectedDraft = null;
        Title = string.Empty;
        Body = string.Empty;
        ValidFromText = DateTime.Today.ToString("dd.MM.yyyy");
        ValidToText = string.Empty;
        UntilRevoked = false;
        ShowSmartOepnvLogo = true;
        SelectedCompanyLogoId = CompanyLogoOptions.FirstOrDefault()?.Id ?? string.Empty;
        SignerNameAndDate = DateTime.Today.ToString("dd.MM.yyyy");
        SelectedSignatureId = string.Empty;
        NotifyDraftCommands();
        StatusMessage = "Neue Mitteilung.";
    }

    [RelayCommand]
    private void ChooseSignature()
    {
        var owner = Application.Current?.MainWindow;
        var dialog = new Views.MitteilungSignatureDialog(this) { Owner = owner };
        if (dialog.ShowDialog() == true)
        {
            ReloadSignatures();
            SelectedSignatureId = dialog.SelectedSignatureId ?? string.Empty;
            StatusMessage = string.IsNullOrWhiteSpace(SelectedSignatureId)
                ? "Keine Unterschrift gewählt."
                : $"Unterschrift „{SelectedSignatureName}“ übernommen.";
        }
    }

    [RelayCommand]
    private void CreatePdf()
    {
        if (string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Body))
        {
            StatusMessage = "Bitte mindestens Überschrift oder Mitteilungstext eingeben.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Mitteilung als PDF speichern",
            Filter = "PDF-Datei (*.pdf)|*.pdf",
            FileName = BuildDefaultFileName(),
            AddExtension = true,
            DefaultExt = ".pdf"
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
        {
            return;
        }

        try
        {
            MitteilungPdfGenerator.Generate(dialog.FileName, new MitteilungPdfModel
            {
                Title = Title.Trim(),
                Body = Body.Trim(),
                ValidFrom = ValidFromText.Trim(),
                ValidTo = ValidToText.Trim(),
                UntilRevoked = UntilRevoked,
                ShowSmartOepnvLogo = ShowSmartOepnvLogo,
                CompanyLogoId = string.IsNullOrWhiteSpace(SelectedCompanyLogoId) ? null : SelectedCompanyLogoId,
                SignerNameAndDate = SignerNameAndDate.Trim(),
                SignatureId = string.IsNullOrWhiteSpace(SelectedSignatureId) ? null : SelectedSignatureId
            });
            StatusMessage = $"PDF gespeichert: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF fehlgeschlagen: {ex.Message}";
            MessageBox.Show(
                Application.Current?.MainWindow,
                ex.Message,
                "Mitteilung",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string BuildDefaultFileName()
    {
        var raw = string.IsNullOrWhiteSpace(Title) ? "Mitteilung" : Title.Trim();
        var chars = raw.Select(ch =>
                char.IsLetterOrDigit(ch) ? ch :
                ch is ' ' or '_' or '-' ? '_' :
                '\0')
            .Where(ch => ch != '\0')
            .Take(40)
            .ToArray();
        var slug = new string(chars).Trim('_');
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "Mitteilung";
        }

        return $"Mitteilung_{slug}_{DateTime.Now:yyyyMMdd}.pdf";
    }
}
