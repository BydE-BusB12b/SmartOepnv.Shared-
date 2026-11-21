using SmartOepnv.Core.RoutePackage;
using Xunit;

namespace SmartOepnv.Core.Tests;

public class OutsideDisplayStartzielDateTests
{
    [Fact]
    public void Dated_startziel_roundtrips_without_replacing_the_standard()
    {
        var standard = OutsideDisplayProgram.CreateDs021t("Startziel");
        standard.IsStartTarget = true;
        standard.FrontCycles[0].SetFromPair("Betriebshof", "");
        standard.SideCycles[0].SetFromPair("Betriebshof", "");
        standard.ApplyStartTargetName();

        var dated = standard.CloneAsDatedStartziel("2026-11-19", [standard.Id]);
        dated.FrontCycles[0].SetFromPair("Sonderfahrt", "");
        dated.SideCycles[0].SetFromPair("Seite Sonder", "");

        var loadedStandard = OutsideDisplayProgram.TryParse(standard.ToStorageEntry());
        var loadedDated = OutsideDisplayProgram.TryParse(dated.ToStorageEntry());

        Assert.NotNull(loadedStandard);
        Assert.NotNull(loadedDated);
        Assert.Equal("", loadedStandard!.StartzielDate);
        Assert.Equal("Betriebshof", loadedStandard.FrontLine1);
        Assert.Equal("2026-11-19", loadedDated!.StartzielDate);
        Assert.Equal("19.11.2026", loadedDated.StartzielDayLabel);
        Assert.Equal("Startziel · 19.11.2026", loadedDated.ListTitle);
        Assert.Equal("Sonderfahrt", loadedDated.FrontLine1);
        Assert.Equal("Seite Sonder", loadedDated.SideLine1);
        Assert.NotEqual(loadedStandard.Id, loadedDated.Id);
        Assert.True(loadedDated.IsStartTarget);
    }
}
