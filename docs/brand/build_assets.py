"""
Regenerate every brand raster from the SVG sources checked into the tree (REBRANDING.md §3).

Sources (editable — replace these when you rebrand, keep the filenames):
    src/Shared.Ui/wwwroot/brand/icon_light.svg          the mark for light grounds
    src/Shared.Ui/wwwroot/brand/icon_dark.svg           (optional) the mark for dark grounds; falls back to icon_light
    src/Shared.Ui/wwwroot/brand/lockup_light.svg, lockup_dark.svg
Outputs:
    the *_1024 / *_1520 PNGs next to those sources; src/Web/wwwroot/{favicon.ico, favicon.png,
    apple_touch_180.png, icon-192.png, icon-512.png, icon-maskable-512.png, og_image_1200x630.png};
    src/Infrastructure/Email/Assets/logo.png; and the store/marketing set in docs/brand/.

Renders with a headless Chromium (Edge or Chrome) so lockup wordmarks carry the real webfonts —
webfonts do NOT load inside an <img>-embedded SVG, which is why the UI references the PNG lockups.
Pillow writes the .ico and flattens the opaque outputs. Needs network for Google Fonts.

    python docs/brand/build_assets.py
"""
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

from PIL import Image

# ── brand config: the only block to edit ────────────────────────────────────────────────────────
ICON_GROUND = "#6b8a72"       # background behind the mark on launcher / store / PWA icons
EMAIL_GROUND = "#FFFFFF"      # email logo must be a flat PNG (clients strip SVG + transparency)
OG_GROUND = "#F5F7F4"         # og_image + LinkedIn banner background; the lockup sits centred on it
OG_LOCKUP = "lockup_light"    # which lockup to draw on OG_GROUND: lockup_light or lockup_dark
FONTS = "https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;700&display=swap"
# ────────────────────────────────────────────────────────────────────────────────────────────────

ROOT = Path(__file__).resolve().parents[2]
SHARED = ROOT / "src/Shared.Ui/wwwroot/brand"
WEB = ROOT / "src/Web/wwwroot"
EMAIL = ROOT / "src/Infrastructure/Email/Assets"
DOCS_BRAND = ROOT / "docs/brand"

BROWSERS = [os.environ.get("BRAND_RENDERER", ""),
            r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            r"C:\Program Files\Google\Chrome\Application\chrome.exe",
            "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome", "/usr/bin/chromium", "/usr/bin/google-chrome"]
BROWSER = next((b for b in BROWSERS if b and Path(b).exists()), None)
WORK_DIRS = []


def svg_text(name):
    p = SHARED / f"{name}.svg"
    if name == "icon_dark" and not p.exists():
        p = SHARED / "icon_light.svg"
    return p.read_text(encoding="utf-8")


def sized(svg, w, h):
    return svg.replace("<svg ", f'<svg width="{w}" height="{h}" ', 1)


def page(inner, w, h, bg="transparent"):
    return (f'<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="{FONTS}">'
            f'<style>html,body{{margin:0;padding:0;background:{bg};width:{w}px;height:{h}px;overflow:hidden}}'
            f'svg{{display:block}}</style></head><body>{inner}</body></html>')


def icon_on(w, h, ground, scale, icon="icon_dark", rx=0):
    """The icon SVG centred on a w×h ground; `scale` = icon size as a fraction of the shorter side."""
    s = int(min(w, h) * scale)
    bg = f"background:{ground};" if ground else ""
    return (f'<div style="width:{w}px;height:{h}px;{bg}border-radius:{rx}px;display:grid;place-items:center">'
            f'{sized(svg_text(icon), s, s)}</div>')


def render(html, out, w, h):
    """The browser launcher returns before it has loaded the page or written the file: keep the page
    and profile alive and poll for the output."""
    out = Path(out); out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        out.unlink()
    td = Path(tempfile.mkdtemp(prefix="brand-")); WORK_DIRS.append(td)
    src = td / "in.html"; src.write_text(html, encoding="utf-8")
    cmd = [BROWSER, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run", "--disable-extensions",
           "--force-device-scale-factor=1", "--default-background-color=00000000",
           f"--user-data-dir={td / 'profile'}", f"--window-size={w},{h}", "--virtual-time-budget=10000",
           f"--screenshot={out}", src.as_uri()]
    subprocess.run(cmd, check=True, capture_output=True, timeout=120)
    deadline = time.time() + 90; last = -1
    while time.time() < deadline:
        if out.exists():
            size = out.stat().st_size
            if size > 0 and size == last:
                break
            last = size
        time.sleep(0.5)
    else:
        raise RuntimeError(f"timed out waiting for the browser to write {out}")
    im = Image.open(out)
    assert im.size == (w, h), f"{out.name}: got {im.size}, wanted {(w, h)}"
    label = out.relative_to(ROOT) if str(out).startswith(str(ROOT)) else out.name
    print(f"  {label}  {w}x{h}")


