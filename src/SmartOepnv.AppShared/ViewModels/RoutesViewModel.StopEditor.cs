using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartOepnv.AppShared.Views;
using SmartOepnv.Core;
using SmartOepnv.Core.RoutePackage;
using SmartOepnv.Core.Vrr;

namespace SmartOepnv.AppShared.ViewModels;

public partial class RoutesViewModel
{
    public ObservableCollection<string> Ds021tDestinations { get; } = [];
    public ObservableCollection<string> Ds021NeuDestinations { get; } = [];
    public ObservableCollection<string> FmaS1Destinations { get; } = [];
    public ObservableCollection<string> Ds003aDestinations { get; } = [];
    public ObservableCollection<string> ZielnummerDestinations { get; } = [];
    public ObservableCollection<string> MobitecDestinations { get; } = [];
    public ObservableCollection<string> LineCourseTripRoutes { get; } = [];
    public ObservableCollection<RouteChangeDatedTargetRow> RouteChangeDatedTargets { get; } = [];

    private bool _startStopCheckbox;
    private bool _suppressSelectedStopSideEffects;
    private int _stopEditorCatalogFingerprint = int.MinValue;
    private IReadOnlyList<OutsideDisplayDestinationResolver.CatalogEntry>? _destinationCatalog;
    private int _destinationCatalogFingerprint = int.MinValue;

    public bool HasSelectedStop => SelectedStop is not null;

