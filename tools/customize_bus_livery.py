"""PostBus-Busgrafik für Smart-ÖPNV Ladeanimation anpassen."""

from __future__ import annotations

from collections import deque
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "tools" / "planer_sync_solaris_bus_source.png"
LOGO = ROOT.parent / "SmartOepnv.Planer" / "src" / "SmartOepnv.Planer" / "Assets" / "app.png"
TARGET = ROOT / "src" / "SmartOepnv.AppShared" / "Assets" / "planer_sync_solaris_bus.png"

BRAND_BLUE = (1, 90, 232)
BRAND_BLUE_DARK = (0, 68, 180)


def is_edge_background(r: int, g: int, b: int, a: int) -> bool:
    if a == 0:
        return True
    if r >= 245 and g >= 245 and b >= 245:
        return True
    return r + g + b >= 735


def is_stray_white(r: int, g: int, b: int, a: int) -> bool:
    if a == 0:
        return False
    if min(r, g, b) >= 200 and max(r, g, b) - min(r, g, b) <= 28:
        return True
    if min(r, g, b) >= 185 and max(r, g, b) - min(r, g, b) <= 12:
        return True
    return False


def is_bus_color(r: int, g: int, b: int, a: int) -> bool:
    if a == 0:
        return False
    if r > 150 and g > 120 and b < 130:
        return True
    if r + g + b < 120:
        return True
    if r > 150 and g < 90 and b < 90:
        return True
    if g > 110 and r < 120 and b < 120:
        return True
    if b > 100 and r < 170 and g > 90:
        return True
    return False


def is_yellow(r: int, g: int, b: int, a: int) -> bool:
    if a == 0:
        return False
    if r > 165 and g > 105 and b < 150 and g >= b - 20 and r >= g - 55:
        return True
    if r > 140 and g > 90 and b < 100 and r > b + 40:
        return True
    return False


def is_window_white(r: int, g: int, b: int, a: int) -> bool:
    if a == 0:
        return False
    if min(r, g, b) >= 175 and max(r, g, b) - min(r, g, b) <= 40:
        return True
    if min(r, g, b) >= 160 and max(r, g, b) <= 255 and max(r, g, b) - min(r, g, b) <= 22:
        return True
    return False


def recolor_yellow(r: int, g: int, b: int) -> tuple[int, int, int]:
    lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
    base = BRAND_BLUE_DARK if lum < 0.55 else BRAND_BLUE
    factor = 0.55 + lum * 0.55
    return (
        min(255, int(base[0] * factor + 20 * (1 - factor))),
        min(255, int(base[1] * factor + 40 * (1 - factor))),
        min(255, int(base[2] * factor + 30 * (1 - factor))),
    )


