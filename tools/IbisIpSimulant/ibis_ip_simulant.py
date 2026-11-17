#!/usr/bin/env python3
"""IBIS-IP-Gerätesimulant nach VDV 301.

Solange dieser Prozess läuft, antwortet der Rechner wie eine IBIS-IP-Anlage:
HTTP-XML, DNS-SD (_ibisip_http._tcp) und Subscribe-Push. Standardport 8081,
damit das SEV-Programm auf 8080 unberührt bleibt.
"""

from __future__ import annotations

import argparse
import builtins
import functools
import json
import shutil
import subprocess
import threading
import time
import traceback
import urllib.error
import urllib.request
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import parse_qs, urlparse
from xml.etree import ElementTree as ET
from zoneinfo import ZoneInfo

print = functools.partial(builtins.print, flush=True)

NS = "http://www.vdv.de/ibis/ip"
ET.register_namespace("", NS)

SERVICES = (
    ("DeviceManagementService", "2.2"),
    ("CustomerInformationService", "2.3"),
    ("GNSSLocationService", "1.0"),
    ("JourneyInformationService", "1.0"),
    ("TimeService", "1.0"),
)

STATE_PATH = Path.home() / "ibis-ip-simulant.json"
HEARTBEAT_S = 90


def q(tag: str) -> str:
    return f"{{{NS}}}{tag}"


def local_name(tag: str) -> str:
    return tag.split("}")[-1].replace("-", "").replace("_", "").lower()


def now_local() -> datetime:
    try:
        return datetime.now(ZoneInfo("Europe/Berlin"))
    except Exception:
        return datetime.now(timezone(timedelta(hours=1)))


def iso(moment: datetime | None = None) -> str:
    return (moment or now_local()).isoformat(timespec="seconds")


def xml_bytes(root: ET.Element) -> bytes:
    return b"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + ET.tostring(root, encoding="utf-8")


def add(parent: ET.Element, tag: str, text: str | None = None) -> ET.Element:
    node = ET.SubElement(parent, q(tag))
    if text is not None:
        node.text = text
    return node


def text_block(parent: ET.Element, tag: str, value: str, language: str = "de") -> ET.Element:
    node = add(parent, tag)
    add(node, "Value", value)
    add(node, "Language", language)
    return node


def stamp(parent: ET.Element) -> None:
    add(add(parent, "TimeStamp"), "Value", iso())


def response(service: str, operation: str) -> ET.Element:
    return ET.Element(q(f"{service}.{operation}Response"))


def error_response(service: str, operation: str, message: str) -> ET.Element:
    root = response(service, operation)
    add(root, "OperationErrorMessage", message)
    return root


def nmtoken(value: str) -> str:
    cleaned = "".join(ch if ch.isalnum() else "_" for ch in value.strip())
    return cleaned or "Ref"


def find_text(root: ET.Element, *names: str) -> str:
    wanted = {name.replace("-", "").replace("_", "").lower() for name in names}
    for node in root.iter():
        if local_name(node.tag) in wanted and node.text and node.text.strip():
            return node.text.strip()
    return ""


