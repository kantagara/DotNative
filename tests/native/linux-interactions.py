"""System-input GTK test. Run on a dedicated X11 desktop with the fixture open.
Requires python3-pyatspi, python3-pil, xdotool and ImageMagick's import command.
AT-SPI locates widgets; only XTest mouse/keyboard events activate them.
"""
import argparse
import json
import pathlib
import subprocess
import time

import pyatspi
from gi.repository import GLib
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
(args.output / "PASS.txt").unlink(missing_ok=True)
evidence = []
window_origin = (0, 0)


def pump():
    context = GLib.MainContext.default()
    while context.pending():
        context.iteration(False)


def await_check(reason, check, timeout=8):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        pump()
        value = check()
        if value:
            return value
        time.sleep(.03)
    raise AssertionError(reason)


def descendants(node):
    yield node
    for child in node:
        yield from descendants(child)


def find(name):
    return next((n for n in descendants(pyatspi.Registry.getDesktop(0)) if n.name == name), None)


def label(name):
    return await_check(f"Missing {name}", lambda: find(name))


def control(name):
    node = label(name)
    return node.parent if node.getRoleName() == "label" else node


def role(*names, excluding=None):
    # GTK's fallback accessible names ("GtkEntry", etc.) vary by GTK version.
    # This fixture contains exactly one of each semantic control role.
    def locate():
        matches = [n for n in descendants(pyatspi.Registry.getDesktop(0))
                   if n.getRoleName() in names and n != excluding]
        assert len(matches) <= 1, f"Ambiguous accessible role {names}: {len(matches)}"
        return matches[0] if matches else None
    return await_check(f"Missing accessible role {names}", locate)


def rect(node):
    # GTK 4.14 deliberately returns (0, 0) for screen coordinates.
    # Window-relative AT-SPI bounds plus X11 geometry work on both GTK versions.
    r = node.queryComponent().getExtents(pyatspi.WINDOW_COORDS)
    return r.x + window_origin[0], r.y + window_origin[1], r.width, r.height


def xdo(*args):
    subprocess.run(["xdotool", *map(str, args)], check=True, capture_output=True, timeout=5)


def move(node):
    x, y, w, h = rect(node)
    xdo("mousemove", x + w // 2, y + h // 2)


def outside():
    xdo("mousemove", 1100, 900)


def tap(node):
    move(node)
    xdo("mousedown", 1)
    time.sleep(.08)
    xdo("mouseup", 1)


def pixel(node, base, opacity, phase):
    # A test-colored border remains visible over native control chrome.
    surface = node.parent
    x, y, w, h = rect(surface)
    expected = tuple(round(c * opacity + 255 * (1 - opacity)) for c in base)
    path = args.output / f"{phase}.png"
    observed = None

    def check():
        nonlocal observed
        subprocess.run(["import", "-window", "root", str(path)], check=True, capture_output=True, timeout=5)
        with Image.open(path) as image:
            observed = image.convert("RGB").getpixel((x + w - 1, y + h // 2))
        return all(abs(a - b) <= 5 for a, b in zip(observed, expected))

    try:
        await_check(f"{phase}: expected {expected}, observed screenshot at {path}", check)
    finally:
        evidence.append({"phase": phase, "expected": expected, "observed": observed, "screenshot": path.name, "control_rect": rect(node), "surface_rect": (x, y, w, h)})


try:
    label("Test radio")
    def preview_window():
        # Query the UTF-8 title explicitly; legacy WM_NAME searches can miss it.
        ids = subprocess.check_output(["xdotool", "search", "--name", ""], text=True, timeout=5).splitlines()
        for xid in ids:
            title = subprocess.check_output(["xdotool", "getwindowname", xid], text=True, timeout=5)
            if title.startswith("DotNative Preview"):
                return xid
        return None

    window = await_check("Preview X11 window", preview_window)
    geometry = subprocess.check_output(["xdotool", "getwindowgeometry", "--shell", window], text=True, timeout=5)
    geometry = dict(line.split("=", 1) for line in geometry.splitlines() if "=" in line)
    window_origin = (int(geometry["X"]), int(geometry["Y"]))
    xdo("windowfocus", "--sync", window)
    radio, button, reset = control("Test radio"), control("Test button"), control("Reset")
    field, toggle, slider = role("text"), role("check box", "toggle button", "switch", excluding=radio), role("slider")
    outside(); pixel(radio, (255, 0, 0), 1, "radio-base")
    move(radio); pixel(radio, (255, 0, 0), .8, "radio-hover")
    xdo("mousedown", 1); pixel(radio, (255, 0, 0), .4, "radio-held"); label("Selections: 0")
    xdo("mouseup", 1); label("Selections: 1"); pixel(radio, (255, 0, 0), .65, "radio-selected")
    tap(reset); outside(); pixel(radio, (255, 0, 0), 1, "radio-reset")
    move(button); xdo("mousedown", 1); pixel(button, (37, 99, 235), .4, "button-held")
    outside(); xdo("mouseup", 1); label("Clicks: 0"); pixel(button, (37, 99, 235), 1, "button-cancelled")
    tap(button); label("Clicks: 1")
    tap(reset); tap(button); tap(button); label("Clicks: 2")
    tap(control("Disable soon")); move(radio); xdo("mousedown", 1)
    pixel(radio, (255, 0, 0), .4, "radio-disable-held")
    pixel(radio, (255, 0, 0), .2, "radio-disabled")
    xdo("mouseup", 1); label("Selections: 0")
    assert not radio.getState().contains(pyatspi.STATE_SENSITIVE)
    tap(reset); outside(); pixel(radio, (255, 0, 0), 1, "radio-enabled")
    for name, node, base, alpha in (("radio", radio, (255,0,0), .8), ("button", button, (37,99,235), .8),
                                   ("field", field, (0,128,0), .75), ("switch", toggle, (0,128,0), .7),
                                   ("slider", slider, (0,128,0), .7)):
        move(node); pixel(node, base, alpha, name + "-hover-enter")
        outside(); pixel(node, base, 1, name + "-hover-exit")
    tap(field); outside(); pixel(field, (0,128,0), .55, "field-focus")
    xdo("key", "Tab"); pixel(toggle, (0,128,0), .55, "switch-focus"); pixel(field, (0,128,0), 1, "field-blur")
    xdo("key", "Tab"); pixel(slider, (0,128,0), .55, "slider-focus"); pixel(toggle, (0,128,0), 1, "switch-blur")
    xdo("key", "shift+Tab"); pixel(toggle, (0,128,0), .55, "switch-reverse-focus")
    xdo("key", "shift+Tab", "shift+Tab", "shift+Tab")
    xdo("keydown", "space"); pixel(radio, (255,0,0), .4, "radio-space-held")
    xdo("keyup", "space"); label("Selections: 1"); pixel(radio, (255,0,0), .65, "radio-space-selected")
    (args.output / "PASS.txt").write_text("PASS GTK system mouse/keyboard, rendered hover/press/focus/reset, drag-out, rapid clicks, disable while held, Tab/Shift+Tab and Space\n")
    print((args.output / "PASS.txt").read_text(), end="")
finally:
    tree = [{"name": n.name, "role": n.getRoleName(), "rect": rect(n) if n.getRoleName() not in ("desktop frame", "application") else None} for n in descendants(pyatspi.Registry.getDesktop(0))]
    (args.output / "accessibility.json").write_text(json.dumps(tree, indent=2) + "\n")
    xdo("mouseup", 1)
    xdo("keyup", "space", "Shift_L")
    (args.output / "pixels.json").write_text(json.dumps(evidence, indent=2) + "\n")
