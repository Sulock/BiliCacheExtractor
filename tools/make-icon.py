"""Render our editable SVG and package Windows ICO sizes. Requires Pillow + NumPy.

The SVG deliberately uses only rects, polygons, and two-stop linear gradients.
Small Windows icons omit the shadow and thin tray using data-min-size.
"""
from io import BytesIO
from pathlib import Path
import struct
import xml.etree.ElementTree as ET

import numpy as np
from PIL import Image, ImageColor, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
SVG = ET.parse(ASSETS / "app-icon.svg").getroot()
NS = "{http://www.w3.org/2000/svg}"
SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)


def render(size):
    scale = max(4, 1024 // size)
    side = size * scale
    ratio = side / 512
    canvas = Image.new("RGBA", (side, side))
    gradients = {node.attrib["id"]: node for node in SVG.findall(f"{NS}defs/{NS}linearGradient")}
    for node in SVG:
        kind = node.tag.removeprefix(NS)
        if kind in ("title", "desc", "defs") or size < int(node.get("data-min-size", "0")):
            continue
        mask = Image.new("L", canvas.size)
        draw = ImageDraw.Draw(mask)
        if kind == "rect":
            x, y, w, h, radius = (float(node.get(k, "0")) * ratio for k in ("x", "y", "width", "height", "rx"))
            draw.rounded_rectangle((x, y, x + w, y + h), radius, fill=255)
        elif kind == "polygon":
            points = [tuple(float(v) * ratio for v in point.split(",")) for point in node.attrib["points"].split()]
            draw.polygon(points, fill=255)
        else:
            raise ValueError(f"Unsupported SVG element: {kind}")
        fill = node.attrib["fill"]
        if fill.startswith("url(#"):
            gradient = gradients[fill[5:-1]]
            x1, y1, x2, y2 = (float(gradient.attrib[k]) for k in ("x1", "y1", "x2", "y2"))
            yy, xx = np.mgrid[0:side, 0:side] / ratio
            t = np.clip(((xx - x1) * (x2 - x1) + (yy - y1) * (y2 - y1)) / ((x2 - x1) ** 2 + (y2 - y1) ** 2), 0, 1)
            colors = [np.array(ImageColor.getrgb(stop.attrib["stop-color"])) for stop in gradient]
            pixels = np.uint8(colors[0] * (1 - t[..., None]) + colors[1] * t[..., None])
            layer = Image.fromarray(pixels, "RGB").convert("RGBA")
        else:
            layer = Image.new("RGBA", canvas.size, fill)
        if "opacity" in node.attrib:
            opacity = float(node.attrib["opacity"])
            mask = mask.point(lambda value: round(value * opacity))
        layer.putalpha(mask)
        canvas = Image.alpha_composite(canvas, layer)
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def dib(image):
    """32-bit bottom-up DIB + transparency mask for small Windows icon frames."""
    size = image.width
    pixels = image.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes("raw", "BGRA")
    stride = ((size + 31) // 32) * 4
    mask = bytearray(stride * size)
    alpha = image.getchannel("A")
    for y in range(size):
        for x in range(size):
            if alpha.getpixel((x, size - y - 1)) == 0:
                mask[y * stride + x // 8] |= 1 << (7 - x % 8)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, len(pixels), 0, 0, 0, 0)
    return header + pixels + mask


def write_ico(images):
    frames = []
    for size, image in images.items():
        if size >= 128:
            stream = BytesIO()
            image.save(stream, format="PNG")
            frames.append((size, stream.getvalue()))
        else:
            frames.append((size, dib(image)))
    offset = 6 + 16 * len(frames)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(frames)))
    payload = bytearray()
    for size, frame in frames:
        directory += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(frame), offset)
        payload += frame
        offset += len(frame)
    (ASSETS / "app-icon.ico").write_bytes(directory + payload)


if __name__ == "__main__":
    images = {size: render(size) for size in SIZES}
    write_ico(images)
    render(512).save(ASSETS / "app-icon.png")
    preview = Image.new("RGB", (880, 390), "#F4F7FB")
    preview.paste(images[256], (35, 55), images[256])
    draw = ImageDraw.Draw(preview)
    font = ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf", 15)
    draw.text((39, 329), "BiliCacheExtractor", font=font, fill="#34445F")
    for y, bg, ink in ((32, "#FFFFFF", "#34445F"), (211, "#18243A", "#CFDAE9")):
        draw.rounded_rectangle((330, y, 852, y + 147), radius=16, fill=bg)
        for x, size in zip((365, 447, 529, 620, 725), (16, 24, 32, 48, 64)):
            picture = images[size]
            preview.paste(picture, (x, y + 35 + (64 - size) // 2), picture)
            draw.text((x, y + 113), f"{size}px", font=font, fill=ink)
    preview.save(ASSETS / "icon-preview.png")
    print("Generated SVG-derived PNG and ICO: " + ", ".join(map(str, SIZES)))