class Plant:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.state = {
            "linie": "704",
            "ziel": "Hauptbahnhof",
            "ziel_kurz": "Hbf",
            "via": "Markt",
            "seite": "",
            "halte": ["Rathaus", "Markt", "Hauptbahnhof"],
            "halt_index": 0,
            "halt_ref": "H-1001",
            "verspaetung_s": 0,
            "fahrt": "704-001",
            "kurs": "001",
            "breite": 51.2562,
            "laenge": 7.1508,
            "tempo_kmh": 0.0,
            "richtung": 90.0,
            "satelliten": 12,
        }
        self.subscribers: dict[tuple[str, str, int, str], float] = {}
        self.started = time.time()
        self._load()

    def _load(self) -> None:
        try:
            saved = json.loads(STATE_PATH.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            return
        if isinstance(saved, dict):
            self.state.update({k: v for k, v in saved.items() if k in self.state})

    def save(self) -> None:
        STATE_PATH.write_text(json.dumps(self.state, ensure_ascii=False, indent=2), encoding="utf-8")

    def snapshot(self) -> dict:
        with self.lock:
            return json.loads(json.dumps(self.state))

    def update(self, changes: dict, advance: bool = False) -> None:
        with self.lock:
            for key, value in changes.items():
                if key in self.state and value is not None:
                    self.state[key] = value
            if advance:
                stops = self.stops_unlocked()
                if stops:
                    self.state["halt_index"] = (int(self.state["halt_index"]) + 1) % len(stops)
            self.save()
        self.push_all()

    def stops_unlocked(self) -> list[str]:
        raw = self.state["halte"]
        if isinstance(raw, str):
            raw = [part.strip() for part in raw.replace(";", "\n").splitlines()]
        return [part.strip() for part in raw if str(part).strip()]

    def current_stop(self, state: dict) -> tuple[int, str]:
        stops = state["halte"] if isinstance(state["halte"], list) else []
        stops = [part for part in stops if str(part).strip()]
        if not stops:
            return 1, "Haltestelle"
        index = int(state["halt_index"]) % len(stops)
        return index + 1, stops[index]

    def subscribe(self, service: str, operation: str, ip: str, port: int, path: str) -> None:
        with self.lock:
            self.subscribers[(service, operation, port, f"{ip}|{path}")] = time.time()
        self.push(service, operation, ip, port, path)

    def unsubscribe(self, service: str, operation: str, ip: str, port: int, path: str) -> None:
        with self.lock:
            self.subscribers.pop((service, operation, port, f"{ip}|{path}"), None)

    def drop_stale(self) -> None:
        limit = time.time() - HEARTBEAT_S
        with self.lock:
            stale = [key for key, seen in self.subscribers.items() if seen < limit]
            for key in stale:
                self.subscribers.pop(key, None)

    def listeners(self, service: str, operation: str) -> list[tuple[str, int, str]]:
        with self.lock:
            found = []
            for (svc, op, port, target), _seen in self.subscribers.items():
                if svc == service and op in {operation, "AllData"}:
                    ip, path = target.split("|", 1)
                    found.append((ip, port, path))
            return found

    def push_all(self) -> None:
        for service, operation in (
            ("CustomerInformationService", "CurrentDisplayContent"),
            ("CustomerInformationService", "CurrentStopPoint"),
            ("CustomerInformationService", "CurrentStopIndex"),
            ("CustomerInformationService", "TripData"),
            ("CustomerInformationService", "VehicleData"),
            ("CustomerInformationService", "AllData"),
            ("GNSSLocationService", "GNSSLocation"),
            ("JourneyInformationService", "AllData"),
        ):
            for ip, port, path in self.listeners(service, operation):
                self.push(service, operation, ip, port, path)

    def push(self, service: str, operation: str, ip: str, port: int, path: str) -> None:
        payload = payload_for(self, service, operation)
        target = path if path.startswith("/") else f"/{path}"
        url = f"http://{ip}:{port}{target}"
        request = urllib.request.Request(
            url,
            data=payload,
            headers={"Content-Type": "application/xml; charset=utf-8"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=2) as reply:
                reply.read(256)
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            print(f"Push {url} nicht zugestellt: {exc}")

    def drift(self) -> None:
        with self.lock:
            speed = float(self.state["tempo_kmh"])
            if speed <= 0:
                return
            heading = float(self.state["richtung"])
            metres = speed * 1000 / 3600 * 2
            north = metres * cos_deg(heading) / 111_320
            east = metres * sin_deg(heading) / (111_320 * max(0.2, cos_deg(float(self.state["breite"]))))
            self.state["breite"] = round(float(self.state["breite"]) + north, 6)
            self.state["laenge"] = round(float(self.state["laenge"]) + east, 6)


def cos_deg(degrees: float) -> float:
    from math import cos, radians
    return cos(radians(degrees))


def sin_deg(degrees: float) -> float:
    from math import radians, sin
    return sin(radians(degrees))


def side_text(state: dict) -> tuple[str, str]:
    side = str(state["seite"]).strip()
    if not side:
        return state["ziel"], state["ziel_kurz"]
    return side, side[:16]


def line_number(line: str) -> str:
    digits = "".join(ch for ch in line if ch.isdigit())
    return digits or "0"


def display_content(parent: ET.Element, state: dict, ref: str, destination: str, short: str) -> None:
    content = add(parent, "CurrentDisplayContent")
    add(content, "DisplayContentRef", ref)
    line = add(content, "LineInformation")
    add(line, "LineRef", nmtoken(state["linie"]))
    text_block(line, "LineName", state["linie"])
    add(line, "LineNumber", line_number(state["linie"]))
    dest = add(content, "Destination")
    add(dest, "DestinationRef", nmtoken(destination))
    text_block(dest, "DestinationName", destination)
    text_block(dest, "DestinationShortName", short or destination[:16])
    for via in str(state["via"]).split(","):
        name = via.strip()
        if not name:
            continue
        point = add(content, "ViaPoint")
        add(point, "ViaPointRef", nmtoken(name))
        text_block(point, "ViaPointName", name)


def cis_display(state: dict) -> ET.Element:
    root = response("CustomerInformationService", "GetCurrentDisplayContent")
    data = add(root, "CurrentDisplayContentData")
    stamp(data)
    display_content(data, state, "Front", state["ziel"], state["ziel_kurz"])
    side_name, side_short = side_text(state)
    display_content(data, state, "Side", side_name, side_short)
    return root


def cis_stop(state: dict) -> ET.Element:
    root = response("CustomerInformationService", "GetCurrentStopPoint")
    data = add(root, "CurrentStopPointData")
    stamp(data)
    index, name = plant_stop(state)
    point = add(data, "CurrentStopPoint")
    add(point, "StopIndex", str(index))
    add(point, "StopRef", nmtoken(f"{state['halt_ref']}-{index}"))
    text_block(point, "StopName", name)
    planned = now_local() + timedelta(seconds=int(state["verspaetung_s"]))
    departure = add(point, "DepartureTime")
    add(departure, "Value", iso(planned))
    return root


def plant_stop(state: dict) -> tuple[int, str]:
    stops = [part for part in state["halte"] if str(part).strip()]
    if not stops:
        return 1, "Haltestelle"
    index = int(state["halt_index"]) % len(stops)
    return index + 1, stops[index]


def cis_stop_index(state: dict) -> ET.Element:
    root = response("CustomerInformationService", "GetCurrentStopIndex")
    data = add(root, "CurrentStopIndexData")
    stamp(data)
    index, _name = plant_stop(state)
    add(data, "CurrentStopIndex", str(index))
    return root


def cis_trip(state: dict) -> ET.Element:
    root = response("CustomerInformationService", "GetTripData")
    data = add(root, "TripData")
    stamp(data)
    add(data, "TripRef", nmtoken(state["fahrt"]))
    add(data, "RunNumber", str(state["kurs"]))
    add(data, "LineRef", nmtoken(state["linie"]))
    text_block(data, "DestinationName", state["ziel"])
    add(data, "Delay", str(int(state["verspaetung_s"])))
    sequence = add(data, "StopSequence")
    for position, name in enumerate(state["halte"], start=1):
        point = add(sequence, "StopPoint")
        add(point, "StopIndex", str(position))
        text_block(point, "StopName", name)
    return root


def cis_vehicle(state: dict) -> ET.Element:
    root = response("CustomerInformationService", "GetVehicleData")
    data = add(root, "VehicleData")
    stamp(data)
    add(data, "VehicleRef", "PI-IBIS-0001")
    add(data, "LineRef", nmtoken(state["linie"]))
    add(data, "RunNumber", str(state["kurs"]))
    add(data, "Delay", str(int(state["verspaetung_s"])))
    return root


def cis_all(state: dict) -> ET.Element:
    root = response("CustomerInformationService", "GetAllData")
    data = add(root, "AllData")
    stamp(data)
    add(data, "TripRef", nmtoken(state["fahrt"]))
    add(data, "RunNumber", str(state["kurs"]))
    display_content(data, state, "Front", state["ziel"], state["ziel_kurz"])
    index, name = plant_stop(state)
    point = add(data, "CurrentStopPoint")
    add(point, "StopIndex", str(index))
    text_block(point, "StopName", name)
    add(data, "Delay", str(int(state["verspaetung_s"])))
    return root


def gnss(state: dict) -> ET.Element:
    root = response("GNSSLocationService", "GetGNSSLocation")
    data = add(root, "GNSSLocationData")
    stamp(data)
    lat = float(state["breite"])
    lon = float(state["laenge"])
    latitude = add(data, "Latitude")
    add(latitude, "Value", f"{abs(lat):.6f}")
    add(latitude, "Hemisphere", "N" if lat >= 0 else "S")
    longitude = add(data, "Longitude")
    add(longitude, "Value", f"{abs(lon):.6f}")
    add(longitude, "Hemisphere", "E" if lon >= 0 else "W")
    add(data, "SpeedOverGround", f"{float(state['tempo_kmh']) / 3.6:.2f}")
    add(data, "Direction", f"{float(state['richtung']):.1f}")
    add(data, "NumberOfSatellites", str(int(state["satelliten"])))
    add(data, "Fix", "3D")
    add(data, "GNSSCoordinateSystem", "WGS84")
    return root


def journey_all(state: dict) -> ET.Element:
    root = response("JourneyInformationService", "GetAllData")
    data = add(root, "JourneyInformationData")
    stamp(data)
    add(data, "BlockRef", nmtoken(state["kurs"]))
    add(data, "TripRef", nmtoken(state["fahrt"]))
    add(data, "RunNumber", str(state["kurs"]))
    add(data, "LineRef", nmtoken(state["linie"]))
    text_block(data, "DestinationName", state["ziel"])
    return root


def journey_displays(state: dict) -> ET.Element:
    root = response("JourneyInformationService", "ListAllDisplayContents")
    data = add(root, "DisplayContentList")
    stamp(data)
    display_content(data, state, "Front", state["ziel"], state["ziel_kurz"])
    side_name, side_short = side_text(state)
    display_content(data, state, "Side", side_name, side_short)
    return root


def device_information(host: str, port: int) -> ET.Element:
    root = response("DeviceManagementService", "GetDeviceInformation")
    data = add(root, "DeviceInformation")
    stamp(data)
    add(data, "DeviceName", "IBIS-IP-Simulant")
    add(data, "Manufacturer", "SmartOepnv")
    add(data, "SerialNumber", "PI-IBIS-0001")
    add(data, "DeviceClass", "OnBoardUnit")
    add(data, "WebInterfaceAddress", f"http://{host}:{port}/")
    return root


def service_information(host: str, port: int) -> ET.Element:
    root = response("DeviceManagementService", "GetServiceInformation")
    data = add(root, "ServiceInformationList")
    stamp(data)
    for name, version in SERVICES:
        item = add(data, "ServiceInformation")
        add(item, "ServiceName", name)
        add(item, "ServiceVersion", version)
        add(item, "IBIS-IP-Version", version)
        add(item, "ServiceURL", f"http://{host}:{port}/{name}/")
    return root


def device_status() -> ET.Element:
    root = response("DeviceManagementService", "GetDeviceStatus")
    data = add(root, "DeviceStatus")
    stamp(data)
    add(data, "DeviceState", "running")
    return root


def service_status() -> ET.Element:
    root = response("DeviceManagementService", "GetServiceStatus")
    data = add(root, "ServiceStatusList")
    stamp(data)
    for name, _version in SERVICES:
        item = add(data, "ServiceStatus")
        add(item, "ServiceName", name)
        add(item, "ServiceState", "running")
    return root


def device_configuration() -> ET.Element:
    root = response("DeviceManagementService", "GetDeviceConfiguration")
    data = add(root, "DeviceConfiguration")
    stamp(data)
    add(data, "DeviceName", "IBIS-IP-Simulant")
    add(data, "HeartbeatInterval", f"PT{HEARTBEAT_S}S")
    return root


def time_service() -> ET.Element:
    root = response("TimeService", "GetTime")
    data = add(root, "TimeData")
    stamp(data)
    add(add(data, "ActualLocalTime"), "Value", iso())
    return root


def subscribe_response(service: str, operation: str, active: bool) -> ET.Element:
    root = response(service, operation)
    add(root, "Active", "true" if active else "false")
    add(root, "HeartbeatInterval", f"PT{HEARTBEAT_S}S")
    return root


OPERATIONS = {
    ("DeviceManagementService", "GetDeviceInformation"): lambda plant, host, port: device_information(host, port),
    ("DeviceManagementService", "GetServiceInformation"): lambda plant, host, port: service_information(host, port),
    ("DeviceManagementService", "GetDeviceStatus"): lambda plant, host, port: device_status(),
    ("DeviceManagementService", "GetServiceStatus"): lambda plant, host, port: service_status(),
    ("DeviceManagementService", "GetDeviceConfiguration"): lambda plant, host, port: device_configuration(),
    ("CustomerInformationService", "GetAllData"): lambda plant, host, port: cis_all(plant.snapshot()),
    ("CustomerInformationService", "GetCurrentDisplayContent"): lambda plant, host, port: cis_display(plant.snapshot()),
    ("CustomerInformationService", "GetCurrentStopPoint"): lambda plant, host, port: cis_stop(plant.snapshot()),
    ("CustomerInformationService", "GetCurrentStopIndex"): lambda plant, host, port: cis_stop_index(plant.snapshot()),
    ("CustomerInformationService", "GetTripData"): lambda plant, host, port: cis_trip(plant.snapshot()),
    ("CustomerInformationService", "GetVehicleData"): lambda plant, host, port: cis_vehicle(plant.snapshot()),
    ("GNSSLocationService", "GetGNSSLocation"): lambda plant, host, port: gnss(plant.snapshot()),
    ("JourneyInformationService", "GetAllData"): lambda plant, host, port: journey_all(plant.snapshot()),
    ("JourneyInformationService", "ListAllDisplayContents"): lambda plant, host, port: journey_displays(plant.snapshot()),
    ("JourneyInformationService", "GetCurrentDisplayContent"): lambda plant, host, port: cis_display(plant.snapshot()),
    ("TimeService", "GetTime"): lambda plant, host, port: time_service(),
}

PUSH_OF = {
    "CurrentDisplayContent": ("CustomerInformationService", "GetCurrentDisplayContent"),
    "CurrentStopPoint": ("CustomerInformationService", "GetCurrentStopPoint"),
    "CurrentStopIndex": ("CustomerInformationService", "GetCurrentStopIndex"),
    "TripData": ("CustomerInformationService", "GetTripData"),
    "VehicleData": ("CustomerInformationService", "GetVehicleData"),
    "AllData": ("CustomerInformationService", "GetAllData"),
    "GNSSLocation": ("GNSSLocationService", "GetGNSSLocation"),
}


def payload_for(plant: Plant, service: str, operation: str) -> bytes:
    if operation == "AllData" and service == "JourneyInformationService":
        return xml_bytes(journey_all(plant.snapshot()))
    if operation == "GNSSLocation":
        return xml_bytes(gnss(plant.snapshot()))
    mapped = PUSH_OF.get(operation)
    if mapped is None:
        return xml_bytes(error_response(service, operation, "Kein Push-Inhalt"))
    service_name, getter = mapped
    if service == "JourneyInformationService":
        return xml_bytes(journey_all(plant.snapshot()))
    return xml_bytes(OPERATIONS[(service_name, getter)](plant, "127.0.0.1", 0))


def parse_body(raw: bytes) -> ET.Element | None:
    if not raw.strip():
        return None
    try:
        return ET.fromstring(raw)
    except ET.ParseError:
        return None


def form_changes(fields: dict[str, str]) -> tuple[dict, bool]:
    changes: dict = {}
    aliases = {
        "linie": "linie",
        "line": "linie",
        "ziel": "ziel",
        "destination": "ziel",
        "ziel_kurz": "ziel_kurz",
        "seite": "seite",
        "via": "via",
        "halt_ref": "halt_ref",
        "fahrt": "fahrt",
        "kurs": "kurs",
    }
    for source, target in aliases.items():
        if source in fields and fields[source] != "":
            changes[target] = fields[source].strip()
    if "halte" in fields:
        changes["halte"] = [part.strip() for part in fields["halte"].replace(";", "\n").splitlines() if part.strip()]
    for source, target, caster in (
        ("halt_index", "halt_index", int),
        ("verspaetung_s", "verspaetung_s", int),
        ("breite", "breite", float),
        ("laenge", "laenge", float),
        ("tempo_kmh", "tempo_kmh", float),
        ("richtung", "richtung", float),
    ):
        if source in fields and fields[source] != "":
            changes[target] = caster(fields[source].replace(",", "."))
    return changes, fields.get("weiter", "") == "1"


class Handler(BaseHTTPRequestHandler):
    plant: Plant
    bind_host: str
    bind_port: int

    def do_GET(self) -> None:
        self.route()

    def do_POST(self) -> None:
        self.route()

    def log_message(self, fmt: str, *args) -> None:
        print(f"{self.address_string()} {fmt % args}")

    def route(self) -> None:
        parsed = urlparse(self.path)
        parts = [part for part in parsed.path.split("/") if part]
        length = int(self.headers.get("Content-Length", "0") or 0)
        raw = self.rfile.read(length) if length else b""
        try:
            if not parts or parts == ["ibis-ip"]:
                self.page()
                return
            if parts == ["simulant", "zustand"]:
                self.state_endpoint(raw)
                return
            if len(parts) != 2:
                self.send_xml(404, error_response("IBIS-IP", "Unknown", "Pfad nicht bekannt"))
                return
            service, operation = parts
            self.operation(service, operation, raw)
        except Exception:
            traceback.print_exc()
            self.send_xml(500, error_response("IBIS-IP", "Error", "Interner Fehler"))

    def operation(self, service: str, operation: str, raw: bytes) -> None:
        known = {name for name, _version in SERVICES}
        if service not in known:
            self.send_xml(404, error_response(service, operation, "Dienst nicht vorhanden"))
            return
        if operation.startswith("Subscribe"):
            self.handle_subscribe(service, operation, raw, active=True)
            return
        if operation.startswith("Unsubscribe"):
            self.handle_subscribe(service, operation, raw, active=False)
            return
        builder = OPERATIONS.get((service, operation))
        if builder is None:
            self.send_xml(404, error_response(service, operation, "Operation nicht vorhanden"))
            return
        host = self.headers.get("Host", f"{self.bind_host}:{self.bind_port}").split(":")[0]
        self.send_xml(200, builder(self.plant, host, self.bind_port))

    def handle_subscribe(self, service: str, operation: str, raw: bytes, active: bool) -> None:
        data_name = operation.removeprefix("Subscribe").removeprefix("Unsubscribe")
        body = parse_body(raw)
        ip = self.client_address[0]
        port = 0
        path = f"/{service}/{operation}"
        if body is not None:
            ip = find_text(body, "Client-IP-Address", "ClientIPAddress", "IPAddress") or ip
            port_text = find_text(body, "ReplyPort", "Port")
            path = find_text(body, "ReplyPath", "Path") or path
            port = int(port_text) if port_text.isdigit() else 0
        if active and port <= 0:
            self.send_xml(400, error_response(service, operation, "ReplyPort fehlt"))
            return
        if active:
            self.plant.subscribe(service, data_name, ip, port, path)
        else:
            self.plant.unsubscribe(service, data_name, ip, port, path)
        self.send_xml(200, subscribe_response(service, operation, active))

    def state_endpoint(self, raw: bytes) -> None:
        if self.command == "GET":
            payload = json.dumps(self.plant.snapshot(), ensure_ascii=False).encode("utf-8")
            self.send_bytes(200, "application/json; charset=utf-8", payload)
            return
        content_type = self.headers.get("Content-Type", "")
        fields: dict[str, str] = {}
        if "json" in content_type:
            loaded = json.loads(raw.decode("utf-8") or "{}")
            fields = {str(key): "" if value is None else str(value) for key, value in loaded.items()}
            if "halte" in loaded and isinstance(loaded["halte"], list):
                fields["halte"] = "\n".join(str(item) for item in loaded["halte"])
        else:
            parsed = parse_qs(raw.decode("utf-8"), keep_blank_values=True)
            fields = {key: values[0] for key, values in parsed.items()}
        changes, advance = form_changes(fields)
        self.plant.update(changes, advance)
        if "json" in content_type:
            payload = json.dumps(self.plant.snapshot(), ensure_ascii=False).encode("utf-8")
            self.send_bytes(200, "application/json; charset=utf-8", payload)
            return
        self.send_response(303)
        self.send_header("Location", "/")
        self.end_headers()

    def page(self) -> None:
        state = self.plant.snapshot()
        index, stop = plant_stop(state)
        halte = "\n".join(state["halte"])
        with self.plant.lock:
            clients = len(self.plant.subscribers)
        host = self.headers.get("Host", f"127.0.0.1:{self.bind_port}")
        body = f"""<!doctype html>
<meta charset="utf-8">
<title>IBIS-IP-Simulant</title>
<style>
body {{ font-family: Segoe UI, sans-serif; margin: 32px; color: #17202a; background: #f4f7f8; }}
main {{ max-width: 820px; }}
h1 {{ font-size: 28px; margin-bottom: 4px; }}
label {{ display: block; margin-top: 12px; font-weight: 600; }}
input, textarea {{ width: 100%; box-sizing: border-box; padding: 8px; font: inherit; }}
button {{ margin: 16px 8px 0 0; padding: 10px 16px; font: inherit; }}
code {{ background: #e7eef2; padding: 1px 4px; }}
</style>
<main>
<h1>IBIS-IP-Simulant</h1>
<p>Dieser Pi antwortet jetzt als IBIS-IP-Anlage. Anzeigen rufen <code>http://{host}/CustomerInformationService/GetCurrentDisplayContent</code> auf.</p>
<p>Aktueller Halt {index}: <strong>{stop}</strong> · Linie {state["linie"]} · Ziel {state["ziel"]} · Abonnenten {clients}</p>
<form method="post" action="/simulant/zustand">
<label>Linie</label><input name="linie" value="{escape(state["linie"])}">
<label>Ziel</label><input name="ziel" value="{escape(state["ziel"])}">
<label>Ziel kurz</label><input name="ziel_kurz" value="{escape(state["ziel_kurz"])}">
<label>Seite, leer = Ziel</label><input name="seite" value="{escape(state["seite"])}">
<label>Via, mit Komma</label><input name="via" value="{escape(state["via"])}">
<label>Halte, eine Zeile je Halt</label><textarea name="halte" rows="6">{escape(halte)}</textarea>
<label>Halt-Index, beginnend bei 0</label><input name="halt_index" value="{state["halt_index"]}">
<label>Halt-Referenz</label><input name="halt_ref" value="{escape(state["halt_ref"])}">
<label>Verspätung in Sekunden</label><input name="verspaetung_s" value="{state["verspaetung_s"]}">
<label>Fahrt</label><input name="fahrt" value="{escape(state["fahrt"])}">
<label>Kurs</label><input name="kurs" value="{escape(state["kurs"])}">
<label>Breite</label><input name="breite" value="{state["breite"]}">
<label>Länge</label><input name="laenge" value="{state["laenge"]}">
<label>Tempo km/h</label><input name="tempo_kmh" value="{state["tempo_kmh"]}">
<label>Richtung Grad</label><input name="richtung" value="{state["richtung"]}">
<button type="submit">Übernehmen</button>
<button type="submit" name="weiter" value="1">Nächster Halt</button>
</form>
</main>"""
        self.send_bytes(200, "text/html; charset=utf-8", body.encode("utf-8"))

    def send_xml(self, status: int, root: ET.Element) -> None:
        self.send_bytes(status, "application/xml; charset=utf-8", xml_bytes(root))

    def send_bytes(self, status: int, content_type: str, payload: bytes) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


def escape(value: str) -> str:
    return (
        str(value)
        .replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def advertise(port: int) -> list[subprocess.Popen]:
    tool = shutil.which("avahi-publish-service") or shutil.which("avahi-publish")
    if not tool:
        print("DNS-SD nicht verfügbar. HTTP bleibt erreichbar, mDNS-Suche nicht.")
        return []
    processes = []
    for name, version in SERVICES:
        command = [
            tool,
            f"IBIS-IP-{name}",
            "_ibisip_http._tcp",
            str(port),
            f"ver={version}",
            f"path=/{name}/",
            "txtvers=1",
        ]
        processes.append(subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
    print(f"DNS-SD: {len(processes)} Dienste als _ibisip_http._tcp auf Port {port}")
    return processes


def gnss_loop(plant: Plant, stop: threading.Event) -> None:
    while not stop.wait(2):
        plant.drop_stale()
        plant.drift()
        for ip, port, path in plant.listeners("GNSSLocationService", "GNSSLocation"):
            plant.push("GNSSLocationService", "GNSSLocation", ip, port, path)


def main() -> None:
    parser = argparse.ArgumentParser(description="IBIS-IP-Anlage auf diesem Rechner simulieren")
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8081)
    args = parser.parse_args()
    plant = Plant()
    Handler.plant = plant
    Handler.bind_host = args.host
    Handler.bind_port = args.port
    server = ThreadingHTTPServer((args.host, args.port), Handler)
    publications = advertise(args.port)
    stop = threading.Event()
    thread = threading.Thread(target=gnss_loop, args=(plant, stop), daemon=True)
    thread.start()
    print(f"IBIS-IP-Simulant aktiv auf Port {args.port}. Statusseite: http://127.0.0.1:{args.port}/")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("Simulant wird beendet.")
    finally:
        stop.set()
        server.server_close()
        for process in publications:
            process.terminate()


if __name__ == "__main__":
    main()
