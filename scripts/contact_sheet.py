"""자동 플레이 스크린샷을 한 장으로 모은다. 사용: python scripts/contact_sheet.py <스크린샷 폴더> <출력.png> [열 수]"""
import sys
from pathlib import Path
from PIL import Image

src, out = Path(sys.argv[1]), Path(sys.argv[2])
cols = int(sys.argv[3]) if len(sys.argv) > 3 else 4
shots = sorted(p for p in src.glob("[0-9][0-9]-*.png"))
thumb_w = 480
thumbs = []
for p in shots:
    im = Image.open(p).convert("RGB")
    thumbs.append(im.resize((thumb_w, round(im.height * thumb_w / im.width)), Image.LANCZOS))
h = max(t.height for t in thumbs)
rows = (len(thumbs) + cols - 1) // cols
gap = 8
sheet = Image.new("RGB", (cols * thumb_w + (cols + 1) * gap, rows * h + (rows + 1) * gap), (27, 30, 43))
for i, t in enumerate(thumbs):
    sheet.paste(t, (gap + (i % cols) * (thumb_w + gap), gap + (i // cols) * (h + gap)))
sheet.save(out)
print(f"{len(thumbs)}장 → {out}")
