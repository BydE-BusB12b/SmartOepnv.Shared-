namespace SmartOepnv.Core.Sev;

public readonly record struct SevDestinationLayout(
    bool HasExpressBus,
    string ExpressBusLine,
    string PrimaryLine,
    string SecondaryLine)
{
    public const string ExpressBusLabel = "Expressbus";

    public static SevDestinationLayout FromDestination(string raw)
    {
        var trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new SevDestinationLayout(false, string.Empty, string.Empty, string.Empty);
        }

        if (SevSignData.TryStripExpressBusPrefix(trimmed, out var remainder))
        {
            var (line1, line2) = SevSignData.SplitDestinationBody(remainder);
            return new SevDestinationLayout(true, ExpressBusLabel, line1, line2);
        }

        var (primary, secondary) = SevSignData.SplitDestinationBody(trimmed);
        return new SevDestinationLayout(false, string.Empty, primary, secondary);
    }
}
