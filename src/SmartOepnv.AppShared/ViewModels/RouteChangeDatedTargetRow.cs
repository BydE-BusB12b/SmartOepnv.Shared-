using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartOepnv.AppShared.ViewModels;

/// <summary>Zeile für datumsabhängiges Routenwechselziel im Halt-Editor.</summary>
public sealed class RouteChangeDatedTargetRow : ObservableObject
{
    private readonly Action _onChanged;
    private string _datesText;
    private string? _selectedTrip;

    public RouteChangeDatedTargetRow(
        int sourceIndex,
        string datesText,
        string? selectedTrip,
        string tripValue,
        Action onChanged)
    {
        SourceIndex = sourceIndex;
        _datesText = datesText;
        _selectedTrip = selectedTrip;
        TripValue = tripValue;
        _onChanged = onChanged;
    }

    public int SourceIndex { get; }

    public string TripValue { get; private set; }

    public string DatesText
    {
        get => _datesText;
        set
        {
            if (SetProperty(ref _datesText, value))
            {
                OnPropertyChanged(nameof(SummaryText));
                _onChanged();
            }
        }
    }

    public string? SelectedTrip
    {
        get => _selectedTrip;
        set
        {
            if (!SetProperty(ref _selectedTrip, value))
            {
                return;
            }

            TripValue = value ?? string.Empty;
            OnPropertyChanged(nameof(TripValue));
            OnPropertyChanged(nameof(SummaryText));
            _onChanged();
        }
    }

    public string SummaryText
    {
        get
        {
            var dates = string.IsNullOrWhiteSpace(DatesText) ? "—" : DatesText.Trim();
            var trip = string.IsNullOrWhiteSpace(SelectedTrip) ||
                       string.Equals(SelectedTrip, "Keine Fahrt ausgewählt", StringComparison.Ordinal)
                ? "—"
                : SelectedTrip!;
            return $"{dates}  →  {trip}";
        }
    }
}
