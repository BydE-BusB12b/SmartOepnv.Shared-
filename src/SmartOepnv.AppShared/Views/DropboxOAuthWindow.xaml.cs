using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using SmartOepnv.Core;
using SmartOepnv.Core.Dropbox;

namespace SmartOepnv.AppShared.Views;

public partial class DropboxOAuthWindow : Window
{
    public DropboxOAuthWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = AppPaths.GetWebView2UserDataDirectory(AppServices.SettingsSubfolder);
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await OAuthWebView.EnsureCoreWebView2Async(environment);
            OAuthWebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            OAuthWebView.Source = new Uri(AppServices.Dropbox.BuildAuthorizeUrl());
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"WebView2 konnte nicht gestartet werden:\n{ex.Message}",
                "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            Close();
        }
    }

    private async void OnNavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
    {
        if (!args.Uri.StartsWith(DropboxConstants.OAuthRedirectUri, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var uri = new Uri(args.Uri);
        var error = GetQueryParameter(uri, "error");
        if (!string.IsNullOrEmpty(error))
        {
            MessageBox.Show(this, $"Dropbox-Fehler: {error}", "OAuth", MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
            Close();
            return;
        }

        var code = GetQueryParameter(uri, "code");
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        args.Cancel = true;
        try
        {
            await AppServices.Dropbox.ExchangeCodeForTokensAsync(code);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Token-Austausch fehlgeschlagen",
                MessageBoxButton.OK, MessageBoxImage.Error);
            DialogResult = false;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string? GetQueryParameter(Uri uri, string key)
    {
        if (string.IsNullOrEmpty(uri.Query))
        {
            return null;
        }

        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && string.Equals(Uri.UnescapeDataString(kv[0]), key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }
}
