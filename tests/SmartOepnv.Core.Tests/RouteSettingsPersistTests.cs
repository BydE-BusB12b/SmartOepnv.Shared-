using SmartOepnv.Core.Dienstvorlagen;
using SmartOepnv.Core.RoutePackage;
using Xunit;

namespace SmartOepnv.Core.Tests;

public class RouteSettingsPersistTests
{
    [Fact]
    public void Short_verkehr_label_parses_weekdays()
    {
        var days = DutyOperatingDayHelper.Parse("Mo-Fr");
        Assert.Equal(5, days.Count);
        Assert.Contains(DutyOperatingDay.Monday, days);
        Assert.Contains(DutyOperatingDay.Friday, days);
        Assert.DoesNotContain(DutyOperatingDay.Saturday, days);
        Assert.Contains(DutyOperatingDay.Tuesday, DutyOperatingDayHelper.Parse("Di"));
    }

    [Fact]
    public void Changing_operating_days_keeps_destination_and_voice()
    {
        var package = EditableRoutePackage.FromJson(
            """
            {
              "routes": ["RE47X (Linie: 047/01, Fahrt: 1, Verkehr: Mo-Fr)"],
              "routeInteriorDisplayDestinations": {
                "RE47X (Linie: 047/01, Fahrt: 1)": "Solingen"
              },
              "routeAnnouncementVoices": {
                "RE47X (Linie: 047/01, Fahrt: 1)": "2"
              },
              "routeOperatingDays": {
                "RE47X (Linie: 047/01, Fahrt: 1, Verkehr: Mo-Fr)": ["monday","tuesday","wednesday","thursday","friday"]
              }
            }
            """);

        var loaded = Assert.Single(package.RouteNames);
        Assert.Contains("Montag-Freitag", loaded, StringComparison.Ordinal);
        Assert.Equal("Solingen", package.GetRouteInteriorDisplayDestination(loaded));
        Assert.Equal(AnnouncementVoiceFiles.Voice2, package.GetRouteAnnouncementVoice(loaded));
        Assert.Equal(5, package.GetRouteOperatingDays(loaded).Count);

        var updated = package.ApplyOperatingDaysChange(loaded, [DutyOperatingDay.Tuesday]);

        Assert.Equal("Solingen", package.GetRouteInteriorDisplayDestination(updated));
        Assert.Equal(AnnouncementVoiceFiles.Voice2, package.GetRouteAnnouncementVoice(updated));
        var days = package.GetRouteOperatingDays(updated);
        Assert.Equal([DutyOperatingDay.Tuesday], days.OrderBy(day => (int)day));
    }
}
