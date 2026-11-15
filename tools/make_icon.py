"""Genera src/app.ico: icona in pixel art (griglia 16x16) ingrandita senza sfumature.

Uso:  python tools/make_icon.py [anteprima.png]
Solo libreria standard. Per cambiare il disegno modificare ART e PALETTE.
"""
import os
import struct
import sys
import zlib

# Ogni carattere è un pixel. '.' = trasparente.
ART = [
    "..LLLLLLLLLLLL..",
    ".LNNNNNNNNNNPNL.",
    "LNNNNNNNNNNPPPNL",
    "NNNNNNNNNNNNPNNN",
    "NNKKKKKKKKKKKKNN",
    "NKWWWWWWWWWWWWKN",
    "NKWWDWWWWWWWYWKN",
    "NKWDDDWSWSWUWRKN",
    "NKWWDWWWWWWWGWKN",
    "NKHHHHKKKKHHHHKN",
    "NKHHHKNNNNKHHHKN",
    "NNKKKNNNNNNKKKNN",
    "NNNPNNNNNNNNNNNN",
    "MNNNNNNNNNNNNNNM",
    ".MNNNNNNNNNNNNM.",
    "..MMMMMMMMMMMM..",
]

PALETTE = {
    ".": (0, 0, 0, 0),
    "N": (0x3B, 0x4C, 0xC0, 255),   # sfondo indaco
    "L": (0x6C, 0x7F, 0xF0, 255),   # bordo chiaro in alto
    "M": (0x23, 0x2A, 0x70, 255),   # bordo scuro in basso
    "P": (0xC9, 0xD6, 0xFF, 255),   # stelline
    "K": (0x0B, 0x0F, 0x1A, 255),   # contorno
    "W": (0xF1, 0xF3, 0xFA, 255),   # corpo del controller
    "H": (0xAA, 0xB3, 0xCF, 255),   # ombra del corpo
    "D": (0x1B, 0x24, 0x44, 255),   # croce direzionale
    "S": (0x6B, 0x76, 0x99, 0xFF),  # start / select
    "Y": (0xFF, 0xD2, 0x3F, 255),
    "U": (0x4D, 0xA3, 0xFF, 255),
    "R": (0xF0, 0x4F, 0x5F, 255),
    "G": (0x4A, 0xDE, 0x80, 255),
}

SIZES = [16, 32, 48, 64, 128, 256]      # multipli di 16: ingrandimento netto


def pixels(size):
    """Righe di tuple RGBA, dall'alto verso il basso."""
    k = size // 16
    rows = []
    for line in ART:
        row = []
        for ch in line:
            row.extend([PALETTE[ch]] * k)
        rows.extend([row] * k)
    return rows


def png(rows):
    size = len(rows)
    raw = b"".join(b"\x00" + bytes(c for px in row for c in px) for row in rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def dib(rows):
    """Bitmap a 32 bit per .ico: intestazione, pixel BGRA dal basso, maschera AND vuota."""
    size = len(rows)
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    xor = b"".join(bytes((b, g, r, a)) for row in reversed(rows) for (r, g, b, a) in row)
    mask_row = ((size + 31) // 32) * 4
    return header + xor + b"\x00" * (mask_row * size)


def main():
    for line in ART:
        assert len(line) == 16, line
    assert len(ART) == 16

    images = []
    for s in SIZES:
        rows = pixels(s)
        images.append((s, png(rows) if s >= 256 else dib(rows)))

    out = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    for s, data in images:
        out += struct.pack("<BBBBHHII", s % 256, s % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in images:
        out += data

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ico = os.path.join(root, "src", "app.ico")
    with open(ico, "wb") as f:
        f.write(out)
    print("OK ->", ico, len(out), "byte")

    if len(sys.argv) > 1:
        with open(sys.argv[1], "wb") as f:
            f.write(png(pixels(256)))


if __name__ == "__main__":
    main()
