using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartOepnv.AppShared.ViewModels;

/// <summary>Gemeinsame Statuszeile für Planer-Bereiche mit Speichern-Button.</summary>
public abstract partial class EditorStatusViewModelBase : ObservableObject
{
    private bool _reportingSaveSuccess;

    protected EditorStatusViewModelBase(string initialStatusMessage = "") =>
        StatusMessage = initialStatusMessage;

    [ObservableProperty] private string statusMessage = string.Empty;

    [ObservableProperty] private bool statusMessageIsSuccess;

    protected void ReportSaveSuccess(string message)
    {
        _reportingSaveSuccess = true;
        StatusMessage = message;
        StatusMessageIsSuccess = true;
        _reportingSaveSuccess = false;
    }

    protected void ReportSaveError(string message)
    {
        StatusMessage = message;
        StatusMessageIsSuccess = false;
    }

    partial void OnStatusMessageChanged(string value)
    {
        if (!_reportingSaveSuccess)
        {
            StatusMessageIsSuccess = false;
        }
    }
}
