import math
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image


def load_skn(path):
    data = Path(path).read_bytes()
    if struct.unpack_from("<I", data, 0)[0] != 0x00112233:
        raise ValueError("Unsupported SKN")
    major, minor, meshes = struct.unpack_from("<HHI", data, 4)
    if (major, minor) != (2, 1):
        raise ValueError("Only SKN 2.1 is supported")
    cursor = 12 + meshes * 80
    index_count, vertex_count = struct.unpack_from("<II", data, cursor)
    cursor += 8
    indices = np.frombuffer(data, dtype="<u2", count=index_count, offset=cursor).reshape(-1, 3)
    cursor += index_count * 2
    raw = np.frombuffer(data, dtype=np.uint8, count=vertex_count * 52, offset=cursor).reshape(-1, 52)
    positions = raw[:, :12].copy().view("<f4").reshape(-1, 3)
    normals = raw[:, 32:44].copy().view("<f4").reshape(-1, 3)
    uv = raw[:, 44:52].copy().view("<f4").reshape(-1, 2)
    return positions, normals, uv, indices


def rotation(yaw, pitch):
    y, p = math.radians(yaw), math.radians(pitch)
    ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    rx = np.array([[1, 0, 0], [0, math.cos(p), -math.sin(p)], [0, math.sin(p), math.cos(p)]])
    return rx @ ry


def render(positions, normals, uv, indices, texture, yaw, pitch, size=640):
    matrix = rotation(yaw, pitch)
    points, norms = positions @ matrix.T, normals @ matrix.T
    xy = points[:, [0, 1]]
    low, high = xy.min(0), xy.max(0)
    scale = (size * 0.82) / max(high - low)
    screen = (xy - (low + high) / 2) * scale + size / 2
    screen[:, 1] = size - screen[:, 1]
    z = points[:, 2]
    canvas = np.zeros((size, size, 4), dtype=np.uint8)
    depth = np.full((size, size), -np.inf, dtype=np.float32)
    tex = np.asarray(texture.convert("RGB"))
    th, tw = tex.shape[:2]
    light = np.array([-0.25, 0.55, 0.8])
    light /= np.linalg.norm(light)
    for tri in indices[np.argsort(z[indices].mean(1))]:
        pts = screen[tri]
        x0, y0 = np.floor(pts.min(0)).astype(int)
        x1, y1 = np.ceil(pts.max(0)).astype(int)
        x0, y0, x1, y1 = max(0, x0), max(0, y0), min(size - 1, x1), min(size - 1, y1)
        if x1 < x0 or y1 < y0:
            continue
        den = ((pts[1, 1] - pts[2, 1]) * (pts[0, 0] - pts[2, 0]) +
               (pts[2, 0] - pts[1, 0]) * (pts[0, 1] - pts[2, 1]))
        if abs(den) < 1e-5:
            continue
        yy, xx = np.mgrid[y0:y1 + 1, x0:x1 + 1]
        a = ((pts[1, 1] - pts[2, 1]) * (xx - pts[2, 0]) + (pts[2, 0] - pts[1, 0]) * (yy - pts[2, 1])) / den
        b = ((pts[2, 1] - pts[0, 1]) * (xx - pts[2, 0]) + (pts[0, 0] - pts[2, 0]) * (yy - pts[2, 1])) / den
        c = 1 - a - b
        inside = (a >= -0.001) & (b >= -0.001) & (c >= -0.001)
        zz = a * z[tri[0]] + b * z[tri[1]] + c * z[tri[2]]
        visible = inside & (zz > depth[y0:y1 + 1, x0:x1 + 1])
        if not visible.any():
            continue
        tuv = a[..., None] * uv[tri[0]] + b[..., None] * uv[tri[1]] + c[..., None] * uv[tri[2]]
        tx = np.clip((tuv[..., 0] % 1 * (tw - 1)).astype(int), 0, tw - 1)
        ty = np.clip((tuv[..., 1] % 1 * (th - 1)).astype(int), 0, th - 1)
        normal = norms[tri].mean(0)
        shade = np.clip(0.45 + 0.65 * abs(float(normal @ light)), 0.45, 1.0)
        colors = (tex[ty, tx] * shade).astype(np.uint8)
        region = canvas[y0:y1 + 1, x0:x1 + 1]
        region[visible, :3] = colors[visible]
        region[visible, 3] = 255
        depth[y0:y1 + 1, x0:x1 + 1][visible] = zz[visible]
    return Image.fromarray(canvas)


def main():
    if len(sys.argv) != 5:
        raise SystemExit("usage: render_legacy_skn.py MODEL.skn TEXTURE.dds OUTPUT.png NAME")
    model, texture_path, output, name = sys.argv[1:]
    positions, normals, uv, indices = load_skn(model)
    texture = Image.open(texture_path)
    views = [render(positions, normals, uv, indices, texture, angle, -8, 520) for angle in (-35, 20, 105)]
    sheet = Image.new("RGB", (1640, 610), (9, 20, 40))
    for index, view in enumerate(views):
        sheet.paste(view, (20 + index * 540, 70), view)
    from PIL import ImageDraw
    draw = ImageDraw.Draw(sheet)
    draw.text((24, 20), name, fill=(244, 227, 168), stroke_width=1, stroke_fill=(0, 0, 0))
    sheet.save(output)
    print(output)


if __name__ == "__main__":
    main()
