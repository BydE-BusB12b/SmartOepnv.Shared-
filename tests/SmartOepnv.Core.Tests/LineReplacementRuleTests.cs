using SmartOepnv.Core.RoutePackage;
using Xunit;

namespace SmartOepnv.Core.Tests;

public class LineReplacementRuleTests
{
    [Fact]
    public void Range_maps_047_to_RE47()
    {
        Assert.True(LineReplacementRule.TryCreate("001", "099", "RE1", "RE99", out var rule, out var error), error);
        Assert.NotNull(rule);

        var shown = LineReplacementRule.Resolve([rule!], "047");

        Assert.Equal("RE47", shown);
        Assert.Equal("RE1", LineReplacementRule.Resolve([rule], "001"));
        Assert.Equal("RE99", LineReplacementRule.Resolve([rule], "099"));
        Assert.Equal("100", LineReplacementRule.Resolve([rule], "100"));
    }

    [Fact]
    public void Exact_line_wins_over_wider_range()
    {
        Assert.True(LineReplacementRule.TryCreate("001", "099", "RE1", "RE99", out var range, out _));
        Assert.True(LineReplacementRule.TryCreate("047", "", "RE47X", "", out var exact, out _));

        var shown = LineReplacementRule.Resolve([range!, exact!], "047");

        Assert.Equal("RE47X", shown);
        Assert.Equal("RE4", LineReplacementRule.Resolve([exact with { FromLine = "604", ToLine = "604", FromDisplay = "RE4", ToDisplay = "RE4" }], "604"));
    }

    [Fact]
    public void Range_keeps_shared_suffix()
    {
        Assert.True(LineReplacementRule.TryCreate("401", "499", "RE1X", "RE99X", out var rule, out var error), error);

        Assert.Equal("RE47X", LineReplacementRule.Resolve([rule!], "447"));
        Assert.Equal("RE1X", LineReplacementRule.Resolve([rule], "401"));
        Assert.Equal("RE99X", LineReplacementRule.Resolve([rule], "499"));
        Assert.Equal("400", LineReplacementRule.Resolve([rule], "400"));
    }

    [Fact]
    public void Range_rejects_different_suffix()
    {
        var ok = LineReplacementRule.TryCreate("401", "499", "RE1X", "RE99", out _, out var error);

        Assert.False(ok);
        Assert.Contains("hinter der Zahl", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Uneven_range_is_rejected()
    {
        var ok = LineReplacementRule.TryCreate("001", "099", "RE1", "RE98", out _, out var error);

        Assert.False(ok);
        Assert.Contains("passt nicht", error, StringComparison.Ordinal);
    }
}
