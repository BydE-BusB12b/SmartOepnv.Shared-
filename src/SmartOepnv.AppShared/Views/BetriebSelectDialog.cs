using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core;
using SmartOepnv.Core.Betrieb;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Views;

/// <summary>Betrieb auswählen oder neuen leeren Betrieb mit eigenem Dropbox-Ordner anlegen.</summary>
public sealed class BetriebSelectDialog : Window
{
    private readonly ListBox _list;
    private readonly TextBox _newName;
    private readonly TextBox _newFolder;
    private readonly TextBlock _status;

    public string? SelectedBetriebId { get; private set; }
    public bool CreateNew { get; private set; }
    public string? NewDisplayName { get; private set; }
    public string? NewDropboxFolderPath { get; private set; }

    public BetriebSelectDialog(Window owner)
    {
        Owner = owner;
        Title = "Betrieb auswählen";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowTitleBarHelper.ApplyDarkWindowBackground(this);
        WindowTitleBarHelper.ApplySmartOepnvTitleBar(this);

        var activeId = BetriebProfileStore.GetActiveProfile()?.Id;
        var profiles = BetriebProfileStore.ListProfiles().ToList();

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Betrieb auswählen",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text =
                "Jeder Betrieb hat einen eigenen Dropbox-Ordner und einen eigenen lokalen Arbeitsstand. " +
                "Der aktuelle Betrieb bleibt vollständig gespeichert.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _list = new ListBox
        {
            Height = 200,
            Margin = new Thickness(0, 0, 0, 12),
            DisplayMemberPath = nameof(BetriebProfile.ListLabel),
            ItemsSource = profiles
        };
        if (profiles.Count > 0)
        {
            var active = profiles.FirstOrDefault(p => p.Id == activeId) ?? profiles[0];
            _list.SelectedItem = active;
        }

        root.Children.Add(_list);

        root.Children.Add(new TextBlock
        {
            Text = "Neuen Betrieb hinzufügen",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 8)
        });
        root.Children.Add(new TextBlock
        {
            Text =
                "Leert den Planer (Anzeigen, Fahrer, Routen …) und speichert neue Daten im neuen Dropbox-Ordner.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 8)
        });

        _newName = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        MaterialDesignThemes.Wpf.HintAssist.SetHint(_newName, "Name (z. B. Stadtwerke Musterstadt)");
        root.Children.Add(_newName);

        _newFolder = new TextBox { Margin = new Thickness(0, 0, 0, 8), Tag = true };
        MaterialDesignThemes.Wpf.HintAssist.SetHint(_newFolder, "Dropbox-Ordnerpfad (z. B. /stadtwerke-musterstadt)");
        _newName.TextChanged += (_, _) =>
        {
            if (_newFolder.Tag as bool? == true)
            {
                _newFolder.Text = BetriebProfileStore.SuggestFolderPath(_newName.Text);
            }
        };
        _newFolder.GotKeyboardFocus += (_, _) => _newFolder.Tag = false;
        root.Children.Add(_newFolder);

        _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0xCA, 0xF9))
        };
        root.Children.Add(_status);

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var cancel = new Button
        {
            Content = "Abbrechen",
            IsCancel = true,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(14, 8, 14, 8),
            MinHeight = 36
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var create = new Button
        {
            Content = "Neu anlegen & wechseln",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(14, 8, 14, 8),
            MinHeight = 36
        };
        create.Click += (_, _) => OnCreate();

        var select = new Button
        {
            Content = "Auswählen",
            IsDefault = true,
            Padding = new Thickness(14, 8, 14, 8),
            MinHeight = 36
        };
        select.Click += (_, _) => OnSelect();

        bar.Children.Add(cancel);
        bar.Children.Add(create);
        bar.Children.Add(select);
        root.Children.Add(bar);

        Content = root;
        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x16, 0x28));
        Foreground = Brushes.White;
    }

    private void OnSelect()
    {
        if (_list.SelectedItem is not BetriebProfile profile)
        {
            _status.Text = "Bitte einen vorhandenen Betrieb wählen.";
            return;
        }

        var active = BetriebProfileStore.GetActiveProfile();
        if (active is not null && string.Equals(active.Id, profile.Id, StringComparison.Ordinal))
        {
            _status.Text = "Dieser Betrieb ist bereits aktiv.";
            return;
        }

        if (!SmartConfirmDialog.ShowConfirm(
                this,
                Title,
                $"Zu „{profile.DisplayName}“ wechseln?\n\nDer Planer speichert den aktuellen Betrieb und startet neu."))
        {
            return;
        }

        SelectedBetriebId = profile.Id;
        CreateNew = false;
        DialogResult = true;
        Close();
    }

    private void OnCreate()
    {
        var name = _newName.Text.Trim();
        var folder = DropboxConstants.NormalizeFolderPath(_newFolder.Text);
        if (string.IsNullOrWhiteSpace(name))
        {
            _status.Text = "Bitte einen Namen für den neuen Betrieb eingeben.";
            return;
        }

        if (string.IsNullOrWhiteSpace(folder) || folder == "/")
        {
            _status.Text = "Bitte einen Dropbox-Ordnerpfad angeben (z. B. /mein-betrieb).";
            return;
        }

        var existing = BetriebProfileStore.ListProfiles();
        if (existing.Any(p =>
                string.Equals(
                    DropboxConstants.NormalizeFolderPath(p.DropboxFolderPath),
                    folder,
                    StringComparison.OrdinalIgnoreCase)))
        {
            _status.Text = "Dieser Dropbox-Ordner ist bereits einem Betrieb zugeordnet.";
            return;
        }

        if (!SmartConfirmDialog.ShowConfirm(
                this,
                Title,
                $"Neuen Betrieb „{name}“ anlegen?\n\n" +
                $"Dropbox: {folder}\n\n" +
                "Der Planer wird geleert (neuer leerer Arbeitsstand) und startet neu. " +
                "Der bisherige Betrieb bleibt gespeichert."))
        {
            return;
        }

        CreateNew = true;
        NewDisplayName = name;
        NewDropboxFolderPath = folder;
        DialogResult = true;
        Close();
    }
}
