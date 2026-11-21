#!/usr/bin/env python3
"""Innenanzeige für den Pi.

Nach dem Start wählt man das Design.
NEW ist das bisherige Bild: Haltestelle und Verlauf.
Monitor ist das eigene Bild mit drei Seiten: Haltestelle, Verlauf, Umsteigen.
Uhrzeit und Datum laufen auf dem Pi. Linie, Ziel und Halte kommen
von der IBIS-IP-App.
"""

from __future__ import annotations

import atexit
import json
import math
import os
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from xml.etree import ElementTree as ET

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
LAYOUT = ROOT / "layout"
STATE_PATH = Path.home() / "ibis-ip-simulant.json"
WIDTH, HEIGHT = 1440, 900
FOOTER = 100
TICKER_SIZE = 34
NEXT_SECONDS = 15
VERLAUF_SECONDS = 15
AD_SECONDS = 90
AD_VIDEO = ROOT / "werbung" / "verhalten-im-bus.mp4"
DESIGN_PATH = Path.home() / ".config" / "smart-oepnv-ip" / "design"
DESIGNS = (
    ("new", "NEW", "Haltestelle und Verlauf"),
    ("monitor", "Monitor", "Haltestelle, Verlauf und Umsteigen"),
)
COMPANY_EMAIL = "info@smart-oepnv.de"
BLUE = (10, 78, 168)
GREEN = (15, 148, 104)
ORANGE = (242, 120, 22)
NEON_YELLOW = (255, 255, 0)
ORANGE_SOFT = (255, 244, 232)
TURQUOISE = (206, 236, 234)
TURQUOISE_ROW = (186, 224, 224)
HOLD_RED = (196, 28, 32)
INK = (18, 46, 82)

SAMPLE = {
    "linie": "704",
    "ziel": "Hauptbahnhof",
    "halte": [
        "Rathaus",
        "Markt",
        "Weststraße",
        "Schule",
        "Bahnhof",
        "Industriegebiet",
        "Stadtmitte",
        "Hauptbahnhof",
    ],
    "halt_index": 0,
    "haelt": False,
}


def clock_now() -> datetime:
    try:
        from zoneinfo import ZoneInfo
        return datetime.now(ZoneInfo("Europe/Berlin"))
    except Exception:
        return datetime.now()


