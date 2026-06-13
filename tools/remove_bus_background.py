from collections import deque

from PIL import Image

source = r"tools\planer_sync_solaris_bus_source.png"
target = r"src\SmartOepnv.AppShared\Assets\planer_sync_solaris_bus.png"
img = Image.open(source).convert("RGBA")
width, height = img.size
pixels = img.load()


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
    # Gelb, Schwarz, Rot, Grün, Fenster-Blau
    if r > 170 and g > 120 and b < 120:
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


# 1) Weißen Außenhintergrund von den Rändern entfernen.
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

# 2) Bodenlinie entfernen.
for y in range(height - 1, max(height - 12, -1), -1):
    dark = sum(
        1
        for x in range(width)
        if pixels[x, y][3] > 0 and max(pixels[x, y][:3]) < 45
    )
    if dark > width * 0.25:
        for x in range(width):
            r, g, b, a = pixels[x, y]
            if a > 0 and max(r, g, b) < 45:
                pixels[x, y] = (r, g, b, 0)

# 3) Weiße Reste unterhalb der Karosserie (zwischen Rädern) entfernen.
wheel_zone_y = int(height * 0.56)
for y in range(wheel_zone_y, height):
    for x in range(width):
        r, g, b, a = pixels[x, y]
        if not is_stray_white(r, g, b, a):
            continue

        touches_bus_color = False
        for dx in range(-2, 3):
            for dy in range(-2, 3):
                nx, ny = x + dx, y + dy
                if nx < 0 or ny < 0 or nx >= width or ny >= height:
                    continue
                if is_bus_color(*pixels[nx, ny]):
                    touches_bus_color = True
                    break
            if touches_bus_color:
                break

        if not touches_bus_color:
            pixels[x, y] = (r, g, b, 0)
            continue

        # Zwischen Rädern: hell, aber nur von Weiß/Grau/Radfarben umgeben.
        light_neighbors = 0
        bus_neighbors = 0
        for dx, dy in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            nx, ny = x + dx, y + dy
            if nx < 0 or ny < 0 or nx >= width or ny >= height:
                continue
            nr, ng, nb, na = pixels[nx, ny]
            if na == 0:
                light_neighbors += 1
            elif is_stray_white(nr, ng, nb, na) or max(nr, ng, nb) < 130:
                light_neighbors += 1
            elif is_bus_color(nr, ng, nb, na):
                bus_neighbors += 1

        if light_neighbors >= 2 and bus_neighbors <= 1:
            pixels[x, y] = (r, g, b, 0)

# 4) Verbleibendes Weiß/Grau im Radbereich ohne direkten Gelb-Kontakt entfernen.
wheel_zone_y = int(height * 0.52)
for y in range(wheel_zone_y, height):
    for x in range(width):
        r, g, b, a = pixels[x, y]
        if a == 0 or min(r, g, b) < 192:
            continue
        if max(r, g, b) - min(r, g, b) > 35:
            continue

        has_yellow_neighbor = False
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                nx, ny = x + dx, y + dy
                if nx < 0 or ny < 0 or nx >= width or ny >= height:
                    continue
                nr, ng, nb, na = pixels[nx, ny]
                if na > 0 and nr > 150 and ng > 120 and nb < 130:
                    has_yellow_neighbor = True
                    break
            if has_yellow_neighbor:
                break

        if not has_yellow_neighbor:
            pixels[x, y] = (r, g, b, 0)

# 5) Zuschneiden.
bbox = img.getbbox()
if not bbox:
    raise SystemExit("No visible pixels left after background removal.")

img = img.crop(bbox)
img.save(target, "PNG")

transparent = sum(1 for px in img.getdata() if px[3] == 0)
total = img.size[0] * img.size[1]
print(f"Saved {target}: {img.size[0]}x{img.size[1]}, transparent {transparent}/{total} ({100 * transparent / total:.1f}%)")
