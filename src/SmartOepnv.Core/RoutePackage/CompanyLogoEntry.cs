namespace SmartOepnv.Core.RoutePackage;

public sealed class CompanyLogoEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
}