def remove_outer_background(img: Image.Image) -> Image.Image:
    width, height = img.size
    pixels = img.load()
    visited = [[False] * width for _ in range(height)]
    queue: deque[tuple[int, int]] = deque()

    for x in range(width):
        for y in (0, height - 1):
            if is_edge_background(*pixels[x, y]):
                visited[y][x] = True
                queue.append((x, y))

    for y in range(height):
        for x in (0, width - 1):
            if not visited[y][x] and is_edge_background(*pixels[x, y]):
                visited[y][x] = True
                queue.append((x, y))

    while queue:
        x, y = queue.popleft()
        pixels[x, y] = (pixels[x, y][0], pixels[x, y][1], pixels[x, y][2], 0)
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            nx, ny = x + dx, y + dy
            if nx < 0 or ny < 0 or nx >= width or ny >= height or visited[ny][nx]:
                continue
            if is_edge_background(*pixels[nx, ny]):
                visited[ny][nx] = True
                queue.append((nx, ny))

    for y in range(height - 1, max(height - 12, -1), -1):
        dark = sum(
            1 for x in range(width) if pixels[x, y][3] > 0 and max(pixels[x, y][:3]) < 45
        )
        if dark > width * 0.25:
            for x in range(width):
                r, g, b, a = pixels[x, y]
                if a > 0 and max(r, g, b) < 45:
                    pixels[x, y] = (r, g, b, 0)

    wheel_zone_y = int(height * 0.52)
    for y in range(wheel_zone_y, height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            if not is_stray_white(r, g, b, a):
                continue
            has_yellow_neighbor = any(
                is_yellow(*pixels[x + dx, y + dy])
                for dx in (-1, 0, 1)
                for dy in (-1, 0, 1)
                if 0 <= x + dx < width and 0 <= y + dy < height
            )
            if not has_yellow_neighbor:
                pixels[x, y] = (r, g, b, 0)

    return img


def recolor_all_yellow(img: Image.Image) -> None:
    pixels = img.load()
    width, height = img.size
    for y in range(height):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            if a == 0 or not is_yellow(r, g, b, a):
                continue
            pixels[x, y] = (*recolor_yellow(r, g, b), a)


def clean_window_whites(img: Image.Image) -> None:
    width, height = img.size
    pixels = img.load()
    y0 = int(height * 0.12)
    y1 = int(height * 0.82)

    for _ in range(3):
        for y in range(y0, y1):
            for x in range(width):
                r, g, b, a = pixels[x, y]
                if a == 0 or not is_window_white(r, g, b, a):
                    continue

                near_glass = False
                for dx in range(-3, 4):
                    for dy in range(-3, 4):
                        nx, ny = x + dx, y + dy
                        if nx < 0 or ny < 0 or nx >= width or ny >= height:
                            continue
                        nr, ng, nb, na = pixels[nx, ny]
                        if na == 0:
                            near_glass = True
                            break
                        if max(nr, ng, nb) < 95 and na > 0:
                            near_glass = True
                            break
                        if nb > nr + 15 and nb > 70 and na > 0:
                            near_glass = True
                            break
                    if near_glass:
                        break

                if near_glass:
                    pixels[x, y] = (r, g, b, 0)


def prepare_logo(max_width: int, max_height: int) -> Image.Image:
    logo = Image.open(LOGO).convert("RGBA")
    logo_px = logo.load()
    lw, lh = logo.size
    for y in range(lh):
        for x in range(lw):
            r, g, b, a = logo_px[x, y]
            if b > 160 and r < 100 and g < 150 and a > 0:
                logo_px[x, y] = (BRAND_BLUE[0], BRAND_BLUE[1], BRAND_BLUE[2], 255)

    ratio = min(max_width / lw, max_height / lh)
    size = (max(1, int(lw * ratio)), max(1, int(lh * ratio)))
    return logo.resize(size, Image.Resampling.LANCZOS)


def fill_rect_blue(img: Image.Image, x0: int, y0: int, x1: int, y1: int) -> None:
    pixels = img.load()
    width, height = img.size
    x0 = max(0, x0)
    y0 = max(0, y0)
    x1 = min(width, x1)
    y1 = min(height, y1)
    for y in range(y0, y1):
        for x in range(x0, x1):
            _, _, _, a = pixels[x, y]
            if a > 0:
                pixels[x, y] = (*BRAND_BLUE, a)


def paste_logo(img: Image.Image, logo: Image.Image, center_x: int, center_y: int) -> None:
    x = center_x - logo.width // 2
    y = center_y - logo.height // 2
    img.paste(logo, (x, y), logo)


def final_light_cleanup(img: Image.Image) -> None:
    """Helle Restpixel in Fenster-/Türzone entfernen (nicht Dach)."""
    width, height = img.size
    pixels = img.load()
    roof_y = int(height * 0.08)
    body_y = int(height * 0.78)

    for y in range(roof_y, body_y):
        for x in range(width):
            r, g, b, a = pixels[x, y]
            if a == 0:
                continue
            if min(r, g, b) >= 160 and max(r, g, b) - min(r, g, b) <= 45:
                pixels[x, y] = (r, g, b, 0)


def replace_post_logos(img: Image.Image) -> None:
    width, height = img.size

    # Posthorn vorne (rechts)
    fill_rect_blue(img, int(width * 0.70), int(height * 0.30), int(width * 0.94), int(height * 0.90))
    horn_logo = prepare_logo(int(width * 0.22), int(height * 0.50))
    paste_logo(img, horn_logo, int(width * 0.82), int(height * 0.58))

    # +P hinten (Mitte) – etwas größerer Bereich
    fill_rect_blue(img, int(width * 0.36), int(height * 0.34), int(width * 0.57), int(height * 0.90))
    rear_logo = prepare_logo(int(width * 0.15), int(height * 0.40))
    paste_logo(img, rear_logo, int(width * 0.465), int(height * 0.58))


def main() -> None:
    img = Image.open(SOURCE).convert("RGBA")
    img = remove_outer_background(img)
    recolor_all_yellow(img)
    clean_window_whites(img)

    bbox = img.getbbox()
    if not bbox:
        raise SystemExit("Bus-Grafik leer nach Freistellung.")
    img = img.crop(bbox)

    replace_post_logos(img)
    recolor_all_yellow(img)
    clean_window_whites(img)
    final_light_cleanup(img)

    img.save(TARGET, "PNG")
    print(f"Saved {TARGET}: {img.size[0]}x{img.size[1]}")


if __name__ == "__main__":
    main()
