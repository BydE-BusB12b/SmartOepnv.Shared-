# Smart ÖPNV – Einkaufsliste Raspberry Pi (Fahrerarbeitsplatz)

Stand: 05.11.2026

Diese Datei ist die Quelle der Einkaufsliste im Projekt. Dieselbe Liste steht als PDF unter `docs/hardware/SmartOepnv_RaspberryPi_Einkaufsliste.pdf`. Bei einer Änderung beide Dateien und `_tools/RaspberryPiEinkaufsliste` zusammen anpassen.

Der Pi ersetzt das Fahrer-Tablet nicht als Android-App. Die Liste ist die Hardware für ein eigenes Linux-Gerät im Fahrzeug.

## Festgelegte Anschlüsse

- Amp2 links = Ansage innen
- Amp2 rechts = Ansage außen
- Bluetooth des Pi = Fahrer (Tastentöne, Eincheckton, VoIP). Ein Bluetooth-Stick ist nicht nötig.
- Mobilfunk und GPS über den PCIe-Anschluss, nicht über USB
- ANT0 = Mobilfunk-Dachantenne, ANT3 = passive GPS-Antenne
- Sprache nur per VoIP über die Datenverbindung. Keine Telefonie über die SIM.
- USB-Port 1 = IBIS-Wandler, USB-Port 2 = RS485-Wandler
- Der Pi 5 hat vier USB-A-Buchsen (zwei USB 3, zwei USB 2). Zwei bleiben frei.
- System auf microSD. Der PCIe-Anschluss ist durch das 5G-HAT belegt, deshalb kein NVMe.

## 1. Rechner und Bildschirm

| Anz. | Teil | Wofür |
| --- | --- | --- |
| 1 | Raspberry Pi 5, 8 GB | Rechner |
| 1 | Offizieller Active Cooler für den Pi 5 | Kühlung im Fahrzeug |
| 1 | microSD 64 GB, Klasse A2 | System und Programm. Kein NVMe. |
| 1 | Touchdisplay per DSI, Querformat, möglichst groß | Bedienung. Darf keinen USB-Port belegen. Offizielles Touch Display 2 (7 Zoll) ist die sichere kleine Variante. |
| 1 | RTC-Batterie für den Pi 5 | Uhrzeit nach Spannungsverlust, für die Zeitwirtschaft |

Das offizielle Touch Display 2 braucht neben dem Flachbandkabel ein kurzes Kabel an die Stiftleiste. Der Stapel aus 5G-HAT und Amp2 muss diese Pins noch erreichbar lassen. Ein HDMI-Touchdisplay ist ungeeignet, wenn der Touch einen USB-Port braucht.

## 2. Mobilfunk und GPS

| Anz. | Teil | Wofür |
| --- | --- | --- |
| 1 | Waveshare PCIe to 5G HAT+ für den Pi 5, mit Quectel RM520N-GL | Daten-SIM und GPS über das flache PCIe-Kabel. Europa-Bänder. |
| 1 | Nano-SIM, nur Daten | Leitstelle, Dropbox, ITCS, VoIP |
| 2 | Adapter IPEX-4 (MHF4) auf SMA, kurz | ANT0 und ANT3. Bleiben im Gehäuse. Oft im HAT-Set enthalten. |
| 1 | Dachantenne Mobilfunk, SMA, 50 Ω | an ANT0 |
| 1 | Dachantenne GPS, passiv, SMA | an ANT3, Sicht nach oben |
| 2 | Antennenkabel SMA, Länge nach Fahrzeug | Leitung zum Dach. Zug nur am SMA, nicht am IPEX. |

ANT1 und ANT2 sind weitere Mobilfunkantennen. Für die Datenverbindung sind sie nicht nötig.

GPS im RM520N-GL ist vorhanden und wird in der Software eingeschaltet. Eine eigene GPS-Maus ist nicht vorgesehen.

## 3. Ton