def flatten(path):
    Image.open(path).convert("RGB").save(path)


def main():
    if not BROWSER:
        sys.exit("No headless Chromium found; set BRAND_RENDERER to a Chrome/Edge executable.")
    print(f"Rendering with {BROWSER}")

    render(page(sized(svg_text("icon_light"), 1024, 1024), 1024, 1024), SHARED / "icon_light_1024.png", 1024, 1024)
    for name in ("lockup_light", "lockup_dark"):
        render(page(sized(svg_text(name), 1520, 392), 1520, 392), SHARED / f"{name}_1520.png", 1520, 392)

    render(page(sized(svg_text("icon_light"), 32, 32), 32, 32), WEB / "favicon.png", 32, 32)
    render(page(icon_on(180, 180, ICON_GROUND, 0.72), 180, 180), WEB / "apple_touch_180.png", 180, 180)
    render(page(icon_on(192, 192, ICON_GROUND, 0.72), 192, 192), WEB / "icon-192.png", 192, 192)
    render(page(icon_on(512, 512, ICON_GROUND, 0.72), 512, 512), WEB / "icon-512.png", 512, 512)
    render(page(icon_on(512, 512, ICON_GROUND, 0.58), 512, 512), WEB / "icon-maskable-512.png", 512, 512)  # safe zone
    og = (f'<div style="width:1200px;height:630px;background:{OG_GROUND};display:grid;place-items:center">'
          + sized(svg_text(OG_LOCKUP), 930, 240) + "</div>")
    render(page(og, 1200, 630, bg=OG_GROUND), WEB / "og_image_1200x630.png", 1200, 630)
    for p in ("apple_touch_180.png", "icon-192.png", "icon-512.png", "icon-maskable-512.png", "og_image_1200x630.png"):
        flatten(WEB / p)

    big = Path(tempfile.mkdtemp(prefix="brand-")) / "fav256.png"; WORK_DIRS.append(big.parent)
    render(page(sized(svg_text("icon_light"), 256, 256), 256, 256), big, 256, 256)
    Image.open(big).save(WEB / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)])
    print("  src/Web/wwwroot/favicon.ico  16/32/48")

    render(page(icon_on(256, 256, EMAIL_GROUND, 0.78, icon="icon_light"), 256, 256, bg=EMAIL_GROUND), EMAIL / "logo.png", 256, 256)
    flatten(EMAIL / "logo.png")

    # Store / marketing set — NEW_APP_GUIDE.md Phase 9
    render(page(icon_on(1024, 1024, ICON_GROUND, 0.72), 1024, 1024), DOCS_BRAND / "app_store_icon_1024.png", 1024, 1024)
    render(page(icon_on(512, 512, ICON_GROUND, 0.72), 512, 512), DOCS_BRAND / "play_store_icon_512.png", 512, 512)
    render(page(icon_on(432, 432, None, 0.61), 432, 432), DOCS_BRAND / "android_adaptive_foreground_432.png", 432, 432)
    render(page(icon_on(300, 300, ICON_GROUND, 0.72, rx=48), 300, 300), DOCS_BRAND / "linkedin_logo_300.png", 300, 300)
    banner = (f'<div style="width:1128px;height:191px;background:{OG_GROUND};display:grid;place-items:center">'
              + sized(svg_text(OG_LOCKUP), 620, 160) + "</div>")
    render(page(banner, 1128, 191, bg=OG_GROUND), DOCS_BRAND / "linkedin_banner_1128x191.png", 1128, 191)
    for p in ("app_store_icon_1024.png", "play_store_icon_512.png", "linkedin_banner_1128x191.png"):
        flatten(DOCS_BRAND / p)

    time.sleep(2)
    for d in WORK_DIRS:
        shutil.rmtree(d, ignore_errors=True)
    print("Done.")


if __name__ == "__main__":
    main()