def load_state() -> dict:
    state = dict(SAMPLE)
    try:
        saved = json.loads(STATE_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return state
    if isinstance(saved, dict):
        for key in ("linie", "ziel", "halte", "halt_index", "haelt"):
            if key in saved:
                state[key] = saved[key]
    if isinstance(state["halte"], str):
        state["halte"] = [part.strip() for part in state["halte"].splitlines() if part.strip()]
    return state


def font_file(bold: bool) -> str | None:
    candidates = [
        r"C:\Windows\Fonts\ariblk.ttf" if bold else r"C:\Windows\Fonts\arial.ttf",
        r"C:\Windows\Fonts\arialbd.ttf" if bold else r"C:\Windows\Fonts\arial.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    ]
    for path in candidates:
        if Path(path).exists():
            return path
    return None


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    path = font_file(bold)
    if path:
        return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def fit(draw: ImageDraw.ImageDraw, text: str, size: int, bold: bool, max_width: int) -> ImageFont.ImageFont:
    current = max(12, size)
    chosen = font(current, bold)
    while current > 18 and draw.textlength(text, font=chosen) > max_width:
        current -= 4
        chosen = font(current, bold)
    return chosen


def passenger_stops(stops) -> list[str]:
    names = [str(part).strip() for part in stops if str(part).strip()]
    if names and all(name.casefold() in {"start", "ende"} for name in names):
        return []
    return names


def monitor_line(state: dict) -> str:
    """Vor der ersten Fahrt steht 000. Eine gelöschte Linie bleibt leer, das Ziel bleibt stehen."""
    line = str(state.get("linie") or "").strip()
    if line:
        return line
    if state.get("empfangen") and str(state.get("ziel") or "").strip():
        return ""
    return "000"


def upcoming(state: dict, count: int) -> list[str]:
    stops = passenger_stops(state["halte"])
    if not stops:
        return []
    index = int(state.get("halt_index", 0)) % len(stops)
    return stops[index:index + count]


def paint(kind: str, state: dict, moment: datetime) -> Image.Image:
    background = "Backplane2.jpg" if kind == "next" else "Backplane.jpg"
    image = Image.open(LAYOUT / background).convert("RGBA")
    draw = ImageDraw.Draw(image)
    line = str(state.get("linie") or "")
    destination = str(state.get("ziel") or "")
    if kind == "next":
        draw_anchored(draw, line, 150, 53, 120, True, (0, 0, 0), "left", 520)
        draw_anchored(draw, destination, 220, 250, 160, True, (0, 0, 0), "left", 1100)
        stops = upcoming(state, 1)
        if stops:
            draw_anchored(draw, stops[0], 720, 648, 150, True, (255, 245, 0), "middle", 1200)
        draw_anchored(draw, f"{moment.hour}:{moment.minute:02d}", 1028, 55, 78, False, (0, 0, 0), "center", 280)
        draw_anchored(
            draw,
            f"{moment.day:02d}.{moment.month:02d}.{moment.year}",
            1028,
            128,
            22,
            True,
            (0, 0, 0),
            "center",
            320,
        )
        if state.get("haelt"):
            banner = Image.open(LAYOUT / "WH.jpg").convert("RGBA")
            banner = banner.resize((980, 120))
            image.alpha_composite(banner, (230, 760))
    else:
        draw_anchored(draw, line, 210, 18, 72, True, (0, 0, 0), "left", 280)
        draw_anchored(draw, destination, 1320, 18, 64, True, (0, 0, 0), "right", 700)
        stops = upcoming(state, 8)
        if stops:
            draw_anchored(draw, stops[0], 140, 839, 60, True, (255, 245, 0), "left-middle", 1100)
        # Gelbe Punkte von unten nach oben, Text auf derselben Höhe rechts daneben.
        dots = ((746, 46), (657, 57), (568, 67), (478, 76), (387, 87), (298, 97), (205, 109))
        for (dot_y, dot_x), name in zip(dots, stops[1:]):
            draw_anchored(draw, name, dot_x + 78, dot_y, 64, False, (0, 0, 0), "left-middle", 900)
    return image.convert("RGB")


def blend(start: tuple[int, int, int], end: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(int(start[i] + (end[i] - start[i]) * t) for i in range(3))


def horizontal_gradient(width: int, height: int, left: tuple[int, int, int], right: tuple[int, int, int]) -> Image.Image:
    row = Image.new("RGB", (max(1, width), 1))
    row.putdata([blend(left, right, i / max(1, width - 1)) for i in range(width)])
    return row.resize((width, height), Image.Resampling.BILINEAR)


def hold_warning(state: dict) -> bool:
    """Wagen hält: eine Sekunde rot, eine Sekunde die Haltestelle."""
    return bool(state.get("haelt")) and int(time.time()) % 2 == 1


def additional_text(state: dict) -> str:
    """Unteres Feld. Quelle ist AdditionalInformation, sonst die feste Adresse."""
    text = str(state.get("hinweis") or "").strip()
    return text or COMPANY_EMAIL


def additional_runs(text: str) -> bool:
    """Passt der Text in die Zeile, bleibt er stehen. Sonst läuft er von rechts nach links."""
    probe = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    return probe.textlength(text, font=font(TICKER_SIZE, True)) > WIDTH - 120


def monitor_course(state: dict) -> tuple[list[str], str]:
    """Fünf kommende Halte von oben, die aktuelle unten."""
    window = upcoming(state, 6)
    if not window:
        return ["", "", "", "", ""], ""
    above = list(reversed(window[1:]))
    return [""] * (5 - len(above)) + above, window[0]


def course_clocks(state: dict) -> list[str]:
    """Fahrplanzeiten in derselben Reihenfolge wie die Verlaufszeilen. Oben leer, unten aktuell."""
    names = passenger_stops(state.get("halte") or [])
    clocks = state.get("fahrplan") or []
    if not isinstance(clocks, list) or not names:
        return [""] * 6
    index = int(state.get("halt_index", 0)) % len(names)
    window = []
    for offset in range(6):
        absolute = index + offset
        if absolute >= len(names):
            break
        clock = str(clocks[absolute]).strip() if absolute < len(clocks) else ""
        window.append(clock)
    if not window:
        return [""] * 6
    above = list(reversed(window[1:]))
    return [""] * (5 - len(above)) + above + [window[0]]


BUS_INK = (8, 118, 132)


def paint_bus_badge(image: Image.Image, left: int, cy: int, diameter: int) -> int:
    """Weißer Kreis, BUS in Blau-Grün. Liefert die x-Position hinter dem Kreis."""
    draw = ImageDraw.Draw(image)
    draw.ellipse((left, cy - diameter / 2, left + diameter, cy + diameter / 2), fill=(255, 255, 255))
    draw.text(
        (left + diameter / 2, cy),
        "BUS",
        font=fit(draw, "BUS", int(diameter * 0.34), True, int(diameter * 0.78)),
        fill=BUS_INK,
        anchor="mm",
    )
    return left + diameter + 18


def paint_destination_arrow(draw: ImageDraw.ImageDraw, left: int, cy: int) -> int:
    """Gleicher Pfeil nach rechts wie auf der Haltestellenseite."""
    scale = 0.62
    points = ((0, -14), (76, -14), (76, -40), (144, 0), (76, 40), (76, 14), (0, 14))
    draw.polygon([(left + x * scale, cy + y * scale) for x, y in points], fill=(255, 255, 255))
    return int(left + 144 * scale) + 16


def paint_monitor_header(image: Image.Image, state: dict, moment: datetime) -> None:
    image.paste(horizontal_gradient(WIDTH, 158, (8, 58, 140), (12, 132, 118)), (0, 0))
    draw = ImageDraw.Draw(image)
    line_x = paint_bus_badge(image, 22, 78, 112)
    line_slot = 232
    draw_anchored(draw, monitor_line(state), line_x, 78, 72, True, (255, 255, 255), "left-middle", line_slot)
    dest_x = paint_destination_arrow(draw, line_x + line_slot + 10, 78)
    draw_anchored(
        draw,
        str(state.get("ziel") or ""),
        dest_x,
        78,
        64,
        True,
        (255, 255, 255),
        "left-middle",
        WIDTH - dest_x - 200,
    )
    draw_anchored(draw, f"{moment.hour}:{moment.minute:02d}", 1396, 52, 52, True, (255, 255, 255), "right", 250)
    draw_anchored(
        draw,
        f"{moment.day:02d}.{moment.month:02d}.{moment.year}",
        1396,
        112,
        24,
        False,
        (255, 255, 255),
        "right",
        250,
    )


def paint_footer(image: Image.Image, text: str, scrolling: bool) -> None:
    top = HEIGHT - FOOTER
    image.paste(horizontal_gradient(WIDTH, FOOTER, GREEN, BLUE), (0, top))
    if scrolling:
        draw_marquee(image, text, top, FOOTER)
        return
    if not text:
        return
    draw = ImageDraw.Draw(image)
    draw_anchored(draw, text, WIDTH // 2, top + FOOTER // 2, TICKER_SIZE, True, (255, 255, 255), "middle", WIDTH - 80)


def draw_marquee(image: Image.Image, text: str, top: int, height: int) -> None:
    chosen = font(TICKER_SIZE, True)
    text = f"+++ {text} +++"
    probe = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    gap = max(1, int(probe.textlength("+", font=chosen))) * 18
    text_width = max(1, int(probe.textlength(text, font=chosen)))
    cycle = text_width + gap
    offset = int(time.monotonic() * 110) % cycle
    strip = horizontal_gradient(WIDTH, height, GREEN, BLUE)
    pen = ImageDraw.Draw(strip)
    x = WIDTH - offset
    while x > -text_width:
        pen.text((x, height // 2), text, font=chosen, fill=(255, 255, 255), anchor="lm")
        x -= cycle
    image.paste(strip, (0, top))


def course_nudge(index: int) -> float:
    """Nach oben jede Haltestelle einen Schritt weiter rechts. Unten ist Index 5."""
    return (5 - index) * 46


def draw_course_arrow(draw: ImageDraw.ImageDraw, points: list[tuple[float, float]]) -> None:
    samples = []
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        for step in range(10):
            t = step / 10
            samples.append((x0 + (x1 - x0) * t, y0 + (y1 - y0) * t))
    if points:
        samples.append(points[-1])
    tip_x, tip_y = samples[-1]
    shaft_end = samples[-1]
    remain = 40.0
    for x0, y0 in reversed(samples[:-1]):
        dx, dy = tip_x - x0, tip_y - y0
        length = math.hypot(dx, dy)
        if length >= remain:
            shaft_end = (tip_x - dx * remain / length, tip_y - dy * remain / length)
            direction = (dx / length, dy / length)
            break
        remain -= length
        tip_x, tip_y = x0, y0
    else:
        direction = (0.0, -1.0)
    shaft = [point for point in samples if point[1] >= shaft_end[1] - 0.5]
    shaft.append(shaft_end)
    if len(shaft) >= 2:
        draw.line(shaft, fill=BLUE, width=16)
    ux, uy = direction
    px, py = -uy, ux
    head = (
        (samples[-1][0], samples[-1][1]),
        (shaft_end[0] + px * 26, shaft_end[1] + py * 26),
        (shaft_end[0] - px * 26, shaft_end[1] - py * 26),
    )
    draw.polygon(head, fill=BLUE)


def stop_sign(size: int) -> Image.Image:
    """Rundes Haltestellenzeichen: gelber Ring, grüner Ring, gelbe Mitte, grünes H."""
    logo = getattr(stop_sign, "logo", None)
    if logo is None:
        logo = Image.open(LAYOUT / "Haltestelle.png").convert("RGBA")
        stop_sign.logo = logo
    return logo.resize((size, size), Image.Resampling.LANCZOS)


def paste_stop_sign(image: Image.Image, cx: float, cy: float, size: int) -> None:
    sign = stop_sign(size)
    image.paste(sign, (int(cx - size / 2), int(cy - size / 2)), sign)


def paint_verlauf(state: dict, moment: datetime) -> Image.Image:
    image = Image.new("RGB", (WIDTH, HEIGHT), TURQUOISE)
    paint_monitor_header(image, state, moment)
    if not upcoming(state, 1):
        draw = ImageDraw.Draw(image)
        draw_anchored(
            draw,
            "Wir wünschen eine gute Fahrt.",
            WIDTH // 2,
            520,
            64,
            True,
            INK,
            "middle",
            1280,
        )
        hint = additional_text(state)
        paint_footer(image, "" if additional_runs(hint) else hint, False)
        return image
    above, current = monitor_course(state)
    bands = (72, 82, 92, 104, 116, 170)
    text_sizes = (34, 42, 48, 56, 64, 78)
    mark_sizes = (28, 34, 40, 48, 56, 72)
    area_top = 152
    spans = []
    y = area_top
    for height in bands:
        spans.append((y, y + height))
        y += height
    slots = list(above) + [current]
    clocks = course_clocks(state)
    named = [index for index, name in enumerate(slots) if name]
    draw = ImageDraw.Draw(image)
    marks = []
    for index in named:
        top, bottom = spans[index]
        cy = (top + bottom) / 2
        text_x = 188 + course_nudge(index)
        cx = text_x - 36 - mark_sizes[index] / 2
        marks.append((index, cx, cy, text_x))
    if marks:
        bottom_to_top = list(reversed(marks))
        arrow = [(cx, cy) for _index, cx, cy, _text_x in bottom_to_top]
        x0, y0 = arrow[-2] if len(arrow) > 1 else (arrow[-1][0], arrow[-1][1] + 40)
        x1, y1 = arrow[-1]
        span = math.hypot(x1 - x0, y1 - y0) or 1
        arrow.append((x1 + (x1 - x0) / span * 34, y1 + (y1 - y0) / span * 34))
        draw_course_arrow(draw, arrow)
    for index, cx, cy, text_x in marks:
        name = slots[index]
        top, bottom = spans[index]
        if index == 5:
            warning = hold_warning(state)
            box = (168, top + 6, WIDTH - 28, bottom - 6)
            fill = HOLD_RED if warning else ORANGE_SOFT
            outline = (140, 12, 16) if warning else ORANGE
            try:
                draw.rounded_rectangle(box, radius=22, fill=fill, outline=outline, width=7)
            except AttributeError:
                draw.rectangle(box, fill=fill, outline=outline, width=7)
            label = "Wagen hält!" if warning else name
            ink = (255, 255, 255) if warning else INK
            clock = clocks[index]
            reserve = 200 if clock else 56
            draw_anchored(draw, label, text_x, cy, text_sizes[index], True, ink, "left-middle", WIDTH - text_x - reserve)
            if clock:
                draw_anchored(draw, clock, WIDTH - 44, cy, text_sizes[index], True, ink, "right-middle", 180)
        else:
            clock = clocks[index]
            reserve = 200 if clock else 48
            draw_anchored(draw, name, text_x, cy, text_sizes[index], True, INK, "left-middle", WIDTH - text_x - reserve)
            if clock:
                draw_anchored(draw, clock, WIDTH - 44, cy, text_sizes[index], True, INK, "right-middle", 180)
        paste_stop_sign(image, cx, cy, mark_sizes[index])
    hint = additional_text(state)
    paint_footer(image, "" if additional_runs(hint) else hint, False)
    return image


def paint_haltestelle(state: dict, moment: datetime) -> Image.Image:
    """Oben Linie und Uhr, schmaler Zielstreifen, darunter Halt mit Symbol und Liveticker."""
    image = Image.new("RGB", (WIDTH, HEIGHT), TURQUOISE)
    image.paste(horizontal_gradient(WIDTH, 150, (8, 58, 140), (12, 132, 118)), (0, 0))
    holds = bool(state.get("haelt"))
    band = (186, 32, 36) if holds else (10, 92, 150)
    band_end = (150, 36, 40) if holds else (14, 130, 110)
    image.paste(horizontal_gradient(WIDTH, HEIGHT - FOOTER - 338, band, band_end), (0, 338))
    draw = ImageDraw.Draw(image)
    draw.rectangle((0, 150, WIDTH, 158), fill=(255, 255, 255))
    draw.rectangle((0, 330, WIDTH, 338), fill=(255, 255, 255))
    line_x = paint_bus_badge(image, 24, 75, 112)
    draw_anchored(draw, monitor_line(state), line_x, 75, 84, True, (255, 255, 255), "left-middle", 640)
    draw_anchored(draw, f"{moment.hour}:{moment.minute:02d}", 1376, 52, 56, True, (255, 255, 255), "right", 280)
    draw_anchored(
        draw,
        f"{moment.day:02d}.{moment.month:02d}.{moment.year}",
        1376,
        112,
        24,
        False,
        (255, 255, 255),
        "right",
        280,
    )
    draw.polygon(
        [(36, 230), (112, 230), (112, 204), (180, 244), (112, 284), (112, 258), (36, 258)],
        fill=GREEN,
    )
    draw_anchored(draw, str(state.get("ziel") or ""), 214, 244, 64, True, INK, "left-middle", 1100)
    draw_anchored(
        draw,
        "Wagen hält!" if holds else "Nächste Haltestelle",
        56,
        400,
        52,
        True,
        (255, 255, 255),
        "left",
        1100,
    )
    sign = 168
    sign_y = 590
    paste_stop_sign(image, 56 + sign / 2, sign_y, sign)
    stops = upcoming(state, 1)
    if stops:
        draw_anchored(
            draw,
            stops[0],
            56 + sign + 36,
            sign_y,
            132,
            True,
            NEON_YELLOW,
            "left-middle",
            WIDTH - sign - 160,
        )
    hint = additional_text(state)
    paint_footer(image, "" if additional_runs(hint) else hint, False)
    return image


TRANSFER_COLUMNS = (
    (48, 230, "Linie"),
    (290, 740, "Richtung"),
    (1050, 220, "Abfahrt"),
    (1290, 110, "Steig"),
)


def transfer_rows(state: dict) -> list[dict]:
    rows = state.get("umstiege")
    if isinstance(rows, list) and rows:
        return [row for row in rows if isinstance(row, dict)][:6]
    text = str(state.get("umstieg") or "").strip()
    if text and text.casefold() != "kein umstieg":
        return [{"linie": "", "richtung": "", "abfahrt": "", "steig": text}]
    return []


def paint_umsteigen(state: dict, moment: datetime) -> Image.Image:
    """Dieselbe Kopfzeile wie der Verlauf. Darunter Linie, Richtung, Abfahrt, Steig."""
    image = Image.new("RGB", (WIDTH, HEIGHT), TURQUOISE)
    paint_monitor_header(image, state, moment)
    draw = ImageDraw.Draw(image)
    for left, width, title in TRANSFER_COLUMNS:
        draw_anchored(draw, title, left, 198, 34, True, GREEN, "left-middle", width - 16)
    for left, _width, _title in TRANSFER_COLUMNS[1:]:
        draw.line((left - 18, 176, left - 18, 220), fill=BLUE, width=2)
    draw.line((40, 232, WIDTH - 40, 232), fill=BLUE, width=3)
    row_top = 240
    row_height = 90
    for index, row in enumerate(transfer_rows(state)):
        top = row_top + index * row_height
        if index % 2 == 0:
            draw.rectangle((0, top, WIDTH, top + row_height), fill=TURQUOISE_ROW)
        cy = top + row_height / 2
        departure = str(row.get("abfahrt") or "")
        delay = shown_delay(row.get("verspaetung") or "")
        if departure and delay:
            departure = f"{departure}  {delay}"
        values = (
            str(row.get("linie") or ""),
            str(row.get("richtung") or ""),
            departure,
            str(row.get("steig") or ""),
        )
        for (left, width, _title), value in zip(TRANSFER_COLUMNS, values):
            if value:
                draw_anchored(draw, value, left, cy, 42, True, INK, "left-middle", width - 24)
    hint = additional_text(state)
    paint_footer(image, "" if additional_runs(hint) else hint, False)
    return image


def paint_smart(kind: str, state: dict, moment: datetime, animate: bool = True) -> Image.Image:
    """Eigenes Bild. Verlauf mit sechs Halten, Haltestelle wie NEW in Weiß, Grün und Blau."""
    hint = additional_text(state)
    scrolling = kind in {"verlauf", "umsteigen", "next"} and additional_runs(hint)
    rows = tuple(
        (
            str(row.get("linie") or ""),
            str(row.get("richtung") or ""),
            " ".join(
                part
                for part in (str(row.get("abfahrt") or ""), shown_delay(row.get("verspaetung") or ""))
                if part
            ),
            str(row.get("steig") or ""),
        )
        for row in transfer_rows(state)
    )
    key = (
        kind,
        str(state.get("linie") or ""),
        str(state.get("ziel") or ""),
        tuple(upcoming(state, 6)),
        tuple(course_clocks(state)),
        rows,
        moment.strftime("%d.%m.%Y %H:%M"),
        hint,
        scrolling,
        hold_warning(state),
    )
    cached = getattr(paint_smart, "cache", None)
    if cached is None or cached[0] != key:
        if kind == "verlauf":
            image = paint_verlauf(state, moment)
        elif kind == "next":
            image = paint_haltestelle(state, moment)
        else:
            image = paint_umsteigen(state, moment)
        paint_smart.cache = (key, image)
    image = paint_smart.cache[1]
    if scrolling and animate:
        frame = image.copy()
        paint_footer(frame, additional_text(state), True)
        return frame
    return image


def saved_design() -> str:
    try:
        chosen = DESIGN_PATH.read_text(encoding="utf-8").strip()
    except OSError:
        return "new"
    return chosen if chosen in {item[0] for item in DESIGNS} else "new"


def remember_design(design: str) -> None:
    DESIGN_PATH.parent.mkdir(parents=True, exist_ok=True)
    DESIGN_PATH.write_text(design + "\n", encoding="utf-8")


def pages_for(design: str, state: dict) -> tuple[str, ...]:
    if design != "monitor":
        return ("next", "verlauf")
    if transfer_rows(state):
        return ("next", "umsteigen")
    return ("next", "verlauf")


def draw_anchored(draw, text, x, y, size, bold, fill, align, max_width, stroke: int = 0) -> None:
    chosen = fit(draw, text, size, bold, max_width)
    if align == "center":
        anchor = "mt"
    elif align == "middle":
        anchor = "mm"
    elif align == "left-middle":
        anchor = "lm"
    elif align == "right":
        anchor = "rt"
    elif align == "right-middle":
        anchor = "rm"
    else:
        anchor = "lt"
    draw.text(
        (x, y),
        text,
        font=chosen,
        fill=fill,
        anchor=anchor,
        stroke_width=stroke,
        stroke_fill=fill,
    )


class LiveTrip:
    """Was die App per IBIS-IP schickt. Leer, bis das erste Paket da ist."""

    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.data = {
            "linie": "",
            "ziel": "",
            "halte": [],
            "fahrplan": [],
            "halt_index": 0,
            "haelt": False,
            "empfangen": False,
            "ticker": "",
            "ticker_an": False,
            "hinweis": "",
            "umstiege": [],
            "aktueller_name": "",
        }

    def view(self) -> dict:
        with self.lock:
            data = json.loads(json.dumps(self.data))
        return data

    def apply_xml(self, raw: bytes) -> bool:
        try:
            root = ET.fromstring(raw)
        except ET.ParseError:
            return False
        found = read_trip(root)
        if not found:
            return False
        with self.lock:
            destination = str(found.get("ziel") or self.data.get("ziel") or "").strip()
            explicit_empty_line = "linie" in found and not str(found.get("linie") or "").strip()
            trip_cleared = self.data.get("empfangen") and destination and not str(
                self.data.get("linie") or ""
            ).strip()
            if explicit_empty_line and destination:
                found["halte"] = []
                found["fahrplan"] = []
                found.pop("aktueller_halt", None)
                found.pop("halt_index", None)
            elif trip_cleared and "halte" in found and "linie" not in found:
                found["halte"] = []
                found["fahrplan"] = []
                found.pop("aktueller_halt", None)
                found.pop("halt_index", None)
            current = found.pop("aktueller_halt", "")
            numeric_index = found.pop("halt_index", None)
            for key, value in found.items():
                if key == "linie":
                    self.data["linie"] = str(value).strip()
                    continue
                if key == "ziel" and not str(value).strip():
                    continue
                if key == "fahrplan":
                    continue
                if key == "halte":
                    names = passenger_stops(value)
                    self.data["halte"] = names
                    if not names:
                        self.data["fahrplan"] = []
                        self.data["halt_index"] = 0
                        self.data["aktueller_name"] = ""
                    continue
                self.data[key] = value
            if "halte" in found and passenger_stops(found["halte"]):
                clocks = found.get("fahrplan") if isinstance(found.get("fahrplan"), list) else []
                names = self.data["halte"]
                self.data["fahrplan"] = [
                    str(clocks[i]).strip() if i < len(clocks) else ""
                    for i in range(len(names))
                ]
            self.data["halte"] = passenger_stops(self.data["halte"])
            names = self.data["halte"]
            known = str(self.data.get("aktueller_name") or "")
            if current and current.casefold() not in {"start", "ende"}:
                if current in names:
                    self.data["halt_index"] = names.index(current)
                    self.data["aktueller_name"] = current
                elif not names and "halte" not in found:
                    line_now = str(self.data.get("linie") or "").strip()
                    destination_now = str(self.data.get("ziel") or "").strip()
                    if not (self.data.get("empfangen") and destination_now and not line_now):
                        self.data["halte"] = [current]
                        self.data["aktueller_name"] = current
            elif known and known in names:
                self.data["halt_index"] = names.index(known)
            elif numeric_index is not None:
                self.data["halt_index"] = numeric_index
            self.data["empfangen"] = True
            print(
                f"IBIS-IP {self.data['linie']} {self.data['ziel']} halte={len(self.data['halte'])}",
                flush=True,
            )
        return True

    def apply_json(self, raw: bytes) -> bool:
        try:
            payload = json.loads(raw.decode("utf-8"))
        except (UnicodeError, json.JSONDecodeError):
            return False
        if not isinstance(payload, dict):
            return False
        found = {}
        if payload.get("line") or payload.get("linie"):
            found["linie"] = str(payload.get("line") or payload.get("linie"))
        if payload.get("destination") or payload.get("ziel"):
            found["ziel"] = str(payload.get("destination") or payload.get("ziel"))
        for key in ("additionalInformation", "hinweis", "ticker", "liveticker", "tickerText"):
            if key in payload:
                found["hinweis"] = str(payload.get(key) or "").strip()
                break
        if any(key in payload for key in ("tickerEnabled", "onlineTickerEnabled", "ticker_an")):
            flag = payload.get("tickerEnabled", payload.get("onlineTickerEnabled", payload.get("ticker_an")))
            found["ticker_an"] = flag not in (False, 0, "0", "false", "nein", "no")
            if not found["ticker_an"] and "additionalInformation" not in payload and "hinweis" not in payload:
                found["hinweis"] = ""
        if "haelt" in payload or "vehicleStopRequested" in payload:
            flag = payload.get("haelt", payload.get("vehicleStopRequested"))
            found["haelt"] = flag not in (False, 0, "0", "false", "nein", "no")
        items = payload.get("umstiege") or payload.get("connections")
        if isinstance(items, list):
            rows = []
            for item in items:
                if not isinstance(item, dict):
                    continue
                rows.append({
                    "linie": str(item.get("linie") or item.get("line") or ""),
                    "richtung": str(item.get("richtung") or item.get("direction") or item.get("destination") or ""),
                    "abfahrt": str(item.get("abfahrt") or item.get("departure") or ""),
                    "verspaetung": str(item.get("verspaetung") or item.get("delay") or ""),
                    "steig": str(item.get("steig") or item.get("platform") or ""),
                })
            found["umstiege"] = rows
        stops = payload.get("stops") or payload.get("halte")
        if isinstance(stops, list) and stops:
            found["halte"] = [str(item) for item in stops]
            found["halt_index"] = 0
        if not found:
            return False
        with self.lock:
            self.data.update(found)
            self.data["empfangen"] = True
        print(f"IBIS-IP {self.data['linie']} {self.data['ziel']}", flush=True)
        return True


def local_tag(tag: str) -> str:
    return tag.split("}")[-1].lower()


def line_label(text: str) -> str:
    value = text.strip()
    if value.upper().startswith("LINE_"):
        value = value[5:]
    if value.upper() == "UNKNOWN" or (value and set(value) <= {"0"}):
        return ""
    return value


def element_text(node: ET.Element) -> str:
    for child in list(node):
        if local_tag(child.tag) == "value" and child.text and child.text.strip():
            return child.text.strip()
    if node.text and node.text.strip():
        return node.text.strip()
    return ""


def read_trip(root: ET.Element) -> dict:
    found: dict = {}
    contents = [node for node in root.iter() if local_tag(node.tag) == "currentdisplaycontent"]
    front = contents[0] if contents else None
    for content in contents:
        ref = next((element_text(child) for child in list(content) if local_tag(child.tag) == "displaycontentref"), "")
        if ref.lower() == "front":
            front = content
            break
    if front is not None:
        line_name = ""
        line_number = ""
        line_ref = ""
        for node in front.iter():
            name = local_tag(node.tag)
            text = element_text(node)
            if name == "linename" and text and not line_name:
                line_name = text
            elif name == "linenumber" and text and not line_number:
                line_number = text
            elif name == "lineref" and text and not line_ref:
                line_ref = text
            if name == "destinationname" and "ziel" not in found:
                if text:
                    found["ziel"] = text
        found["linie"] = line_label(line_name or line_number or line_ref)
    parent_of = {child: node for node in root.iter() for child in list(node)}

    def under(node: ET.Element, tag: str) -> bool:
        current_node = node
        while current_node in parent_of:
            current_node = parent_of[current_node]
            if local_tag(current_node.tag) == tag:
                return True
        return False

    stops = []
    clocks = []
    for node in root.iter():
        if local_tag(node.tag) != "stoppoint":
            continue
        if under(node, "currentstoppoint") and not under(node, "stopsequence"):
            continue
        name = ""
        scheduled = ""
        for child in node.iter():
            tag = local_tag(child.tag)
            text = element_text(child)
            if tag in {"stopname", "stoppointname"} and text and not name:
                name = text
            elif tag == "scheduledarrivaltime" and text and not scheduled:
                scheduled = timetable_label(text)
        if name and name not in stops:
            stops.append(name)
            clocks.append(scheduled)
    has_stop_sequence = any(local_tag(node.tag) == "stopsequence" for node in root.iter())
    if has_stop_sequence:
        if stops and not all(name.casefold() in {"start", "ende"} for name in stops):
            found["halte"] = stops
            found["fahrplan"] = clocks
        else:
            found["halte"] = []
            found["fahrplan"] = []
    for node in root.iter():
        if local_tag(node.tag) == "currentstopindex":
            text = element_text(node)
            if text.isdigit():
                found["halt_index"] = max(0, int(text) - 1)
    current = ""
    for node in root.iter():
        if local_tag(node.tag) == "currentstoppoint":
            for child in node.iter():
                if local_tag(child.tag) == "stopname":
                    current = element_text(child)
                    break
    if current:
        names = found.get("halte", [])
        if names and current in names:
            found["halt_index"] = names.index(current)
        elif not names:
            found["aktueller_halt"] = current
    ticker = None
    for node in root.iter():
        name = local_tag(node.tag)
        if name in {"tickertext", "liveticker", "scrollingtext"}:
            ticker = element_text(node)
        elif name in {"tickerenabled", "onlinetickerenabled"}:
            flag = element_text(node).strip().lower()
            found["ticker_an"] = flag not in {"0", "false", "nein", "no"}
    if ticker is not None and found.get("ticker_an", True) and ticker.strip():
        found["hinweis"] = ticker.strip()
    elif ticker is not None and not ticker.strip():
        found["hinweis"] = ""
    stop_names = {str(name).strip().casefold() for name in found.get("halte", [])}
    if current:
        stop_names.add(current.strip().casefold())
    for node in root.iter():
        if local_tag(node.tag) != "additionalinformation":
            continue
        if under(node, "stoppoint") or under(node, "stopsequence"):
            continue
        text = element_text(node).strip()
        if text and text.casefold() not in stop_names:
            found["hinweis"] = text
            break
    for node in root.iter():
        if local_tag(node.tag) == "vehiclestoprequested":
            flag = element_text(node).strip().lower()
            found["haelt"] = flag in {"true", "1", "yes", "ja"}
            break
    connections = read_connections(root)
    has_connection_list = any(local_tag(node.tag) == "currentconnectiondata" for node in root.iter())
    if has_connection_list or connections:
        found["umstiege"] = connections
    return found


def timetable_label(text: str) -> str:
    value = text.strip()
    if "T" in value and len(value) >= 16 and value[13] == ":":
        return value[11:16]
    for part in value.replace(".", ":").split():
        if ":" not in part:
            continue
        hour, minute = part.split(":", 1)
        if hour.isdigit() and minute[:2].isdigit():
            return f"{int(hour):02d}:{minute[:2]}"
    return ""


def shown_delay(text: str) -> str:
    """Pünktlich (+0) bleibt leer. +1 und -2 bleiben stehen."""
    value = str(text or "").strip()
    token = value.replace(" ", "").casefold()
    for suffix in ("minuten", "min"):
        if token.endswith(suffix):
            token = token[: -len(suffix)]
    if token in {"+0", "0", "+00", "-0", "±0"}:
        return ""
    return value


def departure_label(text: str) -> str:
    value = text.strip()
    if "T" in value and len(value) >= 16:
        return value[11:16]
    if len(value) >= 5 and value[2] == ":":
        return value[:5]
    return value


def read_connections(root: ET.Element) -> list[dict]:
    rows = []
    for node in root.iter():
        if local_tag(node.tag) != "currentconnection":
            continue
        line = direction = expected = scheduled = delay = platform = ""
        for child in node.iter():
            name = local_tag(child.tag)
            text = element_text(child)
            if not text:
                continue
            if name in {"linename", "linenumber"} and not line:
                line = line_label(text)
            elif name == "destinationname" and not direction:
                direction = text
            elif name in {"expecteddeparturetime", "estimateddeparturetime", "departuretime"} and not expected:
                expected = departure_label(text)
            elif name in {"scheduleddeparturetime", "planneddeparturetime"} and not scheduled:
                scheduled = departure_label(text)
            elif name in {"timetabledelay", "delay", "deviation", "verspaetung"} and not delay:
                delay = text.strip()
            elif name in {"platformname", "bayname", "quayname", "steig"} and not platform:
                platform = text
        departure = expected or scheduled
        if any((line, direction, departure, platform, delay)):
            rows.append({
                "linie": line,
                "richtung": direction,
                "abfahrt": departure,
                "verspaetung": delay,
                "steig": platform,
            })
    return rows


def local_ip(remote_host: str) -> str:
    probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        probe.connect((remote_host, 9))
        return probe.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        probe.close()


def subscribe_body(ip: str, port: int, path: str) -> bytes:
    return (
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        "<SubscribeRequest xmlns=\"http://www.vdv.de/ibis/ip\">"
        f"<Client-IP-Address>{ip}</Client-IP-Address>"
        f"<ReplyPort>{port}</ReplyPort>"
        f"<ReplyPath>{path}</ReplyPath>"
        "</SubscribeRequest>"
    ).encode("utf-8")


def app_loop(trip: LiveTrip, app: str, reply_port: int, stop: threading.Event) -> None:
    base = app.rstrip("/")
    host = base.split("://", 1)[-1].split("/", 1)[0].split(":")[0]
    own_ip = local_ip(host)
    operations = (
        "GetCurrentDisplayContent",
        "GetAllData",
        "GetTripData",
        "GetCurrentStopPoint",
        "GetCurrentConnectionInformation",
    )
    subscriptions = (
        "SubscribeCurrentDisplayContent",
        "SubscribeAllData",
        "SubscribeTripData",
        "SubscribeCurrentStopPoint",
        "SubscribeCurrentConnectionInformation",
    )
    next_subscribe = 0.0
    while not stop.is_set():
        now = time.time()
        if now >= next_subscribe:
            for operation in subscriptions:
                path = f"/CustomerInformationService/{operation}"
                request = urllib.request.Request(
                    base + path,
                    data=subscribe_body(own_ip, reply_port, path),
                    headers={"Content-Type": "application/xml; charset=utf-8"},
                    method="POST",
                )
                try:
                    with urllib.request.urlopen(request, timeout=4) as reply:
                        reply.read(400)
                    print(f"Abonniert {operation} an {base}", flush=True)
                except (urllib.error.URLError, TimeoutError, OSError) as exc:
                    print(f"App nicht erreichbar ({operation}): {exc}", flush=True)
            next_subscribe = now + 20
        for operation in operations:
            try:
                with urllib.request.urlopen(f"{base}/CustomerInformationService/{operation}", timeout=4) as reply:
                    trip.apply_xml(reply.read())
            except (urllib.error.URLError, TimeoutError, OSError):
                continue
        stop.wait(2)


def device_information() -> bytes:
    name = socket.gethostname() or "Gorba-TFT"
    xml = f"""<?xml version="1.0" encoding="UTF-8"?>
<DeviceManagementService.GetDeviceInformationResponse>
  <DeviceInformation>
    <DeviceName><Value>Gorba TFT</Value></DeviceName>
    <Manufacturer><Value>Smart-ÖPNV</Value></Manufacturer>
    <SerialNumber><Value>{name}</Value></SerialNumber>
    <DeviceClass>Display</DeviceClass>
  </DeviceInformation>
</DeviceManagementService.GetDeviceInformationResponse>
"""
    return xml.encode("utf-8")


def announce(port: int) -> subprocess.Popen | None:
    try:
        process = subprocess.Popen(
            [
                "avahi-publish-service",
                "GorbaTFT",
                "_ibisip_http._tcp",
                str(port),
                "ver=1.0",
                "svc=GorbaTFT",
                "deviceclass=Display",
                "deviceID=GorbaTFT",
            ],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
    except OSError as exc:
        print(f"DNS-SD nicht gestartet: {exc}", flush=True)
        return None
    print(f"Im IBIS-IP-Netz gemeldet, Port {port}", flush=True)
    atexit.register(process.terminate)
    return process


def local_addresses() -> set[str]:
    found = {"127.0.0.1"}
    try:
        found.add(socket.gethostbyname(socket.gethostname()))
    except OSError:
        pass
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            found.add(info[4][0])
    except OSError:
        pass
    return found


def ethernet_cidr() -> str | None:
    """IPv4 der Ethernetbuchse, so wie eine Anzeige nur diese Buchse hat."""
    try:
        result = subprocess.run(
            ["ip", "-4", "-o", "addr", "show", "dev", "eth0"],
            capture_output=True,
            text=True,
            timeout=2,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    for line in result.stdout.splitlines():
        parts = line.split()
        if "inet" in parts:
            cidr = parts[parts.index("inet") + 1]
            if "/" in cidr:
                return cidr
    return None


def same_ipv4_subnet(address: str, cidr: str) -> bool:
    host, prefix_text = cidr.split("/", 1)
    prefix = int(prefix_text)
    if prefix <= 0 or prefix > 32:
        return False
    mask = (0xFFFFFFFF << (32 - prefix)) & 0xFFFFFFFF

    def pack(text: str) -> int:
        return int.from_bytes(socket.inet_aton(text), "big")

    return (pack(address) & mask) == (pack(host) & mask)


def probe_onboard(url: str, source_ip: str) -> bool:
    target = f"{url}/CustomerInformationService/GetCurrentDisplayContent"
    parsed = urllib.parse.urlparse(target)
    host = parsed.hostname
    if not host:
        return False
    port = parsed.port or 80
    path = parsed.path or "/"
    request = (
        f"GET {path} HTTP/1.1\r\n"
        f"Host: {host}:{port}\r\n"
        "Connection: close\r\n\r\n"
    )
    try:
        with socket.create_connection(
            (host, port), timeout=2, source_address=(source_ip, 0)
        ) as sock:
            sock.settimeout(2)
            sock.sendall(request.encode("ascii"))
            data = sock.recv(120)
        return data.startswith(b"HTTP/1.")
    except OSError:
        return False


def find_onboard(own_port: int) -> str | None:
    cidr = ethernet_cidr()
    if not cidr:
        print("Ethernet ohne Adresse, Suche wartet", flush=True)
        return None
    source_ip = cidr.split("/", 1)[0]
    try:
        result = subprocess.run(
            ["avahi-browse", "-rptk", "_ibisip_http._tcp"],
            capture_output=True,
            text=True,
            timeout=8,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    own = local_addresses()
    best: dict[str, int] = {}
    for line in result.stdout.splitlines():
        if not line.startswith("="):
            continue
        parts = line.split(";")
        if len(parts) < 10:
            continue
        interface, address, port_text, txt = parts[1], parts[7], parts[8], parts[9]
        if interface != "eth0" or ":" in address or address in own:
            continue
        if not same_ipv4_subnet(address, cidr):
            continue
        try:
            port = int(port_text)
        except ValueError:
            continue
        if port == own_port or "svc=GorbaTFT" in txt or "deviceclass=Display" in txt:
            continue
        score = 1
        if "CustomerInformationService" in txt:
            score = 3
        elif "OnBoardUnit" in txt:
            score = 2
        key = f"http://{address}:{port}"
        best[key] = max(best.get(key, 0), score)
    for key in sorted(best, key=best.get, reverse=True):
        if probe_onboard(key, source_ip):
            return key
    return None


def watch_network(trip: LiveTrip, reply_port: int, app: str, stop: threading.Event) -> None:
    publisher = announce(reply_port)
    current = app
    if current:
        threading.Thread(target=app_loop, args=(trip, current, reply_port, threading.Event()), daemon=True).start()
        print(f"App vorgegeben: {current}", flush=True)
    while not stop.is_set():
        if publisher is None or publisher.poll() is not None:
            publisher = announce(reply_port)
        if not current:
            found = find_onboard(reply_port)
            if found:
                current = found
                threading.Thread(
                    target=app_loop, args=(trip, current, reply_port, threading.Event()), daemon=True
                ).start()
                print(f"IBIS-IP-Anlage gefunden: {current}", flush=True)
        stop.wait(5)


def serve_replies(trip: LiveTrip, port: int) -> None:
    class Reply(BaseHTTPRequestHandler):
        def do_POST(self) -> None:
            length = int(self.headers.get("Content-Length", "0") or 0)
            raw = self.rfile.read(length) if length else b""
            content = self.headers.get("Content-Type", "")
            ok = trip.apply_json(raw) if raw.strip().startswith(b"{") or "json" in content else trip.apply_xml(raw)
            body = b"ok" if ok else b"unbekannt"
            self.send_response(200 if ok else 400)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self) -> None:
            path = self.path.lower()
            if "getdeviceinformation" in path:
                body = device_information()
                kind = "application/xml; charset=utf-8"
            else:
                body = b"Gorba-TFT IBIS-IP\n"
                kind = "text/plain; charset=utf-8"
            self.send_response(200)
            self.send_header("Content-Type", kind)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, fmt: str, *args) -> None:
            print(f"TFT {fmt % args}", flush=True)

    try:
        server = ThreadingHTTPServer(("0.0.0.0", port), Reply)
    except OSError as exc:
        print(f"Antwortport {port} belegt: {exc}", flush=True)
        return
    print(f"TFT hört auf Port {port}", flush=True)
    server.serve_forever()


def save_previews(folder: Path) -> tuple[Path, Path]:
    moment = clock_now()
    state = load_state()
    folder.mkdir(parents=True, exist_ok=True)
    next_path = folder / "Gorba-Naechste.png"
    course_path = folder / "Gorba-Verlauf.png"
    paint("next", state, moment).save(next_path, "PNG")
    paint("verlauf", state, moment).save(course_path, "PNG")
    return next_path, course_path


def play_advertisement(width: int, height: int, paint_frame) -> None:
    """Clip in das offene Fenster zeichnen. Die Seite bleibt stehen."""
    if not AD_VIDEO.is_file():
        print(f"Werbung fehlt: {AD_VIDEO}", flush=True)
        return
    width = max(16, int(width))
    height = max(16, int(height))
    frame_size = width * height * 3
    print("Werbung läuft", flush=True)
    proc = subprocess.Popen(
        [
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-re",
            "-i",
            str(AD_VIDEO),
            "-vf",
            (
                f"scale={width}:{height}:force_original_aspect_ratio=decrease,"
                f"pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:black"
            ),
            "-pix_fmt",
            "rgb24",
            "-f",
            "rawvideo",
            "pipe:1",
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
    )
    deadline = time.time() + 45
    try:
        stream = proc.stdout
        if stream is None:
            return
        fd = stream.fileno()
        while time.time() < deadline:
            raw = _read_exact(fd, frame_size, deadline)
            if raw is None:
                break
            paint_frame(raw, width, height)
    except OSError as exc:
        print(f"Werbung nicht abgespielt: {exc}", flush=True)
    finally:
        if proc.stdout is not None:
            proc.stdout.close()
        if proc.poll() is None:
            proc.kill()
        proc.wait(timeout=3)


def _read_exact(fd: int, size: int, deadline: float) -> bytes | None:
    import select

    parts: list[bytes] = []
    got = 0
    while got < size:
        remaining = deadline - time.time()
        if remaining <= 0:
            return None
        ready, _, _ = select.select([fd], [], [], min(remaining, 0.5))
        if not ready:
            continue
        chunk = os.read(fd, size - got)
        if not chunk:
            return None
        parts.append(chunk)
        got += len(chunk)
    return b"".join(parts)


def frame_delay(trip: LiveTrip, phase: dict) -> int:
    view = trip.view()
    if phase["design"] == "monitor" and phase["kind"] in {"verlauf", "umsteigen", "next"} and additional_runs(additional_text(view)):
        return 200
    if phase["design"] == "monitor" and view.get("haelt"):
        return 500
    return 1000


def render_frame(trip: LiveTrip, phase: dict, animate: bool = True) -> Image.Image:
    view = trip.view()
    pages = pages_for(phase["design"], view)
    now = time.time()
    if phase["kind"] not in pages or now >= phase["until"]:
        if phase["kind"] in pages and now >= phase["until"]:
            index = pages.index(phase["kind"])
            phase["kind"] = pages[(index + 1) % len(pages)]
        else:
            phase["kind"] = pages[0]
        phase["until"] = now + NEXT_SECONDS
    if phase["design"] == "monitor":
        return paint_smart(phase["kind"], view, clock_now(), animate)
    return paint(phase["kind"], view, clock_now())


def show(trip: LiveTrip) -> None:
    phase = {
        "design": saved_design(),
        "kind": "next",
        "until": time.time() + NEXT_SECONDS,
        "ad_at": time.time() + AD_SECONDS,
    }
    try:
        from PyQt6.QtCore import Qt, QRect, QTimer
        from PyQt6.QtGui import QColor, QFont, QFontDatabase, QFontMetrics, QImage, QPainter, QPixmap
        from PyQt6.QtWidgets import QApplication, QFrame, QLabel, QVBoxLayout, QWidget
    except ImportError:
        show_tk(trip, phase)
        return

    class Screen(QLabel):
        def __init__(self) -> None:
            super().__init__()
            self.opened_at = 0.0
            self.base = None
            self.scroll_strip = None
            self.footer_top = 0
            self.offset = 0
            self.cycle = 1

        def paintEvent(self, event) -> None:
            if self.base is None:
                super().paintEvent(event)
                return
            painter = QPainter(self)
            painter.fillRect(self.rect(), QColor(0, 0, 0))
            ox = (self.width() - self.base.width()) // 2
            oy = (self.height() - self.base.height()) // 2
            painter.drawPixmap(ox, oy, self.base)
            strip = self.scroll_strip
            if strip is None or strip.width() < 1:
                painter.end()
                return
            top = self.footer_top
            painter.setClipRect(ox, oy + top, self.base.width(), strip.height())
            shift = self.offset % strip.width()
            x = ox + self.base.width() - shift
            y = oy + top
            painter.drawPixmap(x, y, strip)
            painter.drawPixmap(x - strip.width(), y, strip)
            painter.end()

        def mouseDoubleClickEvent(self, event) -> None:
            if time.time() - self.opened_at < 2:
                event.accept()
                return
            print("Doppelklick, Anzeige wird beendet", flush=True)
            application.quit()
            event.accept()

        def keyPressEvent(self, event) -> None:
            if event.key() == Qt.Key.Key_Escape:
                application.quit()
                event.accept()
                return
            super().keyPressEvent(event)

    application = QApplication([])
    screen = application.primaryScreen().size()
    title_px = max(22, min(40, screen.height() // 16))
    name_px = max(18, min(28, screen.height() // 22))
    text_px = max(14, min(20, screen.height() // 30))
    margin = max(16, min(36, screen.width() // 28))
    settings = QWidget()
    settings.setStyleSheet("background:#10141c; color:white;")
    layout = QVBoxLayout(settings)
    layout.setContentsMargins(margin, margin, margin, margin)
    layout.setSpacing(12)
    title = QLabel("Design")
    title.setAlignment(Qt.AlignmentFlag.AlignCenter)
    title.setStyleSheet(f"font-size:{title_px}px; font-weight:700; color:white;")
    layout.addWidget(title)
    last = saved_design()

    class Choice(QFrame):
        def __init__(self, design_id: str, name: str, text: str) -> None:
            super().__init__()
            self.design_id = design_id
            box = QVBoxLayout(self)
            box.setContentsMargins(14, 8, 14, 8)
            box.setSpacing(2)
            heading = QLabel(name)
            heading.setWordWrap(True)
            heading.setStyleSheet(
                f"font-size:{name_px}px; font-weight:700; color:white; background:transparent;"
            )
            detail = QLabel(text)
            detail.setWordWrap(True)
            detail.setStyleSheet(
                f"font-size:{text_px}px; font-weight:500; color:white; background:transparent;"
            )
            box.addWidget(heading)
            box.addWidget(detail)
            self.setStyleSheet("QFrame { background:#1d4e89; border-radius:10px; }")

        def mousePressEvent(self, event) -> None:
            start_design(self.design_id)
            event.accept()

    for design_id, name, text in DESIGNS:
        mark = "  ·  zuletzt" if design_id == last else ""
        layout.addWidget(Choice(design_id, name + mark, text))
    layout.addStretch(1)
    settings.showFullScreen()

    label = Screen()
    label.setAlignment(Qt.AlignmentFlag.AlignCenter)
    label.setStyleSheet("background: black;")
    busy = {"ad": False}
    timer = QTimer()
    timer.setTimerType(Qt.TimerType.PreciseTimer)
    scroll = {"key": None, "hint": None, "on": False}
    scroll_family = ""
    face = font_file(True)
    if face:
        font_id = QFontDatabase.addApplicationFont(face)
        if font_id >= 0:
            names = QFontDatabase.applicationFontFamilies(font_id)
            if names:
                scroll_family = names[0]

    def show_trip() -> None:
        view = trip.view()
        image = render_frame(trip, phase, animate=False)
        hint = additional_text(view)
        scrolling = (
            phase["design"] == "monitor"
            and phase["kind"] in {"verlauf", "umsteigen", "next"}
            and additional_runs(hint)
        )
        rows = tuple(
            (
                str(row.get("linie") or ""),
                str(row.get("richtung") or ""),
                str(row.get("abfahrt") or ""),
                str(row.get("verspaetung") or ""),
                str(row.get("steig") or ""),
            )
            for row in transfer_rows(view)
        )
        stamp = (
            phase["design"],
            phase["kind"],
            hint,
            str(view.get("linie") or ""),
            str(view.get("ziel") or ""),
            tuple(upcoming(view, 6)),
            tuple(view.get("fahrplan") or []),
            rows,
            clock_now().strftime("%H:%M"),
            hold_warning(view),
            label.width(),
            label.height(),
        )
        if scroll["key"] != stamp or label.base is None:
            rgb = image.convert("RGB")
            raw = rgb.tobytes("raw", "RGB")
            picture = QImage(raw, rgb.width, rgb.height, rgb.width * 3, QImage.Format.Format_RGB888).copy()
            label.base = QPixmap.fromImage(picture).scaled(
                label.size(),
                Qt.AspectRatioMode.KeepAspectRatio,
                Qt.TransformationMode.SmoothTransformation,
            )
            scroll["key"] = stamp
        if not scrolling or label.base is None:
            scroll["on"] = False
            label.scroll_strip = None
            label.repaint()
            return
        scale = label.base.width() / WIDTH
        top = int((HEIGHT - FOOTER) * scale)
        height = max(1, label.base.height() - top)
        label.footer_top = top
        if scroll["hint"] != hint or scroll.get("strip_h") != height or label.scroll_strip is None:
            chosen = QFont(scroll_family) if scroll_family else QFont()
            chosen.setBold(True)
            chosen.setPixelSize(max(16, min(int(TICKER_SIZE * scale), int(height * 0.72))))
            marked = f"+++ {hint} +++"
            metrics_probe = QFontMetrics(chosen)
            text_width = max(1, metrics_probe.horizontalAdvance(marked))
            gap = max(1, metrics_probe.horizontalAdvance("+")) * 18
            cycle = text_width + gap
            strip = QPixmap(cycle, height)
            strip.fill(QColor(0, 0, 0, 0))
            painter = QPainter(strip)
            painter.setRenderHint(QPainter.RenderHint.TextAntialiasing, True)
            painter.setPen(QColor(255, 255, 255))
            painter.setFont(chosen)
            painter.drawText(
                QRect(0, 0, text_width, height),
                Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft,
                marked,
            )
            painter.end()
            label.scroll_strip = strip
            label.cycle = cycle
            scroll["hint"] = hint
            scroll["strip_h"] = height
        if not scroll["on"]:
            scroll["on"] = True
            QTimer.singleShot(16, scroll_step)
        label.repaint()

    def scroll_step() -> None:
        if not scroll["on"] or busy["ad"] or label.scroll_strip is None:
            scroll["on"] = False
            return
        label.offset = (label.offset + 1) % max(1, label.scroll_strip.width())
        label.repaint()
        QTimer.singleShot(16, scroll_step)

    def tick() -> None:
        if busy["ad"]:
            return
        if phase["design"] == "new" and AD_VIDEO.is_file() and time.time() >= phase["ad_at"]:
            busy["ad"] = True
            timer.stop()
            started = time.time()

            def paint_frame(raw: bytes, width: int, height: int) -> None:
                picture = QImage(raw, width, height, width * 3, QImage.Format.Format_RGB888).copy()
                label.setPixmap(QPixmap.fromImage(picture))
                application.processEvents()

            play_advertisement(label.width(), label.height(), paint_frame)
            phase["until"] += time.time() - started
            phase["ad_at"] = time.time() + AD_SECONDS
            busy["ad"] = False
            timer.start(1000)
        show_trip()
        delay = frame_delay(trip, phase)
        if timer.interval() != delay:
            timer.setInterval(delay)

    def start_design(design: str) -> None:
        remember_design(design)
        phase["design"] = design
        phase["kind"] = "next"
        phase["until"] = time.time() + NEXT_SECONDS
        phase["ad_at"] = time.time() + AD_SECONDS
        print(f"Design {design}", flush=True)
        settings.hide()
        label.opened_at = time.time()
        label.showFullScreen()
        timer.start(1000)
        tick()

    timer.timeout.connect(tick)
    print("Design wählen. Doppelklick auf der Anzeige beendet das Programm.", flush=True)
    application.exec()


def show_tk(trip: LiveTrip, phase: dict) -> None:
    import tkinter as tk
    from PIL import ImageTk

    window = tk.Tk()
    window.attributes("-fullscreen", True)
    window.configure(bg="#10141c")
    chooser = tk.Frame(window, bg="#10141c")
    chooser.pack(expand=True, fill="both")
    tk.Label(chooser, text="Design", fg="white", bg="#10141c", font=("Arial", 22, "bold")).pack(pady=16)
    last = saved_design()

    def begin(design: str) -> None:
        remember_design(design)
        phase["design"] = design
        phase["kind"] = "next"
        phase["until"] = time.time() + NEXT_SECONDS
        chooser.destroy()
        run()

    for design_id, name, text in DESIGNS:
        mark = "  ·  zuletzt" if design_id == last else ""
        tk.Button(
            chooser,
            text=f"{name}{mark}\n{text}",
            fg="white",
            bg="#1d4e89",
            activebackground="#163a66",
            font=("Arial", 16, "bold"),
            wraplength=640,
            command=lambda chosen=design_id: begin(chosen),
        ).pack(fill="x", padx=80, pady=16, ipady=28)

    label = tk.Label(window, bg="black", borderwidth=0)
    busy = {"ad": False}

    def run() -> None:
        label.pack(expand=True)
        tick()

    def tick() -> None:
        if busy["ad"]:
            return
        if phase["design"] == "new" and AD_VIDEO.is_file() and time.time() >= phase["ad_at"]:
            busy["ad"] = True
            started = time.time()

            def paint_frame(raw: bytes, width: int, height: int) -> None:
                frame = Image.frombytes("RGB", (width, height), raw)
                photo = ImageTk.PhotoImage(frame)
                label.configure(image=photo)
                label.image = photo
                window.update()

            play_advertisement(window.winfo_width(), window.winfo_height(), paint_frame)
            phase["until"] += time.time() - started
            phase["ad_at"] = time.time() + AD_SECONDS
            busy["ad"] = False
        image = render_frame(trip, phase)
        screen_w = max(window.winfo_screenwidth(), 1)
        screen_h = max(window.winfo_screenheight(), 1)
        scale = min(screen_w / WIDTH, screen_h / HEIGHT)
        resized = image.resize((int(WIDTH * scale), int(HEIGHT * scale)), Image.Resampling.LANCZOS)
        photo = ImageTk.PhotoImage(resized)
        label.configure(image=photo)
        label.image = photo
        window.after(frame_delay(trip, phase), tick)

    window.bind("<Double-Button-1>", lambda _event: window.destroy())
    window.bind("<Escape>", lambda _event: window.destroy())
    window.mainloop()


def main() -> None:
    if "--anzeige" in sys.argv:
        app = ""
        reply_port = 9090
        args = sys.argv[1:]
        if "--app" in args:
            app = args[args.index("--app") + 1]
        if "--antwort-port" in args:
            reply_port = int(args[args.index("--antwort-port") + 1])
        trip = LiveTrip()
        threading.Thread(target=serve_replies, args=(trip, reply_port), daemon=True).start()
        threading.Thread(
            target=watch_network, args=(trip, reply_port, app, threading.Event()), daemon=True
        ).start()
        show(trip)
        return
    target = Path.home() / "Desktop"
    if not target.exists():
        target = Path.home()
    next_path, course_path = save_previews(target)
    print(next_path)
    print(course_path)


if __name__ == "__main__":
    main()
