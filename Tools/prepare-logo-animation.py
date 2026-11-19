"""Export the approved GIF's composited frames for WPF; build-time tool only."""
import json
from pathlib import Path
from PIL import Image

folder = Path(__file__).resolve().parents[1] / "Assets" / "LogoAnimation"
manifest = {"Frames": []}
with Image.open(folder / "cat-reading.gif") as animation:
    assert animation.n_frames > 1
    for index in range(animation.n_frames):
        animation.seek(index)
        # Pillow applies GIF offsets and disposal before conversion. Every PNG
        # is a complete transparent canvas, so WPF never leaves old eye pixels.
        frame = animation.convert("RGBA")
        assert frame.size == (320, 320)
        assert frame.getpixel((0, 0))[3] == 0
        name = f"frame-{index + 1:02d}.png"
        frame.save(folder / name)
        manifest["Frames"].append({"FileName": name, "Milliseconds": animation.info["duration"]})
assert len(manifest["Frames"]) == 6
assert sum(frame["Milliseconds"] for frame in manifest["Frames"]) == 2210
(folder / "frames.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print("Prepared 6 transparent frames and their original GIF timings (2210 ms).")
