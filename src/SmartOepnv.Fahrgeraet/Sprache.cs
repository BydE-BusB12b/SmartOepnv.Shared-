namespace SmartOepnv.Fahrgeraet;

/// <summary>0 Deutsch, 1 Niederländisch, 2 Englisch. Dieselben Texte wie auf dem Tablet.</summary>
public static class Sprache
{
    public static bool Niederlaendisch =>
        GeraetEinstellungen.Text("language_selected_index", "0") == "1";

    public static string T(string deutsch, string niederlaendisch) =>
        Niederlaendisch ? niederlaendisch : deutsch;
}
