using SmartOepnv.Core.RoutePackage;
using Xunit;

namespace SmartOepnv.Core.Tests;

public class JsonTopLevelPropertyTests
{
    [Fact]
    public void Upsert_replaces_top_level_array_and_keeps_nested_text()
    {
        var json = """{"version":"1.0","outsideDisplays":["alt"],"note":"outsideDisplays","timestamp":5}""";

        var updated = JsonTopLevelProperty.Upsert(json, "outsideDisplays", """["RE47|neu"]""");
        updated = JsonTopLevelProperty.Upsert(updated, "timestamp", "99");

        Assert.Contains(""""outsideDisplays":["RE47|neu"]"""", updated, StringComparison.Ordinal);
        Assert.Contains(""""note":"outsideDisplays"""", updated, StringComparison.Ordinal);
        Assert.Contains(""""timestamp":99"""", updated, StringComparison.Ordinal);
        Assert.DoesNotContain(""""["alt"]"""", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void Upsert_inserts_missing_property()
    {
        var json = """{"version":"1.0"}""";

        var updated = JsonTopLevelProperty.Upsert(json, "dateBasedHints", "[]");

        Assert.Equal("""{"version":"1.0","dateBasedHints":[]}""", updated);
    }
}
