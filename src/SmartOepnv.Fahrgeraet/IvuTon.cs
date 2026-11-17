namespace SmartOepnv.Fahrgeraet;

/// <summary>
/// IVU-Töne der App. Sie laufen über die Lautsprecher des HDMI-Displays.
/// </summary>
public static class IvuTon
{
    public static bool Piep() => Play("IVU_piep_ton.wav");

    public static bool Error() => Play("IVU_piep_error.wav");

    public static bool Funk() =>
        GeraetEinstellungen.Bool("funk_tone_enabled", true) && Play("IVU_piep_funk.wav");

    public static bool Ticket() => Play("IVU_eTicket_Kontrolle.wav");

    public static bool Mail() => Play("IVU_mailchat_notification.mp3");

    public static bool Unfall() => Play("IVU_mailchat_unfallruf.mp3");

    private static bool Play(string fileName)
    {
        if (!GeraetEinstellungen.Bool("button_sounds_enabled", true))
        {
            return true;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "sounds", fileName);
        return File.Exists(path) && TonAusgang.PlayFileOnDisplay(path);
    }
}
