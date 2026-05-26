namespace SmartOepnv.Core.RoutePackage;

public enum DriverCredentialWarningLevel
{
    /// <summary>1–90 Tage vor Ablauf (gelb).</summary>
    ExpiringSoon = 1,

    /// <summary>Ablauf heute oder überfällig (rot).</summary>
    Expired = 2
}
