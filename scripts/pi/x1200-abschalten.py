#!/usr/bin/env python3
"""Geekworm X1200: 15 Minuten nur auf Akku, dann sauber herunterfahren.

Es gibt keine GPIO-Steuerung. Das X1200 meldet den Netzausfall selbst
über seine Statusleitung am Pin 31. Aktiv bzw. Wert 1 bedeutet Netzteil da.
Der Dienst liest nur diese Meldung und schaltet keinen Ausgang.
Im Originalskript ist das Herunterfahren auskommentiert. Hier wartet der Pi
15 Minuten und fährt nur herunter, wenn das Netz weg bleibt.
"""

import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

import gpiod

# Statusleitung des X1200, Pin 31. Nur lesen, nichts schalten.
STATUSLEITUNG = 6
NACHLAUF_SEKUNDEN = 15 * 60
ZUSTAND = Path("/run/smart-oepnv/x1200-zustand")
PROBE = "--probe" in sys.argv


def schreibe(akku_seit_wand):
    """Das Fahrgerät liest diese Datei. Die Statusleitung bleibt bei diesem Dienst."""
    ZUSTAND.parent.mkdir(parents=True, exist_ok=True)
    text = "netz\n" if akku_seit_wand is None else f"akku {int(akku_seit_wand)}\n"
    tmp = ZUSTAND.with_suffix(".tmp")
    tmp.write_text(text)
    os.chmod(ZUSTAND.parent, 0o755)
    os.chmod(tmp, 0o644)
    tmp.replace(ZUSTAND)


def lies_seit():
    try:
        teile = ZUSTAND.read_text().split()
    except OSError:
        return None
    if len(teile) >= 2 and teile[0] == "akku":
        try:
            return float(teile[1])
        except ValueError:
            return None
    return None


def chip_pfad() -> str:
    """Der 40-polige Anschluss des Pi 5 hängt am RP1-Chip."""
    kandidaten = []
    for pfad in sorted(Path("/dev").glob("gpiochip*")):
        label = ""
        try:
            if hasattr(gpiod, "Chip"):
                chip = gpiod.Chip(str(pfad))
                try:
                    info = chip.get_info() if hasattr(chip, "get_info") else None
                    if info is not None:
                        label = f"{info.name} {info.label}"
                    else:
                        label = f"{chip.name()} {chip.label()}"
                finally:
                    close = getattr(chip, "close", None)
                    if close:
                        close()
        except Exception:
            label = ""
        kandidaten.append((str(pfad), label.lower()))
    for pfad, label in kandidaten:
        if "rp1" in label:
            return pfad
    vorhanden = {pfad for pfad, _ in kandidaten}
    for bevorzugt in ("/dev/gpiochip0", "/dev/gpiochip4"):
        if bevorzugt in vorhanden:
            return bevorzugt
    if kandidaten:
        return kandidaten[0][0]
    raise SystemExit("Kein GPIO-Chip gefunden.")


class NetzLeser:
    def __init__(self, pfad: str) -> None:
        self._request = None
        self._line = None
        if hasattr(gpiod, "request_lines"):
            self._request = gpiod.request_lines(
                pfad,
                consumer="x1200-abschalten",
                config={STATUSLEITUNG: gpiod.LineSettings(direction=gpiod.line.Direction.INPUT)},
            )
            return
        chip = gpiod.Chip(pfad)
        self._line = chip.get_line(STATUSLEITUNG)
        self._line.request(consumer="x1200-abschalten", type=gpiod.LINE_REQ_DIR_IN)

    def netz_da(self) -> bool:
        if self._request is not None:
            wert = self._request.get_values()[0]
            return wert == gpiod.line.Value.ACTIVE
        return self._line.get_value() == 1

    def close(self) -> None:
        if self._request is not None:
            self._request.release()
        elif self._line is not None:
            self._line.release()


def herunterfahren() -> None:
    befehl = shutil.which("shutdown") or "/sbin/shutdown"
    print("15 Minuten nur auf Akku. Der Pi fährt jetzt herunter.", flush=True)
    subprocess.run([befehl, "-h", "now"], check=False)


def main() -> None:
    pfad = chip_pfad()
    leser = NetzLeser(pfad)
    print(f"X1200 beobachtet die Statusleitung an {pfad}. Nachlauf {NACHLAUF_SEKUNDEN // 60} Minuten.", flush=True)
    akku_seit = None
    akku_wand = None
    try:
        if PROBE:
            zustand = "Netz" if leser.netz_da() else "Akku"
            print(f"Zustand: {zustand}", flush=True)
            return
        if not leser.netz_da():
            akku_wand = lies_seit() or time.time()
            akku_seit = time.monotonic() - max(0.0, time.time() - akku_wand)
            schreibe(akku_wand)
        else:
            schreibe(None)
        while True:
            if leser.netz_da():
                if akku_seit is not None:
                    print("Netz ist zurück. Die Abschaltung ist abgebrochen.", flush=True)
                    akku_seit = None
                    akku_wand = None
                    schreibe(None)
            else:
                jetzt = time.monotonic()
                if akku_seit is None:
                    akku_seit = jetzt
                    akku_wand = time.time()
                    schreibe(akku_wand)
                    print("Nur noch Akku. Abschaltung in 15 Minuten, sofern das Netz weg bleibt.", flush=True)
                elif jetzt - akku_seit >= NACHLAUF_SEKUNDEN:
                    herunterfahren()
                    return
            time.sleep(2)
    finally:
        leser.close()


if __name__ == "__main__":
    main()
