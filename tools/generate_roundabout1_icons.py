import math
from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 300
BG = (179, 215, 242)
BORDER = (13, 71, 161)
ROAD = (255, 255, 255)
ROUTE = (13, 71, 161)


def draw_roundabout(path: Path, arm_count: int, exit_num: int) -> None:
    img = Image.new("RGB", (SIZE, SIZE), BG)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([4, 4, SIZE - 4, SIZE - 4], radius=18, outline=BORDER, width=6, fill=BG)
    cx = cy = 150
    outer_r = 108
    inner_r = 64
    road_half = 20
    road_top = 22
    road_bottom = 278
    road_left = 22
    road_right = 278

    d.rectangle([0, cy - road_half, road_left, cy + road_half], fill=ROAD)
    d.rectangle([road_right, cy - road_half, SIZE, cy + road_half], fill=ROAD)
    d.rectangle([cx - road_half, 0, cx + road_half, road_top], fill=ROAD)
    d.rectangle([cx - road_half, road_bottom, cx + road_half, SIZE], fill=ROAD)
    d.ellipse([cx - outer_r, cy - outer_r, cx + outer_r, cy + outer_r], fill=ROAD, outline=BORDER, width=4)
    d.ellipse([cx - inner_r, cy - inner_r, cx + inner_r, cy + inner_r], fill=BG, outline=BORDER, width=4)

    step = 360.0 / arm_count
    angle = (360.0 - (exit_num - 1) * step) % 360.0
    start_bottom_y = 286
    enter_y = 230
    start_angle = 90.0
    sweep = -360.0 if abs(angle - start_angle) < 0.001 else -((start_angle - angle + 360.0) % 360.0)

    pts = [(cx, start_bottom_y), (cx, enter_y)]
    arc_r = 84
    steps = max(8, int(abs(sweep) / 8))
    for i in range(steps + 1):
        a = math.radians(start_angle + sweep * i / steps)
        pts.append((cx + math.cos(a) * arc_r, cy + math.sin(a) * arc_r))
    rad = math.radians(angle)
    ex = cx + math.cos(rad) * 138
    ey = cy + math.sin(rad) * 138
    pts.append((ex, ey))
    d.line(pts, fill=ROUTE, width=24, joint="curve")

    tip_x = cx + math.cos(rad) * 154
    tip_y = cy + math.sin(rad) * 154
    left = math.radians(angle + 150)
    right = math.radians(angle - 150)
    arrow = [
        (tip_x, tip_y),
        (tip_x + math.cos(left) * 28, tip_y + math.sin(left) * 28),
        (tip_x + math.cos(right) * 28, tip_y + math.sin(right) * 28),
    ]
    d.polygon(arrow, fill=ROUTE)
    path.parent.mkdir(parents=True, exist_ok=True)
    img.save(path)


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    targets = [
        root / "src/SmartOepnv.AppShared/Assets/navi_grafiken",
        root.parent / "GPSAnsagen/app/src/main/assets/navi_grafiken",
    ]
    icons = [
        ("Navi Kreisverkehr 1.Ausfahrt.png", 4, 1),
        ("Navi Kreisverkehr 1.Ausfahrt bei 5.png", 5, 1),
    ]
    for folder in targets:
        for name, arms, exit_n in icons:
            out = folder / name
            draw_roundabout(out, arms, exit_n)
            print(f"wrote {out}")


if __name__ == "__main__":
    main()
