using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.Core;

namespace SmartOepnv.AppShared.ViewModels;

public partial class ZeitwirtschaftPlannerViewModel : ObservableObject
{
    [ObservableProperty] private string statusMessage =
        "Zeitwirtschaft-Tool: lädt zeitwirtschaft_*.json aus Dropbox, führt zusammen, CSV-Export.";

    public void RefreshHint()
    {
        var script = ResolveScriptPath();
        StatusMessage = File.Exists(script)
            ? $"Tool bereit: {script}"
            : "zeitwirtschaft_planer.py nicht gefunden – bitte im GPSAnsagen-Ordner prüfen.";
    }

    [RelayCommand]
    private void OpenZeitwirtschaftTool()
    {
        var script = ResolveScriptPath();
        if (!File.Exists(script))
        {
            StatusMessage = "Python-Tool nicht gefunden: " + script;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "pythonw",
                Arguments = $"\"{script}\"",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(script) ?? Environment.CurrentDirectory
            });
            StatusMessage = "Zeitwirtschaft-Planer gestartet.";
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{script}\"",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(script) ?? Environment.CurrentDirectory
                });
                StatusMessage = "Zeitwirtschaft-Planer gestartet.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Tool konnte nicht gestartet werden: {ex.Message}";
            }
        }
    }

    private static string ResolveScriptPath()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AndroidStudioProjects", "GPSAnsagen", "zeitwirtschaft_planer.py"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "GPSAnsagen", "zeitwirtschaft_planer.py")),
            Path.Combine(AppContext.BaseDirectory, "zeitwirtschaft_planer.py")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return candidates[0];
    }
}
