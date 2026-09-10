"""Convert an equirectangular panorama into six cubemap face textures."""
from pathlib import Path
import sys

import numpy as np
from PIL import Image


def sample_bilinear(image, x, y):
    h, w = image.shape[:2]
    x %= w
    y = np.clip(y, 0, h - 1)
    x0 = np.floor(x).astype(np.int32)
    y0 = np.floor(y).astype(np.int32)
    x1 = (x0 + 1) % w
    y1 = np.minimum(y0 + 1, h - 1)
    dx = (x - x0)[..., None]
    dy = (y - y0)[..., None]
    return ((image[y0, x0] * (1 - dx) + image[y0, x1] * dx) * (1 - dy)
            + (image[y1, x0] * (1 - dx) + image[y1, x1] * dx) * dy)


def main(source_path, output_dir, size=1024):
    source = np.asarray(Image.open(source_path).convert("RGB"), dtype=np.float32)
    output = Path(output_dir)
    output.mkdir(parents=True, exist_ok=True)
    coords = (np.arange(size, dtype=np.float32) + 0.5) / size * 2 - 1
    u, v = np.meshgrid(coords, -coords)
    # DirectX-style face vectors: Front (+Z), Back (-Z), Right (+X), Left (-X), Up (+Y), Down (-Y).
    faces = {
        "Skybox_Front_PosZ.png": (u, v, np.ones_like(u)),
        "Skybox_Back_NegZ.png": (-u, v, -np.ones_like(u)),
        "Skybox_Right_PosX.png": (np.ones_like(u), v, -u),
        "Skybox_Left_NegX.png": (-np.ones_like(u), v, u),
        "Skybox_Up_PosY.png": (u, np.ones_like(u), -v),
        "Skybox_Down_NegY.png": (u, -np.ones_like(u), v),
    }
    h, w = source.shape[:2]
    for filename, (x, y, z) in faces.items():
        length = np.sqrt(x * x + y * y + z * z)
        x, y, z = x / length, y / length, z / length
        longitude = np.arctan2(x, z)
        latitude = np.arcsin(np.clip(y, -1, 1))
        sx = (longitude / (2 * np.pi) + 0.5) * w - 0.5
        sy = (0.5 - latitude / np.pi) * h - 0.5
        face = np.clip(sample_bilinear(source, sx, sy), 0, 255).astype(np.uint8)
        Image.fromarray(face, "RGB").save(output / filename, optimize=True)
    (output / "README.txt").write_text(
        "Cubemap faces generated from the original stylized snowfield panorama.\n"
        "Face convention: +X Right, -X Left, +Y Up, -Y Down, +Z Front, -Z Back.\n"
        f"Resolution: {size} x {size} per face.\n", encoding="utf-8")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 1024)