| Anz. | Teil | Wofür |
| --- | --- | --- |
| 1 | HiFiBerry Amp2 (HB-AMP2) | Ansagen. Schraubklemmen links und rechts. |
| 1 | Passiver Lautsprecher 4–8 Ω, innen | Fahrgastraum, an der linken Klemme |
| 1 | Passiver Lautsprecher 4–8 Ω, außen | Außenseite, an der rechten Klemme |
| 1 | Lautsprecherlitze, zweiadrig | Zu beiden Lautsprechern. Links und rechts nicht brücken. |
| 1 | Bluetooth-Lautsprecher, fest am Fahrerplatz, eigene Stromversorgung | Tastentöne, Eincheckton, VoIP |

Gesehener Shop-Preis Amp2: 64,90 €. Das ist kein Angebot, nur die Orientierung aus der angesehenen Seite.

Eine Lautsprecherklemme am Amp2 ist gegenüber dem älteren Amp+ umgepolt. Es gilt der Aufdruck auf der Platine.

## 4. Strom aus dem 24-V-Bordnetz

| Anz. | Teil | Wofür |
| --- | --- | --- |
| 1 | DC/DC 24 V auf 18 V, mindestens 4 A | nur der Amp2. Bei 4-Ω-Lautsprechern 18 V, nicht das rohe Bordnetz. Darüber laufen Pi, Display und die USB-Wandler. |
| 1 | Sicherungshalter mit Sicherung auf der 24-V-Seite | vor dem 18-V-Wandler |

Am Amp2 gibt es keine 5-V-Brücke zum Ziehen. Die Platine versorgt den Pi fest über die Stiftleiste. Display und USB-Wandler hängen an derselben 5-V-Leitung. Ein zweites 5-V-Netzteil darf dort nicht hin: nicht an den USB-C-Anschluss des Pi, nicht an die 5-V-Pins und nicht ans Displaykabel.

Ein DC/DC 24 V auf 5 V kommt nur dazu, wenn der zusätzliche Stromeingang der 5G-Platine von diesen Pins getrennt ist. Ist er mit den Pins verbunden, bleibt der Wandler weg. Das Mobilfunkmodul läuft dann mit über den Amp2. Dessen 5 V sind für Pi, Display und Mobilfunk zusammen knapp.

## 5. Nur für den Prüfplatz an 230 V

| Anz. | Teil | Wofür |
| --- | --- | --- |
| 1 | Mean Well GST60A18-P1J, 18 V | Amp2 am Schreibtisch. Damit läuft auch der Pi. Nicht das Fahrzeugnetzteil. |
| 1 | Offizielles Netzteil Raspberry Pi 5, 27 W, USB-C | Pi am Schreibtisch, nur solange der Amp2 nicht aufgesteckt ist |

Gesehener Shop-Preis des Mean Well: 19,90 €.

## 6. Fahrzeugbus

| Anz. | Teil | Wofür |
| --- | --- | --- |
| 1 | USB-IBIS-Wandler | IBIS. Belegt eine USB-Buchse. |
| 1 | USB-RS485-Wandler | RS485. Belegt eine USB-Buchse. |

## 7. Mechanik

| Anz. | Teil | Wofür |
| --- | --- | --- |
| 1 | GPIO-Stacking-Header 40-polig, extralang | Amp2 sitzt über dem 5G-HAT. Das HAT reicht die Pins durch. Abstand vor dem Einbau prüfen. |
| 1 | Montageplatte oder Fahrzeuggehäuse mit Lüftung | Pi, beide Aufsteckplatinen, Klemmen, SMA-Anschlüsse |
| 1 | Abstandshalter und Schrauben passend zum Stapel | Befestigung |

Ein geschlossenes Standardgehäuse für den nackten Pi passt nicht über 5G-HAT und Amp2.

## Nicht kaufen

- kein USB-LTE-Stick und kein USB-GPS
- kein GSM-Modul für Telefonate
- kein zweites Audio-HAT und kein NVMe-HAT
- kein HDMI-Touch, der einen USB-Port für den Touch braucht
- kein Bluetooth-Stick
