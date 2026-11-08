"""Regenerate the embedded grayscale strikes using DejaVu Sans 2.37, Pillow and fontTools."""
import gzip
import hashlib
from io import BytesIO
from pathlib import Path
import struct
import sys

from fontTools.ttLib import TTFont
from PIL import ImageFont


def main():
    source = Path(sys.argv[1])
    if hashlib.sha256(source.read_bytes()).hexdigest() != "ae7b7855e115a5966d8b1b3f80f254ccc117ec86f9965e202ee2940453837280":
        raise ValueError("Expected the DejaVu Sans 2.37 regular face")
    target = Path(__file__).resolve().parent
    face = TTFont(source)
    (target / "LICENSE.txt").write_text(face["name"].getDebugName(13).strip() + "\n", encoding="utf-8")
    codes = sorted(code for code in face.getBestCmap() if
                   32 <= code <= 126 or 160 <= code <= 0x24f or 0x370 <= code <= 0x52f
                   or 0x2000 <= code <= 0x203f or code == 0xfffd)
    strikes = []
    for size in range(8, 21):
        font = ImageFont.truetype(str(source), size, layout_engine=ImageFont.Layout.BASIC)
        strike = BytesIO()
        strike.write(struct.pack("<BBH", *font.getmetrics(), len(codes)))
        for code in codes:
            mask, (left, top) = font.getmask2(chr(code), mode="L", anchor="ls")
            width, height = mask.size
            strike.write(struct.pack("<HbbBBB", code, left, top, width, height, round(font.getlength(chr(code)))))
            strike.write(bytes(mask))
        strikes.append(gzip.compress(strike.getvalue(), mtime=0))
    # Size table allows loading just the active strike, even on the Mono runtime.
    (target / "coverage.bin").write_bytes(struct.pack("<13I", *(len(s) for s in strikes)) + b"".join(strikes))


if __name__ == "__main__":
    main()
