using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.ViewModels;

/// <summary>Zeile für datums-/wochentagsabhängiges Routenwechselziel im Halt-Editor.</summary>
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
        Action onChanged,
        IEnumerable<DutyOperatingDay>? selectedDays = null)
    {
        SourceIndex = sourceIndex;
        _datesText = datesText;
        _selectedTrip = selectedTrip;
        TripValue = tripValue;
        _onChanged = onChanged;

        var selected = selectedDays?.ToHashSet() ?? [];
        foreach (var (day, name) in DutyOperatingDayHelper.AllDays)
        {
            var item = new OperatingDayOptionItem(day, ShortLabel(day, name));
            item.IsSelected = selected.Contains(day);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(OperatingDayOptionItem.IsSelected))
                {
                    return;
                }

                OnPropertyChanged(nameof(SummaryText));
                _onChanged();
            };
            DaySelections.Add(item);
        }
    }

    public int SourceIndex { get; }

    public string TripValue { get; private set; }

    public ObservableCollection<OperatingDayOptionItem> DaySelections { get; } = [];

    public IReadOnlyList<DutyOperatingDay> SelectedDays =>
        DaySelections.Where(d => d.IsSelected).Select(d => d.Day).ToList();

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
            var dayPart = DutyOperatingDayHelper.FormatDisplay(SelectedDays);
            var dates = string.IsNullOrWhiteSpace(DatesText) ? string.Empty : DatesText.Trim();
            var whenParts = new List<string>();
            if (!string.IsNullOrEmpty(dayPart))
            {
                whenParts.Add(dayPart);
            }

            if (!string.IsNullOrEmpty(dates))
            {
                whenParts.Add(dates);
            }

            var when = whenParts.Count == 0 ? "—" : string.Join(" · ", whenParts);
            var trip = string.IsNullOrWhiteSpace(SelectedTrip) ||
                       string.Equals(SelectedTrip, RouteStopEditorCatalog.NoLineCourseTripLabel, StringComparison.Ordinal)
                ? "—"
                : SelectedTrip!;
            return $"{when}  →  {trip}";
        }
    }

    private static string ShortLabel(DutyOperatingDay day, string fullName) =>
        day switch
        {
            DutyOperatingDay.Monday => "Mo",
            DutyOperatingDay.Tuesday => "Di",
            DutyOperatingDay.Wednesday => "Mi",
            DutyOperatingDay.Thursday => "Do",
            DutyOperatingDay.Friday => "Fr",
            DutyOperatingDay.Saturday => "Sa",
            DutyOperatingDay.SundayHoliday => "So/F",
            _ => fullName
        };
}
