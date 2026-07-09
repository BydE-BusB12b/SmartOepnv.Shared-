namespace SmartOepnv.AppShared.Employees;

/// <summary>Deutsche Fahreranleitung für die Smart-ÖPNV-Einweisungs-PDF.</summary>
internal static class EmployeeBriefingManualContent
{
    public static IReadOnlyList<BriefingSection> Sections { get; } =
    [
        new BriefingSection(
            "1. Anmeldung und Sicherheit",
            """
            • Melden Sie sich zu Dienstbeginn an: zuerst Ihre 4-stellige Personalnummer eingeben, danach Ihr persönliches Passwort (siehe Abbildungen unten).
            • Die Anmeldung ist Voraussetzung für die Zeitwirtschaft und die ordnungsgemäße Nutzung der App.
            • Verwenden Sie die Pause-Funktion im Menü, wenn Sie das Gerät kurzzeitig unbeaufsichtigt lassen.
              Während der Pause bleiben Sie angemeldet; zum Fortfahren geben Sie Ihr Anmeldepasswort erneut ein (siehe Abschnitt 8).
            • Melden Sie sich am Dienstende über „Abmelden“ ab – nicht nur über Pause.
            • Wenn das Fahrzeug abgestellt wird, nutzen Sie statt „Abmelden“ den Button „App beenden“ und schalten anschließend das Gerät aus.
            • Geben Sie Ihr Passwort nicht an Dritte weiter. Bei Verlust melden Sie sich umgehend in der Personalverwaltung.
            """,
            [
                new BriefingImage("Personalnummer eingeben", "briefing_personnel_number.png"),
                new BriefingImage("Passworteingabe", "briefing_password_login.png")
            ]),
        new BriefingSection(
            "2. Linie/Kurs und Fahrtauswahl",
            """
            • In der ITCS-Ansicht tippen Sie auf den hellblauen Button „Linie/Kurs“ in der Seitenleiste (siehe Abbildung unten).
            • Geben Sie Linie und Kurs über das Ziffernfeld ein (z. B. 128/01) und bestätigen Sie mit „OK“.
            • Anschließend öffnet sich der Fahrtauswahldialog „Routen für Linie/Kurs: …“ mit allen verfügbaren Fahrten.
            • Jede Zeile zeigt die Fahrtnummer und den Routennamen, z. B. „(Fahrt: 01) S28 Neuss / Kaarst, Kaarster See“.
            • „Route suchen…“ filtert die Liste; „Sortieren“ ändert die Reihenfolge.
            • Die Anzahl gefundener Routen wird unter dem Suchfeld angezeigt.
            • Tippen Sie die richtige Fahrt an – die Haltestellenliste und Zielanzeige werden geladen.
            • Oben in der Statuszeile werden Linie/Kurs, Routenname und Ziel: angezeigt.
            • Ist an der Starthaltestelle kein Ziel hinterlegt, steht dort „Ziel: Kein Ziel“ (niederländisch: „Doel: Geen doel“).
            • „ABBRECHEN“ schließt den Dialog ohne Auswahl; bei unbekannter Linie/Kurs erscheint „Keine Route verfügbar“.
            • Wählen Sie vor jeder Fahrt die korrekte Linie/Kurs-Kombination und Fahrt aus. Standardmäßig erfolgt der Routenwechsel bei geplanten Fahrten automatisch.
            """,
            [
                new BriefingImage("Fahrtauswahl nach Eingabe von Linie/Kurs", "briefing_fahrtauswahl.png")
            ]),
        new BriefingSection(
            "3. ITCS – Fahren und Linienbetrieb",
            """
            • Die ITCS-Seite ist Ihre Hauptansicht im Linienbetrieb: Haltestellenfolge, Linie/Kurs und Statushinweise (siehe Abbildungen unten).
            • In der Kopfzeile sehen Sie Linie/Kurs, Routenname und Ziel: – ohne hinterlegtes Starthaltestellenziel erscheint „Ziel: Kein Ziel“.
            • Pas.-Info: rot „off“ = Fahrgastinformation und Ansagen aktiv, grün „on“ = deaktiviert – vor Fahrtantritt einschalten, wenn vorgesehen.
            • IBIS-USB in der Statuszeile: rot = nicht verbunden, grün = verbunden, blau blinkend = sendet gerade IBIS-Daten.
            • Die aktuelle Haltestelle ist gelb umrandet; ausfallende Haltestellen erscheinen rot mit X.
            • Symbol mit Fragezeichen in der Statuszeile: Route wurde verlassen – fehlt die Linienführung oder gibt es Fehler, diese bitte über Mängelmeldung melden oder an die Planung wenden.
            • Wählen Sie vor Abfahrt Linie/Kurs und Fahrt aus – Ablauf in Abschnitt 2; prüfen Sie Zielanzeige und Fahrtrichtung.
            • Haltestellen werden in der Reihenfolge des Fahrplans angefahren; Abweichungen nur nach betrieblicher Anweisung.
            • Sonderziele werden über die vorgesehenen Menüpunkte gesetzt (Anzeige).
            • Bei Störungen oder Unklarheiten wenden Sie sich an die Leitstelle – schnelle Meldungen über „Mail“ (Abschnitt 9).
            """,
            [
                new BriefingImage("ITCS – Pas.-Info aus", "briefing_itcs_pasinfo_off.png"),
                new BriefingImage(
                    "ITCS – Pas.-Info an, ausfallende und aktuelle Haltestelle, Route verlassen",
                    "briefing_itcs_pasinfo_on.png")
            ]),
        new BriefingSection(
            "4. Pas.Info, Ansagen und Fahrgastinformation",
            """
            • „Pas.Info“ wird generell immer aktiviert – die App gibt automatische Haltestellenansagen und Fahrgastinformationen aus.
            • Die App erkennt Haltestellen über GPS und gibt Ansagen bzw. Anzeigen entsprechend dem Fahrplan aus.
            • Prüfen Sie die Außenanzeige vor Fahrtantritt.
            • Manuelle Ansagen und Sonderdurchsagen sind über die vorgesehenen Tasten im Menü möglich.
            • Bei Leitstellendurchsagen erscheint ein Lautsprechersymbol. Diese Durchsagen können nicht unterbrochen werden.
            • Unterbrechen Sie andere laufende Durchsagen nicht ohne betrieblichen Grund.
            """),
        new BriefingSection(
            "5. Menü, Einstellungen und Kommunikation",
            """
            • Das Hauptmenü öffnen Sie über den Button „MENÜ“ unten rechts (siehe Abbildung unten).
            • Zeitwirtschaft (grün): Kommen- und Gehen-Zeiten erfassen – nach betrieblicher Vorgabe zu Dienstbeginn und -ende nutzen.
            • Mängelkarte (gelb): Mängel am Fahrzeug melden – Details in Abschnitt 7.
            • Pause (rot): Gerät kurzzeitig sperren – Details in Abschnitt 8.
            • Einstellungen: App-Konfiguration; Abmelden: Dienstende; App beenden: Fahrzeug abgestellt – danach Gerät ausschalten.
            • Das Gerät im Bus kommuniziert kontinuierlich mit der Leitstelle. Die Leitstelle hat Zugriff auf einige Funktionen und kann so Ziele oder Routen aus der Ferne steuern.
            • Prüfen Sie regelmäßig eingehende Leitstellennachrichten und bestätigen Sie diese, wenn vorgesehen.
            • Bei technischen Problemen dokumentieren Sie Uhrzeit, Linie und Fehlerbild und informieren Sie die Leitstelle.
            """,
            [
                new BriefingImage(
                    "Hauptmenü – Zeitwirtschaft, Mängelkarte und Pause",
                    "briefing_main_menu.png")
            ]),
        new BriefingSection(
            "6. Zeitwirtschaft und Dokumente",
            """
            • Die Zeitwirtschaft erreichen Sie im Hauptmenü über den grünen Button „Zeitwirtschaft“ (siehe Abbildung unten).
            • Arbeitsbeginn jetzt: sofort einstempeln; Arbeitsende: ausstempeln zum Dienstende.
            • Angepasster Arbeitsbeginn: Einstempeln mit abweichender Uhrzeit – maximal 30 Minuten in die Vergangenheit, nicht in die Zukunft.
            • Unten werden Ihre Einträge des aktuellen Monats angezeigt.
            • Zeile mit Pfeil (→): Korrektur eines früheren Stempels; durchgestrichene Zeile: Stornierung.
            • Eine vorzeitige Abmeldung oder das Schließen der App ohne Pause kann zu fehlerhaften Zeiten führen.
            • Bei Fragen zu Arbeitszeiten wenden Sie sich an Ihre Disposition bzw. Personalverwaltung.
            """,
            [
                new BriefingImage("Zeitwirtschaft – Stempeln und Monatsübersicht", "briefing_zeitwirtschaft.png")
            ]),
        new BriefingSection(
            "7. Mängelkarte",
            """
            • Die Mängelkarte erreichen Sie im Hauptmenü über den gelben Button „Mängelkarte“ (siehe Abbildung unten).
            • In der Liste sehen Sie alle offenen Mängel: nicht bearbeitete („Neu“) und in Bearbeitung befindliche Einträge.
            • Erledigte bzw. abgehakte Mängel verschwinden aus dieser Ansicht – sie bleiben in der Werkstatt/Disposition im Planer erhalten.
            • Neuen Mangel melden: Kurzbeschreibung eingeben (maximal 60 Zeichen) und „Speichern“ drücken.
            • „Aktualisieren“ lädt die aktuelle Liste aus der Cloud – z. B. wenn die Werkstatt den Status geändert hat.
            • Sie können Einträge nur anlegen, nicht bearbeiten oder abhaken; Bearbeitung erfolgt in der Disposition/Werkstatt.
            • Melden Sie Mängel und Schäden am Fahrzeug unverzüglich, bevor Sie den Dienst beginnen oder das Fahrzeug abgeben.
            """,
            [
                new BriefingImage("Mängelkarte – offene Einträge und neuer Mangel", "briefing_maengelkarte.png")
            ]),
        new BriefingSection(
            "8. Pause",
            """
            • Die Pause aktivieren Sie im Hauptmenü über den roten Button „Pause“ (siehe Abbildung unten).
            • Die App wird sofort gesperrt; oben erscheint der Hinweis „App pausiert – Passwort erforderlich“.
            • Während der Pause bleiben Sie angemeldet – auch wenn der Bildschirm ausgeschaltet wird oder die App in den Hintergrund wechselt.
            • Zum Fortfahren „Pause beenden“: Ihr Anmeldepasswort über das Ziffernfeld eingeben und „OK“ bestätigen.
            • „Löschen“ entfernt die letzte Eingabe; bei falschem Passwort erneut versuchen.
            • „Zwangsabmelden“ beendet die Anmeldung vollständig – nur nutzen, wenn Sie das Passwort vergessen haben oder den Dienst beenden möchten.
            • Nutzen Sie Pause, wenn Sie das Gerät kurzzeitig unbeaufsichtigt lassen – nicht als Ersatz für die Abmeldung am Dienstende.
            """,
            [
                new BriefingImage("Pause beenden – Passworteingabe", "briefing_pause.png")
            ]),
        new BriefingSection(
            "9. Mail – Meldungen an die Leitstelle",
            """
            • In der ITCS-Ansicht tippen Sie auf den dunkelgrünen Button „Mail“ in der Seitenleiste (siehe Abbildung unten).
            • Der Dialog „Mail-Vorlage auswählen“ zeigt vorgefertigte Meldungstexte, die im Planer hinterlegt sind.
            • Beispiele: „Fahrzeug defekt“, „Streckensperrung“, „Unfall mit Personenschaden“, „Unfall ohne Personenschaden“.
            • Tippen Sie die passende Vorlage an – die Meldung wird automatisch an die Leitstelle gesendet.
            • Mitgesendet werden u. a. Ihr Standort, die aktuelle Linie/Kurs-Kombination und das Ziel.
            • Nach erfolgreichem Versand erscheint ein Bestätigungssymbol in der Statuszeile.
            • „Schließen“ beendet den Dialog ohne Meldung.
            • Bei akutem Notfall nutzen Sie den roten Button „Unfallruf“. So werden Sie in der Leitstelle direkt aufgerufen.
            """,
            [
                new BriefingImage("Mail-Vorlage auswählen", "briefing_mail.png")
            ]),
    ];
}

internal sealed record BriefingImage(string Caption, string AssetFileName);

internal sealed record BriefingSection(
    string Title,
    string Body,
    IReadOnlyList<BriefingImage>? Images = null);
