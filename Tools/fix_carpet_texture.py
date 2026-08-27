from pathlib import Path
from PIL import Image, ImageDraw

TEXTURE = Path(r"D:\project\CodexOutput\Passivity\Carpet\T_Carpet_Green_Paw_BaseColor.png")
BACKUP = TEXTURE.with_name(TEXTURE.stem + "_before_uv_fix.png")
LEFT_PANEL = (73, 91, 52, 255)
BEIGE = (201, 176, 140, 255)


def main() -> None:
    # Restart from the untouched original. The right half and all UVs remain unchanged.
    image = Image.open(BACKUP).convert("RGBA")
    clear_box = (96, 207, 201, 341)

    # Remove only the original beige paw pixels from the left panel.
    for y in range(clear_box[1], clear_box[3]):
        for x in range(clear_box[0], clear_box[2]):
            if image.getpixel((x, y)) == BEIGE:
                image.putpixel((x, y), LEFT_PANEL)

    # Build a recognisably canine paw: four separated oval toes and a broad,
    # softly triangular central pad (not a plain bear-like ellipse).
    mask = Image.new("L", (105, 134), 0)
    draw = ImageDraw.Draw(mask)
    draw.ellipse((12, 27, 32, 53), fill=255)
    draw.ellipse((34, 15, 54, 45), fill=255)
    draw.ellipse((57, 15, 77, 45), fill=255)
    draw.ellipse((79, 27, 99, 53), fill=255)
    draw.polygon(
        [(30, 82), (34, 68), (44, 58), (52, 55), (60, 58),
         (70, 68), (75, 82), (72, 99), (65, 106), (40, 106),
         (33, 99)],
        fill=255,
    )
    # Round the pad shoulders and lower corners while keeping the dog-pad silhouette.
    draw.ellipse((33, 60, 72, 96), fill=255)
    draw.rectangle((39, 77, 66, 103), fill=255)
    draw.ellipse((34, 89, 52, 107), fill=255)
    draw.ellipse((53, 89, 71, 107), fill=255)

    paw = Image.new("RGBA", mask.size, BEIGE)
    paw.putalpha(mask)
    paw = paw.rotate(90, expand=True, resample=Image.Resampling.NEAREST)

    # Rotate around the original centre at exactly 1:1 scale.
    centre_x = (clear_box[0] + clear_box[2]) // 2
    centre_y = (clear_box[1] + clear_box[3]) // 2
    position = (centre_x - paw.width // 2, centre_y - paw.height // 2)
    image.alpha_composite(paw, position)
    image.save(TEXTURE)

    print(f"updated={TEXTURE}")
    print("changed=left paw only; rotation=90_ccw; scale=1.0")
    print("unchanged=UVs, right texture half, left dark-green border")


if __name__ == "__main__":
    main()