    public bool IsStartStop
    {
        get => _startStopCheckbox;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            // Unveränderter Wert: nichts tun. Sonst setzt Speichern-Flush der unchecked
            // „Starthaltestelle“-Checkbox IsAnnouncementEnabled immer auf true und
            // überschreibt „Ansage ausblenden“.
            if (_startStopCheckbox == value)
            {
                return;
            }

            _startStopCheckbox = value;
            if (value)
            {
                if (SelectedStop.ZielwechselEnabled)
                {
                    SelectedStop.ZielwechselEnabled = false;
                    OnPropertyChanged(nameof(ZielwechselEnabled));
                    OnPropertyChanged(nameof(ShowZielwechselFields));
                }

                SelectedStop.IsAnnouncementEnabled = false;
                RouteStopEditorCatalog.EnsureStartStopMarker(SelectedStop);
                TryApplyLearnedStartDestinationSuggestion();
            }
            else if (!SelectedStop.ZielwechselEnabled)
            {
                RouteStopEditorCatalog.ClearStartStopFields(SelectedStop);
                SelectedStop.IsAnnouncementEnabled = true;
            }
            else
            {
                SelectedStop.IsAnnouncementEnabled = true;
            }

            NotifyStopEditorStateChanged();
            // TextBox/Combo an Property-Änderungen auf RouteStopItem binden (kein INPC).
            OnPropertyChanged(nameof(SelectedStop));
            MarkStopDetailDirty();
        }
    }

    public bool IsAnnouncementHidden
    {
        get => SelectedStop is not null && !IsStartStop && !SelectedStop.IsAnnouncementEnabled;
        set
        {
            if (SelectedStop is null || IsStartStop)
            {
                return;
            }

            var enabled = !value;
            if (SelectedStop.IsAnnouncementEnabled == enabled)
            {
                return;
            }

            SelectedStop.IsAnnouncementEnabled = enabled;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool ShowAnnouncementHiddenOption => HasSelectedStop && !IsStartStop;

    public bool ZielwechselEnabled
    {
        get => SelectedStop?.ZielwechselEnabled ?? false;
        set
        {
            if (SelectedStop is null || SelectedStop.ZielwechselEnabled == value)
            {
                return;
            }

            SelectedStop.ZielwechselEnabled = value;
            if (value)
            {
                // Zielwechsel ≠ Starthaltestelle: Ansage bleibt aktiv.
                if (_startStopCheckbox)
                {
                    _startStopCheckbox = false;
                    OnPropertyChanged(nameof(IsStartStop));
                }

                SelectedStop.IsAnnouncementEnabled = true;
                TryApplyLearnedZielwechselSuggestion();
                if (SelectedStop.ZielwechselRadius <= 0)
                {
                    SelectedStop.ZielwechselRadius = 40;
                }
            }

            NotifyStopEditorStateChanged();
            OnPropertyChanged(nameof(SelectedStop));
            MarkStopDetailDirty();
        }
    }

    /// <summary>
    /// Zielwechsel-GPS und Ziel aus anderen Routen mit derselben Haltestellen-ID.
    /// </summary>
    private bool TryApplyLearnedZielwechselSuggestion()
    {
        if (SelectedStop is null || !SelectedStop.ZielwechselEnabled)
        {
            return false;
        }

        var typical = BuildLearnedStopSuggestions();
        if (!RouteZielwechselLearner.TrySuggest(typical, SelectedStop.PlannerStopCode, out var suggestion) ||
            !RouteZielwechselLearner.TryApplyZielwechselToEmptyFields(SelectedStop, suggestion))
        {
            return false;
        }

        EnsureCatalogContainsStopSelections(SelectedStop);
        StatusMessage = FormatLearnedSuggestionMessage("Zielwechsel", SelectedStop.PlannerStopCode, suggestion);
        return true;
    }

    /// <summary>Startziel aus anderen Routen mit derselben Haltestellen-ID.</summary>
    private bool TryApplyLearnedStartDestinationSuggestion()
    {
        if (SelectedStop is null || !IsStartStop)
        {
            return false;
        }

        var typical = BuildLearnedStopSuggestions();
        if (!RouteZielwechselLearner.TrySuggest(typical, SelectedStop.PlannerStopCode, out var suggestion) ||
            !RouteZielwechselLearner.TryApplyStartDestinationToEmptyFields(SelectedStop, suggestion))
        {
            return false;
        }

        EnsureCatalogContainsStopSelections(SelectedStop);
        StatusMessage = FormatLearnedSuggestionMessage("Startziel", SelectedStop.PlannerStopCode, suggestion);
        return true;
    }

    private IReadOnlyDictionary<string, RouteZielwechselLearner.Suggestion> BuildLearnedStopSuggestions()
    {
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return new Dictionary<string, RouteZielwechselLearner.Suggestion>(StringComparer.Ordinal);
        }

        // Aktuelle (noch ungespeicherte) Haltestellenliste mit einbeziehen.
        var lists = new List<IEnumerable<RouteStopItem>>();
        var selectedKey = SelectedRoute?.Trim() ?? string.Empty;
        foreach (var (routeKey, stops) in editor.StopsByRoute)
        {
            if (!string.IsNullOrEmpty(selectedKey) &&
                RouteDisplayHelper.RouteKeysMatch(routeKey, selectedKey) &&
                Stops.Count > 0)
            {
                lists.Add(Stops);
            }
            else
            {
                lists.Add(stops);
            }
        }

        if (!string.IsNullOrEmpty(selectedKey) &&
            Stops.Count > 0 &&
            lists.TrueForAll(list => !ReferenceEquals(list, Stops)))
        {
            lists.Add(Stops);
        }

        return RouteZielwechselLearner.BuildTypical(lists);
    }

    private static string FormatLearnedSuggestionMessage(
        string kind,
        string? plannerStopCode,
        RouteZielwechselLearner.Suggestion suggestion)
    {
        var parts = new List<string>();
        if (suggestion.HasGps && string.Equals(kind, "Zielwechsel", StringComparison.Ordinal))
        {
            parts.Add($"GPS {suggestion.GpsCoordinates} ({suggestion.Radius} m)");
        }

        if (suggestion.HasDestination)
        {
            var dest =
                FirstNonEmpty(
                    suggestion.ZielnummerDestination,
                    suggestion.Ds003aDestination,
                    suggestion.MobitecDestination,
                    suggestion.Ds021NeuDestination,
                    suggestion.FmaS1Destination,
                    suggestion.Destination) ?? "Ziel";
            parts.Add($"Ziel „{dest}“");
        }

        var id = PlannerStopCode.Normalize(plannerStopCode);
        return parts.Count > 0
            ? $"{kind}-Vorschlag (ID {id}): {string.Join(", ", parts)} – bitte prüfen."
            : $"{kind}-Vorschlag übernommen – bitte prüfen.";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    public bool ShowZielwechselFields => HasSelectedStop && ZielwechselEnabled;

    public bool StopHintEnabled
    {
        get => SelectedStop?.StopHintEnabled ?? false;
        set
        {
            if (SelectedStop is null || SelectedStop.StopHintEnabled == value)
            {
                return;
            }

            SelectedStop.StopHintEnabled = value;
            if (value && SelectedStop.StopHintRadius <= 0)
            {
                SelectedStop.StopHintRadius = 40;
            }

            if (string.IsNullOrWhiteSpace(SelectedStop.StopHintTriggerMode))
            {
                SelectedStop.StopHintTriggerMode = RouteStopHintTrigger.WithAnnouncement;
            }

            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool ShowStopHintFields => HasSelectedStop && StopHintEnabled;

    public bool IsStopHintWithAnnouncement
    {
        get => SelectedStop is null ||
               RouteStopHintTrigger.Normalize(SelectedStop.StopHintTriggerMode) ==
               RouteStopHintTrigger.WithAnnouncement;
        set
        {
            if (SelectedStop is null || !value)
            {
                return;
            }

            if (RouteStopHintTrigger.Normalize(SelectedStop.StopHintTriggerMode) ==
                RouteStopHintTrigger.WithAnnouncement)
            {
                return;
            }

            SelectedStop.StopHintTriggerMode = RouteStopHintTrigger.WithAnnouncement;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool IsStopHintOwnGps
    {
        get => SelectedStop is not null &&
               RouteStopHintTrigger.Normalize(SelectedStop.StopHintTriggerMode) ==
               RouteStopHintTrigger.OwnGps;
        set
        {
            if (SelectedStop is null || !value)
            {
                return;
            }

            if (RouteStopHintTrigger.Normalize(SelectedStop.StopHintTriggerMode) ==
                RouteStopHintTrigger.OwnGps)
            {
                return;
            }

            SelectedStop.StopHintTriggerMode = RouteStopHintTrigger.OwnGps;
            if (SelectedStop.StopHintRadius <= 0)
            {
                SelectedStop.StopHintRadius = 40;
            }

            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool ShowStopHintOwnGpsFields => ShowStopHintFields && IsStopHintOwnGps;

    public bool EntwerterEnabled
    {
        get => SelectedStop?.EntwerterEnabled ?? false;
        set
        {
            if (SelectedStop is null || SelectedStop.EntwerterEnabled == value)
            {
                return;
            }

            SelectedStop.EntwerterEnabled = value;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool ShowEntwerterFields => HasSelectedStop && EntwerterEnabled;

    /// <summary>Nur Wabe (DS004 Ziffer 4–6). Linie = Ziffer 1–3 kommt automatisch von der Route.</summary>
    public string EntwerterWabe
    {
        get => ExtractEntwerterWabe(SelectedStop?.EntwerterCode);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            var kurz = EntwerterStopCode.EffectiveKurzstrecke(SelectedStop.EntwerterCode);
            var stored = StoreEntwerterCode(value, kurz);
            if (string.Equals(SelectedStop.EntwerterCode, stored, StringComparison.Ordinal))
            {
                return;
            }

            SelectedStop.EntwerterCode = stored;
            OnPropertyChanged(nameof(EntwerterWabe));
            OnPropertyChanged(nameof(EntwerterKurzstrecke));
            OnPropertyChanged(nameof(EntwerterLinieAuto));
            OnPropertyChanged(nameof(EntwerterTelegramPreview));
            MarkStopDetailDirty();
        }
    }

    /// <summary>DS004a Kurzstrecke (7. Stempelstelle), 0–9; Voreinstellung 3 → DS004a <c>0031</c>.</summary>
    public string EntwerterKurzstrecke
    {
        get => EntwerterStopCode.EffectiveKurzstrecke(SelectedStop?.EntwerterCode).ToString();
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
            // Leer = Voreinstellung 0031 (Kurzstrecke 3), nicht 0
            var kurz = digits.Length == 0
                ? EntwerterStopCode.DefaultKurzstrecke
                : digits[^1] - '0';
            var stored = StoreEntwerterCode(EntwerterWabe, kurz);
            if (string.Equals(SelectedStop.EntwerterCode, stored, StringComparison.Ordinal))
            {
                return;
            }

            SelectedStop.EntwerterCode = stored;
            OnPropertyChanged(nameof(EntwerterWabe));
            OnPropertyChanged(nameof(EntwerterKurzstrecke));
            OnPropertyChanged(nameof(EntwerterTelegramPreview));
            MarkStopDetailDirty();
        }
    }

    /// <summary>DS004 Ziffer 4–6 aus Starthaltestellen-Ziel (DS001), nur Anzeige.</summary>
    public string EntwerterLinieAuto
    {
        get
        {
            var line = ResolveRouteEntwerterLinie();
            return string.IsNullOrEmpty(line) ? "—" : line;
        }
    }

    public string EntwerterTelegramPreview
    {
        get
        {
            var wabe = ExtractEntwerterWabe(SelectedStop?.EntwerterCode);
            if (string.IsNullOrEmpty(wabe))
            {
                return "Nur Wabe eintragen. Linie kommt automatisch von der Route (DS001). DS004a = 0031.";
            }

            var line = ResolveRouteEntwerterLinie();
            var linePart = string.IsNullOrEmpty(line) ? "???" : line;
            var ds004a = EntwerterStopCode.FormatDs004a(SelectedStop?.EntwerterCode);
            // IbisUtility/ELGEBA: e + Linie(3) + Wabe(3); DS004a Standard 0031
            return $"DS004: e{linePart}{wabe}  ·  DS004a: {ds004a} (Linie auto)";
        }
    }

    private string ResolveRouteEntwerterLinie()
    {
        var start = Stops.FirstOrDefault(RouteStopEditorCatalog.IsStartStop);
        if (start is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(start.LineNumber))
        {
            return PadEntwerterDrei(start.LineNumber);
        }

        // Route-Linie/Kurs (z. B. 686/00) hat Vorrang vor DS001 am Zielprogramm –
        // DS003-Programme haben oft Default „001“ (Zielnummer), nicht die Buslinie.
        var fromRoute = ResolveEntwerterLinieFromSelectedRoute();
        if (!string.IsNullOrEmpty(fromRoute))
        {
            return fromRoute;
        }

        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return string.Empty;
        }

        foreach (var destName in new[]
                 {
                     start.Destination,
                     start.Ds021NeuDestination,
                     start.FmaS1Destination,
                     start.Ds003aDestination,
                     start.MobitecDestination
                     // Zielnummer/DS003 absichtlich nicht: deren DS001 ist die Zielnr., nicht die Linie
                 })
        {
            if (string.IsNullOrWhiteSpace(destName) ||
                string.Equals(destName, RouteStopEditorCatalog.NoDestinationLabel, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var entry in editor.OutsideDisplays)
            {
                var program = OutsideDisplayProgram.TryParse(entry);
                if (program is null)
                {
                    continue;
                }

                if (!string.Equals(program.Name.Trim(), destName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (program.IsDs003 || program.IsZielnummer)
                {
                    continue;
                }

                var digits = new string((program.Ds001Value ?? string.Empty).Where(char.IsDigit).ToArray());
                if (digits.Length > 0)
                {
                    return PadEntwerterDrei(digits);
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Linie aus ausgewählter Route: PassengerLine, sonst Linie/Kurs, sonst führende Ziffern im Namen.</summary>
    private string ResolveEntwerterLinieFromSelectedRoute()
    {
        if (string.IsNullOrWhiteSpace(SelectedRoute))
        {
            return string.Empty;
        }

        var parsed = RouteDisplayHelper.Parse(SelectedRoute);
        if (!string.IsNullOrWhiteSpace(parsed.PassengerDisplayLine))
        {
            var fromPassenger = PadEntwerterDrei(parsed.PassengerDisplayLine);
            if (!string.IsNullOrEmpty(fromPassenger))
            {
                return fromPassenger;
            }
        }

        if (!string.IsNullOrWhiteSpace(parsed.LineCourse))
        {
            var linePart = parsed.LineCourse.Split('/')[0];
            var fromCourse = PadEntwerterDrei(linePart);
            if (!string.IsNullOrEmpty(fromCourse))
            {
                return fromCourse;
            }
        }

        var name = (parsed.Name ?? string.Empty).Trim();
        var leading = new string(name.TakeWhile(char.IsDigit).ToArray());
        return PadEntwerterDrei(leading);
    }

    private static string ExtractEntwerterWabe(string? raw) =>
        EntwerterStopCode.ExtractWabe(raw);

    private static int ExtractEntwerterKurzstrecke(string? raw) =>
        EntwerterStopCode.ExtractKurzstrecke(raw);

    private static string StoreEntwerterCode(string? wabe, int kurzstrecke) =>
        EntwerterStopCode.Store(wabe, kurzstrecke);

    private static string PadEntwerterDrei(string? raw) =>
        EntwerterStopCode.PadDrei(raw);

    public bool IsEndStop
    {
        get => SelectedStop?.IsEndStop ?? false;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.IsEndStop = value;

            if (value && SelectedStop.Radius <= 0)
            {
                SelectedStop.Radius = 15;
            }

            // Ohne Endhaltestelle gibt es keinen Routenwechsel (nur ggf. End-Ansage).
            if (!value && SelectedStop.RouteChangeEnabled)
            {
                SelectedStop.RouteChangeEnabled = false;
            }

            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool PlayEndStopAnnouncementEn
    {
        get => SelectedStop?.PlayEndStopAnnouncementEn ?? false;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.PlayEndStopAnnouncementEn = value;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool PlayEndStopAnnouncementNl
    {
        get => SelectedStop?.PlayEndStopAnnouncementNl ?? false;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.PlayEndStopAnnouncementNl = value;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool PlayEndStopAnnouncement
    {
        get => SelectedStop?.PlayEndStopAnnouncement ?? false;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.PlayEndStopAnnouncement = value;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool PlayStartStopGreeting
    {
        get => SelectedStop?.PlayStartStopGreeting ?? false;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.PlayStartStopGreeting = value;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool RouteChangeEnabled
    {
        get => SelectedStop?.RouteChangeEnabled ?? false;
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.RouteChangeEnabled = value;
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
        }
    }

    public bool ShowStartStopFields => HasSelectedStop && (IsStartStop || ZielwechselEnabled);
    /** Begrüßung wie Endhaltestellen-Ansage: an jedem Halt möglich (Einstieg ≠ Starthaltestelle). */
    public bool ShowStartStopGreetingFields => HasSelectedStop;
    public bool ShowStartStopGreetingCoordinatesFields =>
        HasSelectedStop && PlayStartStopGreeting;
    /** Endziel-Felder: bei Endhaltestelle oder Endhaltestellen-Ansage (ohne Routenwechsel). */
    public bool ShowEndDestinationFields =>
        HasSelectedStop && (IsEndStop || PlayEndStopAnnouncement);
    /** @deprecated Alias – Prefer [ShowEndDestinationFields]. */
    public bool ShowEndStopFields => ShowEndDestinationFields;
    /** Endhaltestellen-Ansage-Checkbox unabhängig von Endhaltestelle. */
    public bool ShowEndStopAnnouncementFields => HasSelectedStop;
    /** Automatischer Routenwechsel nur bei echter Endhaltestelle. */
    public bool ShowRouteChangeOption => HasSelectedStop && IsEndStop;
    public bool ShowRouteChangeFields => HasSelectedStop && IsEndStop && RouteChangeEnabled;

    [ObservableProperty]
    private string selectedStopVrrStopId = string.Empty;

    private bool _syncingStopVrrStopId;

    public string? SelectedDestinationDs021t
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds021T,
            SelectedStop?.DestinationId,
            SelectedStop?.Destination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds021T, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationDs021Neu
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds021Neu,
            SelectedStop?.Ds021NeuDestinationId,
            SelectedStop?.Ds021NeuDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds021Neu, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationFmaS1
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.FmaS1,
            SelectedStop?.FmaS1DestinationId,
            SelectedStop?.FmaS1Destination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.FmaS1, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationDs003a
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds003aKrefeld,
            SelectedStop?.Ds003aDestinationId,
            SelectedStop?.Ds003aDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds003aKrefeld, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationZielnummer
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds003,
            SelectedStop?.ZielnummerDestinationId,
            SelectedStop?.ZielnummerDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds003, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedDestinationMobitec
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Mobitec,
            SelectedStop?.MobitecDestinationId,
            SelectedStop?.MobitecDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Mobitec, isEnd: false, value);
            MaintainStartStopMarkerIfNeeded();
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationDs021t
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds021T,
            SelectedStop?.EndDestinationId,
            SelectedStop?.EndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds021T, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationDs021Neu
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds021Neu,
            SelectedStop?.Ds021NeuEndDestinationId,
            SelectedStop?.Ds021NeuEndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds021Neu, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationFmaS1
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.FmaS1,
            SelectedStop?.FmaS1EndDestinationId,
            SelectedStop?.FmaS1EndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.FmaS1, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationDs003a
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds003aKrefeld,
            SelectedStop?.Ds003aEndDestinationId,
            SelectedStop?.Ds003aEndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds003aKrefeld, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationZielnummer
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Ds003,
            SelectedStop?.ZielnummerEndDestinationId,
            SelectedStop?.ZielnummerEndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Ds003, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    public string? SelectedEndDestinationMobitec
    {
        get => ResolveComboLabel(
            OutsideDisplayProtocolKind.Mobitec,
            SelectedStop?.MobitecEndDestinationId,
            SelectedStop?.MobitecEndDestination);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            ApplyDestinationSelection(OutsideDisplayProtocolKind.Mobitec, isEnd: true, value);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    private IReadOnlyList<OutsideDisplayDestinationResolver.CatalogEntry> DestinationCatalog
    {
        get
        {
            var editor = AppServices.Routes.Editor;
            var fingerprint = ComputeOutsideDisplaysFingerprint(editor);
            if (_destinationCatalog is null || fingerprint != _destinationCatalogFingerprint)
            {
                _destinationCatalog = OutsideDisplayDestinationResolver.BuildCatalog(
                    editor?.OutsideDisplays ?? Array.Empty<string>());
                _destinationCatalogFingerprint = fingerprint;
            }

            return _destinationCatalog;
        }
    }

    private static int ComputeOutsideDisplaysFingerprint(EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(editor.OutsideDisplays.Count);
        foreach (var entry in editor.OutsideDisplays)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }

    private static int ComputeStopEditorCatalogFingerprint(EditableRoutePackage? editor)
    {
        if (editor is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(ComputeOutsideDisplaysFingerprint(editor));
        hash.Add(editor.RouteNames.Count);
        foreach (var route in editor.RouteNames)
        {
            hash.Add(route);
        }

        return hash.ToHashCode();
    }

    private string? ResolveComboLabel(
        OutsideDisplayProtocolKind protocol,
        string? destinationId,
        string? destinationName)
    {
        var resolved = OutsideDisplayDestinationResolver.ResolveDisplayName(
            DestinationCatalog,
            protocol,
            destinationId,
            destinationName);
        return ToComboLabel(resolved, RouteStopEditorCatalog.NoDestinationLabel);
    }

    private void ApplyDestinationSelection(
        OutsideDisplayProtocolKind protocol,
        bool isEnd,
        string? comboLabel)
    {
        if (SelectedStop is null)
        {
            return;
        }

        OutsideDisplayDestinationResolver.ApplySelection(
            SelectedStop,
            protocol,
            isEnd,
            comboLabel,
            DestinationCatalog);
    }

    public string? SelectedLineCourseTrip
    {
        get => ToComboLabel(SelectedStop?.SelectedLineCourseTrip, RouteStopEditorCatalog.NoLineCourseTripLabel);
        set
        {
            if (SelectedStop is null)
            {
                return;
            }

            SelectedStop.SelectedLineCourseTrip = FromComboLabel(value, RouteStopEditorCatalog.NoLineCourseTripLabel);
            OnPropertyChanged();
            MarkStopDetailDirty();
        }
    }

    [ObservableProperty] private string lineCourseTripQuickEntry = string.Empty;

    partial void OnLineCourseTripQuickEntryChanged(string value) =>
        ApplyLineCourseTripByNumberCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanApplyLineCourseTripByNumber))]
    private void ApplyLineCourseTripByNumber()
    {
        if (SelectedStop is null)
        {
            return;
        }

        var routes = LineCourseTripRoutes
            .Where(route => !string.Equals(route, RouteStopEditorCatalog.NoLineCourseTripLabel, StringComparison.Ordinal));
        if (!RouteStopEditorCatalog.TryResolveLineCourseTripByTripNumber(
                routes,
                LineCourseTripQuickEntry,
                SelectedRoute,
                out var matchedRoute,
                out var error))
        {
            StatusMessage = error ?? "Fahrt konnte nicht übernommen werden.";
            return;
        }

        SelectedLineCourseTrip = matchedRoute;
        LineCourseTripQuickEntry = string.Empty;
        StatusMessage = $"Routenwechsel-Fahrt übernommen: {matchedRoute}";
    }

    private bool CanApplyLineCourseTripByNumber() =>
        ShowRouteChangeFields && !string.IsNullOrWhiteSpace(LineCourseTripQuickEntry);

    [RelayCommand(CanExecute = nameof(CanAddRouteChangeDatedTarget))]
    private void AddRouteChangeDatedTarget()
    {
        if (SelectedStop is null || !ShowRouteChangeFields)
        {
            return;
        }

        // Leere Zeile anlegen – Verkehrstage/Datum und Folgefahrt werden in der Zeile gepflegt.
        RouteChangeDatedTargets.Add(new RouteChangeDatedTargetRow(
            RouteChangeDatedTargets.Count,
            string.Empty,
            RouteStopEditorCatalog.NoLineCourseTripLabel,
            string.Empty,
            PersistRouteChangeDatedTargetsFromRows));
        MarkStopDetailDirty();
        StatusMessage = "Neue Abweichungszeile hinzugefügt – Verkehrstag und/oder Datum sowie Folgefahrt eintragen.";
    }

    private bool CanAddRouteChangeDatedTarget() => ShowRouteChangeFields;

    [RelayCommand]
    private void RemoveRouteChangeDatedTarget(RouteChangeDatedTargetRow? row)
    {
        if (SelectedStop is null || row is null)
        {
            return;
        }

        RouteChangeDatedTargets.Remove(row);
        PersistRouteChangeDatedTargetsFromRows();
        ReloadRouteChangeDatedTargets();
        MarkStopDetailDirty();
    }

    public void ReloadRouteChangeDatedTargets()
    {
        RouteChangeDatedTargets.Clear();
        if (SelectedStop is null)
        {
            return;
        }

        for (var i = 0; i < SelectedStop.RouteChangeTargetsByDate.Count; i++)
        {
            var entry = SelectedStop.RouteChangeTargetsByDate[i];
            RouteChangeDatedTargets.Add(new RouteChangeDatedTargetRow(
                i,
                RouteOperatingDatesEditor.FormatDisplay(entry.OperatingDates),
                ToComboLabel(entry.SelectedLineCourseTrip, RouteStopEditorCatalog.NoLineCourseTripLabel),
                entry.SelectedLineCourseTrip,
                PersistRouteChangeDatedTargetsFromRows,
                entry.OperatingDays));
        }

        AddRouteChangeDatedTargetCommand.NotifyCanExecuteChanged();
    }

    private void PersistRouteChangeDatedTargetsFromRows()
    {
        if (SelectedStop is null)
        {
            return;
        }

        var rebuilt = new List<RouteChangeTargetEntry>();
        foreach (var row in RouteChangeDatedTargets)
        {
            _ = RouteOperatingDatesEditor.TryParseDateList(row.DatesText, out var dates, out _);
            dates ??= [];
            var days = row.SelectedDays.ToList();
            if (dates.Count == 0 && days.Count == 0)
            {
                continue;
            }

            var trip = FromComboLabel(row.SelectedTrip, RouteStopEditorCatalog.NoLineCourseTripLabel);
            if (string.IsNullOrWhiteSpace(trip))
            {
                continue;
            }

            rebuilt.Add(new RouteChangeTargetEntry
            {
                SelectedLineCourseTrip = trip,
                OperatingDates = dates,
                OperatingDays = days
            });
        }

        SelectedStop.RouteChangeTargetsByDate = rebuilt;
        MarkStopDetailDirty();
    }

    partial void OnSelectedStopVrrStopIdChanged(string value)
    {
        if (_syncingStopVrrStopId || SelectedStop is null)
        {
            return;
        }

        var trimmed = value?.Trim() ?? string.Empty;
        if (string.Equals(SelectedStop.VrrStopId, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        SelectedStop.VrrStopId = trimmed;
        MarkStopDetailDirty();
    }

    [RelayCommand]
    private void StopDetailEdited() => MarkStopDetailDirty();

    [RelayCommand]
    private void PickStartStopGreetingCoordinatesOnMap()
    {
        if (SelectedStop is null)
        {
            return;
        }

        try
        {
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !owner.IsLoaded)
            {
                owner = null;
            }

            var initial = string.IsNullOrWhiteSpace(SelectedStop.StartStopGreetingCoordinates)
                ? SelectedStop.GpsCoordinates
                : SelectedStop.StartStopGreetingCoordinates;
            var dialog = new GpsMapPickerDialog(
                "Begrüßungs-GPS",
                initial,
                SelectedStop.GpsCoordinates,
                "Haltestelle",
                radiusMeters: SelectedStop.Radius > 0 ? SelectedStop.Radius : 50)
            {
                Owner = owner
            };
            if (dialog.ShowDialog() != true || !dialog.HasSelection)
            {
                return;
            }

            SelectedStop.StartStopGreetingCoordinates = dialog.SelectedCoordinates;
            OnPropertyChanged(nameof(SelectedStop));
            MarkStopDetailDirty();
            StatusMessage = "Begrüßungs-GPS auf der Karte gesetzt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Karte: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PickZielwechselCoordinatesOnMap()
    {
        if (SelectedStop is null)
        {
            return;
        }

        try
        {
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !owner.IsLoaded)
            {
                owner = null;
            }

            var initial = string.IsNullOrWhiteSpace(SelectedStop.ZielwechselGpsCoordinates)
                ? SelectedStop.GpsCoordinates
                : SelectedStop.ZielwechselGpsCoordinates;
            var radius = SelectedStop.ZielwechselRadius > 0
                ? SelectedStop.ZielwechselRadius
                : 40;
            var dialog = new GpsMapPickerDialog(
                "Zielwechsel-GPS",
                initial,
                SelectedStop.GpsCoordinates,
                "Haltestelle",
                radiusMeters: radius)
            {
                Owner = owner
            };
            if (dialog.ShowDialog() != true || !dialog.HasSelection)
            {
                return;
            }

            SelectedStop.ZielwechselGpsCoordinates = dialog.SelectedCoordinates;
            OnPropertyChanged(nameof(SelectedStop));
            MarkStopDetailDirty();
            StatusMessage = "Zielwechsel-GPS auf der Karte gesetzt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Karte: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PickStopHintCoordinatesOnMap()
    {
        if (SelectedStop is null)
        {
            return;
        }

        try
        {
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !owner.IsLoaded)
            {
                owner = null;
            }

            var initial = string.IsNullOrWhiteSpace(SelectedStop.StopHintGpsCoordinates)
                ? SelectedStop.GpsCoordinates
                : SelectedStop.StopHintGpsCoordinates;
            var radius = SelectedStop.StopHintRadius > 0
                ? SelectedStop.StopHintRadius
                : 40;
            var dialog = new GpsMapPickerDialog(
                "Hinweis-GPS",
                initial,
                SelectedStop.GpsCoordinates,
                "Haltestelle",
                radiusMeters: radius)
            {
                Owner = owner
            };
            if (dialog.ShowDialog() != true || !dialog.HasSelection)
            {
                return;
            }

            SelectedStop.StopHintGpsCoordinates = dialog.SelectedCoordinates;
            OnPropertyChanged(nameof(SelectedStop));
            MarkStopDetailDirty();
            StatusMessage = "Hinweis-GPS auf der Karte gesetzt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Karte: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PickEndDestinationCoordinatesOnMap()
    {
        if (SelectedStop is null)
        {
            return;
        }

        try
        {
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !owner.IsLoaded)
            {
                owner = null;
            }

            var initial = string.IsNullOrWhiteSpace(SelectedStop.EndDestinationCoordinates)
                ? SelectedStop.GpsCoordinates
                : SelectedStop.EndDestinationCoordinates;
            var dialog = new GpsMapPickerDialog(
                "Endziel-GPS",
                initial,
                SelectedStop.GpsCoordinates,
                "Haltestelle",
                radiusMeters: SelectedStop.Radius > 0 ? SelectedStop.Radius : 50)
            {
                Owner = owner
            };
            if (dialog.ShowDialog() != true || !dialog.HasSelection)
            {
                return;
            }

            SelectedStop.EndDestinationCoordinates = dialog.SelectedCoordinates;
            OnPropertyChanged(nameof(SelectedStop));
            MarkStopDetailDirty();
            StatusMessage = "Endziel-GPS auf der Karte gesetzt.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Karte: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PickVrrStop()
    {
        if (SelectedStop is null)
        {
            return;
        }

        try
        {
            var prefill = VrrStopAssignmentManager.PrefillQuery(
                SelectedStop.Name,
                SelectedStop.VrrStopId);
            var owner = Application.Current?.MainWindow;
            if (owner is not null && !owner.IsLoaded)
            {
                owner = null;
            }

            var dialog = new VrrStopFinderDialog(prefill) { Owner = owner };
            if (dialog.ShowDialog() != true || dialog.SelectedEntry is null)
            {
                return;
            }

            var assignment = VrrStopAssignmentManager.FromCatalogEntry(dialog.SelectedEntry);
            VrrStopAssignmentManager.ApplyToRouteStop(SelectedStop, assignment);
            SyncSelectedStopVrrStopIdFromStop();
            NotifyStopEditorStateChanged();
            MarkStopDetailDirty();
            StatusMessage = $"VRR-ID „{SelectedStop.VrrStopId}“ übernommen ({assignment.DisplayName}).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"VRR-Suche fehlgeschlagen: {ex.Message}";
        }
    }

    public void OnStopGridEdited()
    {
        NotifyStopEditorStateChanged();
        MarkStopDetailDirty();
    }

    /// <summary>Kataloge und ComboBox-Auswahl vor dem Bearbeitungsdialog vorbereiten (verhindert Absturz bei fehlenden Listeneinträgen).</summary>
    public void PrepareStopEditDialog(RouteStopItem stop)
    {
        _suppressSelectedStopSideEffects = true;
        try
        {
            SelectedStop = stop;
            _startStopCheckbox = RouteStopEditorCatalog.IsStartStop(stop);
            if (_startStopCheckbox)
            {
                RouteStopEditorCatalog.EnsureStartStopMarker(stop);
            }

            // Zielwechsel-Halte früher fälschlich als Starthaltestelle ohne Ansage gespeichert.
            if (stop.ZielwechselEnabled && !stop.IsAnnouncementEnabled)
            {
                stop.IsAnnouncementEnabled = true;
                MarkStopDetailDirty();
            }

            RefreshStopEditorCatalogs();
            var learned = false;
            if (_startStopCheckbox)
            {
                learned |= TryApplyLearnedStartDestinationSuggestion();
            }

            if (stop.ZielwechselEnabled)
            {
                learned |= TryApplyLearnedZielwechselSuggestion();
            }

            if (learned)
            {
                MarkStopDetailDirty();
            }

            EnsureCatalogContainsStopSelections(stop);
            SyncSelectedStopVrrStopIdFromStop();
            ReloadRouteChangeDatedTargets();
            OnPropertyChanged(nameof(SelectedStop));
        }
        finally
        {
            _suppressSelectedStopSideEffects = false;
        }
    }

    private void SyncSelectedStopVrrStopIdFromStop()
    {
        _syncingStopVrrStopId = true;
        SelectedStopVrrStopId = SelectedStop?.VrrStopId ?? string.Empty;
        _syncingStopVrrStopId = false;
    }

    private void EnsureCatalogContainsStopSelections(RouteStopItem stop)
    {
        EnsureComboValue(Ds021tDestinations, ToComboLabel(stop.Destination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds021NeuDestinations, ToComboLabel(stop.Ds021NeuDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(FmaS1Destinations, ToComboLabel(stop.FmaS1Destination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds003aDestinations, ToComboLabel(stop.Ds003aDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(
            ZielnummerDestinations,
            ResolveComboLabel(
                OutsideDisplayProtocolKind.Ds003,
                stop.ZielnummerDestinationId,
                stop.ZielnummerDestination));
        EnsureComboValue(MobitecDestinations, ToComboLabel(stop.MobitecDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds021tDestinations, ToComboLabel(stop.EndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds021NeuDestinations, ToComboLabel(stop.Ds021NeuEndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(FmaS1Destinations, ToComboLabel(stop.FmaS1EndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(Ds003aDestinations, ToComboLabel(stop.Ds003aEndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(
            ZielnummerDestinations,
            ResolveComboLabel(
                OutsideDisplayProtocolKind.Ds003,
                stop.ZielnummerEndDestinationId,
                stop.ZielnummerEndDestination));
        EnsureComboValue(MobitecDestinations, ToComboLabel(stop.MobitecEndDestination, RouteStopEditorCatalog.NoDestinationLabel));
        EnsureComboValue(
            LineCourseTripRoutes,
            ToComboLabel(stop.SelectedLineCourseTrip, RouteStopEditorCatalog.NoLineCourseTripLabel));
    }

    private static void EnsureComboValue(ObservableCollection<string> items, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || items.Contains(value))
        {
            return;
        }

        items.Add(value);
    }

    public void RefreshStopEditorCatalogs()
    {
        var editor = AppServices.Routes.Editor;
        var fingerprint = ComputeStopEditorCatalogFingerprint(editor);
        if (fingerprint == _stopEditorCatalogFingerprint &&
            Ds021tDestinations.Count > 0 &&
            LineCourseTripRoutes.Count > 0)
        {
            return;
        }

        _stopEditorCatalogFingerprint = fingerprint;
        _destinationCatalog = null;

        Ds021tDestinations.Clear();
        Ds021NeuDestinations.Clear();
        FmaS1Destinations.Clear();
        Ds003aDestinations.Clear();
        ZielnummerDestinations.Clear();
        MobitecDestinations.Clear();
        LineCourseTripRoutes.Clear();

        Ds021tDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        Ds021NeuDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        FmaS1Destinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        Ds003aDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        ZielnummerDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        MobitecDestinations.Add(RouteStopEditorCatalog.NoDestinationLabel);
        LineCourseTripRoutes.Add(RouteStopEditorCatalog.NoLineCourseTripLabel);

        if (editor is null)
        {
            return;
        }

        var names = RouteStopEditorCatalog.LoadAllProtocolNames(editor);
        foreach (var name in names.Ds021t)
        {
            Ds021tDestinations.Add(name);
        }

        foreach (var name in names.Ds021Neu)
        {
            Ds021NeuDestinations.Add(name);
        }

        foreach (var name in names.FmaS1)
        {
            FmaS1Destinations.Add(name);
        }

        foreach (var name in names.Ds003a)
        {
            Ds003aDestinations.Add(name);
        }

        foreach (var name in names.Zielnummer)
        {
            ZielnummerDestinations.Add(name);
        }

        foreach (var name in names.Mobitec)
        {
            MobitecDestinations.Add(name);
        }

        foreach (var route in RouteStopEditorCatalog.LoadLineCourseTripRoutes(editor))
        {
            LineCourseTripRoutes.Add(route);
        }
    }

    public void NotifyStopEditorStateChanged()
    {
        OnPropertyChanged(nameof(HasSelectedStop));
        OnPropertyChanged(nameof(IsStartStop));
        OnPropertyChanged(nameof(IsAnnouncementHidden));
        OnPropertyChanged(nameof(ShowAnnouncementHiddenOption));
        OnPropertyChanged(nameof(ZielwechselEnabled));
        OnPropertyChanged(nameof(ShowZielwechselFields));
        OnPropertyChanged(nameof(StopHintEnabled));
        OnPropertyChanged(nameof(ShowStopHintFields));
        OnPropertyChanged(nameof(IsStopHintWithAnnouncement));
        OnPropertyChanged(nameof(IsStopHintOwnGps));
        OnPropertyChanged(nameof(ShowStopHintOwnGpsFields));
        OnPropertyChanged(nameof(EntwerterEnabled));
        OnPropertyChanged(nameof(ShowEntwerterFields));
        OnPropertyChanged(nameof(EntwerterWabe));
        OnPropertyChanged(nameof(EntwerterKurzstrecke));
        OnPropertyChanged(nameof(EntwerterLinieAuto));
        OnPropertyChanged(nameof(EntwerterTelegramPreview));
        OnPropertyChanged(nameof(IsEndStop));
        OnPropertyChanged(nameof(PlayEndStopAnnouncement));
        OnPropertyChanged(nameof(PlayEndStopAnnouncementEn));
        OnPropertyChanged(nameof(PlayEndStopAnnouncementNl));
        OnPropertyChanged(nameof(PlayStartStopGreeting));
        OnPropertyChanged(nameof(RouteChangeEnabled));
        OnPropertyChanged(nameof(ShowStartStopFields));
        OnPropertyChanged(nameof(ShowStartStopGreetingFields));
        OnPropertyChanged(nameof(ShowStartStopGreetingCoordinatesFields));
        OnPropertyChanged(nameof(ShowEndDestinationFields));
        OnPropertyChanged(nameof(ShowEndStopFields));
        OnPropertyChanged(nameof(ShowEndStopAnnouncementFields));
        OnPropertyChanged(nameof(ShowRouteChangeOption));
        OnPropertyChanged(nameof(ShowRouteChangeFields));
        OnPropertyChanged(nameof(SelectedDestinationDs021t));
        OnPropertyChanged(nameof(SelectedDestinationDs021Neu));
        OnPropertyChanged(nameof(SelectedDestinationFmaS1));
        OnPropertyChanged(nameof(SelectedDestinationDs003a));
        OnPropertyChanged(nameof(SelectedDestinationZielnummer));
        OnPropertyChanged(nameof(SelectedDestinationMobitec));
        OnPropertyChanged(nameof(SelectedEndDestinationDs021t));
        OnPropertyChanged(nameof(SelectedEndDestinationDs021Neu));
        OnPropertyChanged(nameof(SelectedEndDestinationFmaS1));
        OnPropertyChanged(nameof(SelectedEndDestinationDs003a));
        OnPropertyChanged(nameof(SelectedEndDestinationZielnummer));
        OnPropertyChanged(nameof(SelectedEndDestinationMobitec));
        OnPropertyChanged(nameof(SelectedLineCourseTrip));
        ApplyLineCourseTripByNumberCommand.NotifyCanExecuteChanged();
        AddRouteChangeDatedTargetCommand.NotifyCanExecuteChanged();
        RouteChangeDisplayTick++;
    }

    private void MarkStopDetailDirty()
    {
        CancelSaveButtonSuccessFeedback();
        MaintainStartStopMarkerIfNeeded();
        _sync.MarkDirty();
        _needsStopTemplateEnrich = true;
        if (!RefreshStopTimeOrderWarnings(showDialog: false))
        {
            StatusMessage = "Haltestellen-Änderungen – bitte „Speichern“.";
        }
    }

    private void MaintainStartStopMarkerIfNeeded()
    {
        if (!_startStopCheckbox || SelectedStop is null)
        {
            return;
        }

        // Manuelle Liniennummer nicht mehr nutzen (Fehleingaben wie „000“ überschrieben DS001 am Ziel)
        if (RouteStopEditorCatalog.HasStartStopDestination(SelectedStop.Destination) ||
            RouteStopEditorCatalog.HasStartStopDestination(SelectedStop.Ds021NeuDestination) ||
            RouteStopEditorCatalog.HasStartStopDestination(SelectedStop.FmaS1Destination) ||
            !string.IsNullOrWhiteSpace(SelectedStop.Ds003aDestination) ||
            !string.IsNullOrWhiteSpace(SelectedStop.ZielnummerDestination) ||
            !string.IsNullOrWhiteSpace(SelectedStop.MobitecDestination))
        {
            SelectedStop.LineNumber = string.Empty;
        }

        RouteStopEditorCatalog.EnsureStartStopMarker(SelectedStop);
    }

    public void MaintainStartStopMarkerAfterEdit() => MaintainStartStopMarkerIfNeeded();

    public void ApplyDestinationComboSelection(string fieldKey, string? comboLabel)
    {
        if (SelectedStop is null)
        {
            return;
        }

        switch (fieldKey)
        {
            case "startDs021t":
                SelectedDestinationDs021t = comboLabel;
                break;
            case "startDs021Neu":
                SelectedDestinationDs021Neu = comboLabel;
                break;
            case "startFmaS1":
                SelectedDestinationFmaS1 = comboLabel;
                break;
            case "startDs003a":
                SelectedDestinationDs003a = comboLabel;
                break;
            case "startZielnummer":
                SelectedDestinationZielnummer = comboLabel;
                break;
            case "startMobitec":
                SelectedDestinationMobitec = comboLabel;
                break;
            case "endDs021t":
                SelectedEndDestinationDs021t = comboLabel;
                break;
            case "endDs021Neu":
                SelectedEndDestinationDs021Neu = comboLabel;
                break;
            case "endFmaS1":
                SelectedEndDestinationFmaS1 = comboLabel;
                break;
            case "endDs003a":
                SelectedEndDestinationDs003a = comboLabel;
                break;
            case "endZielnummer":
                SelectedEndDestinationZielnummer = comboLabel;
                break;
            case "endMobitec":
                SelectedEndDestinationMobitec = comboLabel;
                break;
            case "lineCourseTrip":
                SelectedLineCourseTrip = comboLabel;
                break;
            default:
                return;
        }
    }

    /// <summary>
    /// Freie DS003-Eingabe (1–4 Ziffern): vorhandenes Programm mit dieser Zielnummer nutzen
    /// oder neu anlegen und das Combo-ListLabel zurückgeben.
    /// </summary>
    public bool TryEnsureDs003DestinationByNumber(string? rawNumber, out string comboLabel)
    {
        comboLabel = RouteStopEditorCatalog.NoDestinationLabel;
        var digits = new string((rawNumber ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 1 or > 4)
        {
            return false;
        }

        var number = OutsideDisplayTelegramFactory.NormalizeZielnummer(digits);
        var editor = AppServices.Routes.Editor;
        if (editor is null)
        {
            return false;
        }

        foreach (var entry in editor.OutsideDisplays)
        {
            var program = OutsideDisplayProgram.TryParse(entry);
            if (program is null || program.Protocol != OutsideDisplayProtocolKind.Ds003)
            {
                continue;
            }

            var programNumber = OutsideDisplayTelegramFactory.NormalizeZielnummer(program.FrontLine1);
            if (string.IsNullOrWhiteSpace(programNumber) || programNumber == "000")
            {
                programNumber = OutsideDisplayTelegramFactory.NormalizeZielnummer(program.Name);
            }

            if (!string.Equals(programNumber, number, StringComparison.Ordinal))
            {
                continue;
            }

            comboLabel = string.IsNullOrWhiteSpace(program.Ds003ListLabel)
                ? program.Name.Trim()
                : program.Ds003ListLabel;
            EnsureComboValue(ZielnummerDestinations, comboLabel);
            return true;
        }

        var created = OutsideDisplayProgram.CreateDs003(number);
        created.FrontLine1 = number;
        created.Id = OutsideDisplayId.NewUniqueId(
            editor.OutsideDisplays
                .Select(OutsideDisplayProgram.TryParse)
                .Where(p => p is not null)
                .Select(p => p!.Id));
        editor.OutsideDisplays.Add(created.ToStorageEntry());
        RefreshStopEditorCatalogs();
        _sync.MarkDirty();
        StatusMessage = $"DS003-Ziel {number} angelegt und übernommen.";
        comboLabel = string.IsNullOrWhiteSpace(created.Ds003ListLabel)
            ? created.Name.Trim()
            : created.Ds003ListLabel;
        return true;
    }

    private static string? ToComboLabel(string? value, string emptyLabel) =>
        RouteStopEditorCatalog.ToComboLabel(value, emptyLabel);

    private static string FromComboLabel(string? value, string emptyLabel) =>
        RouteStopEditorCatalog.FromComboLabel(value, emptyLabel);
}
