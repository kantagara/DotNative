"""10,000-row GTK fixture, driven by real XTest mouse input and AT-SPI geometry."""
import argparse
import pathlib
import subprocess
import time

import pyatspi
from gi.repository import GLib

parser = argparse.ArgumentParser()
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
window_origin = (0, 0)


def descendants(node):
    yield node
    for child in node:
        yield from descendants(child)


def find(name):
    return next((n for n in descendants(pyatspi.Registry.getDesktop(0)) if n.name == name), None)


def wait(reason, check):
    until = time.monotonic() + 12
    while time.monotonic() < until:
        context = GLib.MainContext.default()
        while context.pending():
            context.iteration(False)
        result = check()
        if result:
            return result
        time.sleep(.05)
    raise AssertionError(reason)


def rect(node):
    r = node.queryComponent().getExtents(pyatspi.WINDOW_COORDS)
    return r.x + window_origin[0], r.y + window_origin[1], r.width, r.height


def xdo(*args):
    subprocess.run(["xdotool", *map(str, args)], check=True, capture_output=True, timeout=5)


def tap(name):
    node = wait(f"Missing {name}", lambda: find(name))
    x, y, width, height = rect(node)
    xdo("mousemove", x + width // 2, y + height // 2)
    xdo("click", 1)
    time.sleep(.35)


def row_rect(index):
    return rect(wait(f"Missing Row {index}", lambda: find(f"Row {index}")))


mutation = 0


def mutate(action):
    global mutation
    tap(action)
    mutation += 1
    wait("Mutation did not finish", lambda: find(f"Mutation {mutation}"))


wait("Fixture did not start", lambda: find("Ready: 10000 rows"))
def preview_window():
    ids = subprocess.check_output(["xdotool", "search", "--name", ""], text=True, timeout=5).splitlines()
    for xid in ids:
        title = subprocess.check_output(["xdotool", "getwindowname", xid], text=True, timeout=5)
        if title.startswith("DotNative Preview"):
            return xid
    return None


window = wait("Preview X11 window", preview_window)
geometry = subprocess.check_output(["xdotool", "getwindowgeometry", "--shell", window], text=True, timeout=5)
geometry = dict(line.split("=", 1) for line in geometry.splitlines() if "=" in line)
window_origin = (int(geometry["X"]), int(geometry["Y"]))
xdo("windowfocus", "--sync", window)
tap("Jump 5000")
anchor = row_rect(5000)
tap("Row 5000")
wait("Wrong callback after jump", lambda: find("Selected 5000"))
next_y = row_rect(5001)[1]
mutate("Grow")
wait("Automatic height did not grow", lambda: row_rect(5001)[1] > next_y + 30)
assert abs(row_rect(5000)[1] - anchor[1]) <= 2, "grow lost anchor"
mutate("Grow")
wait("Automatic height did not shrink", lambda: abs(row_rect(5001)[1] - next_y) <= 2)
mutate("Prepend")
assert abs(row_rect(5000)[1] - anchor[1]) <= 2, "prepend lost anchor"
for _ in range(2):
    mutate("Reverse")
    assert abs(row_rect(5000)[1] - anchor[1]) <= 2, "reorder lost anchor"
tap("Row 5000")
wait("Recycled callback changed identity", lambda: find("Selected 5000"))
# The scroll wheel must update the managed realization window through event 10.
x, y, width, height = row_rect(5000)
xdo("mousemove", x + width // 2, y + height // 2 + 80)
before_wheel = {n.name for n in descendants(pyatspi.Registry.getDesktop(0)) if n.name.startswith("Row ")}
xdo("click", "--repeat", 12, "--delay", 70, 5)
wait("Native wheel did not advance the managed window", lambda: any(
    n.name.startswith("Row ") and n.name not in before_wheel
    for n in descendants(pyatspi.Registry.getDesktop(0))))
tap("End")
tap("Row 9999")
wait("End row callback", lambda: find("Selected 9999"))
rows = [n for n in descendants(pyatspi.Registry.getDesktop(0)) if n.name.startswith("Row ")]
assert len(rows) < 100, f"Unbounded native realization: {len(rows)}"
tap("Clear")
wait("Empty state", lambda: find("Empty"))
assert find("Row 9999") is None, "stale recycled content after clear"
message = "PASS GTK 10,000 rows: jump, callbacks, automatic height, anchors, wheel, end, bounded window, clear"
(args.output / "PASS.txt").write_text(message + "\n")
print(message)
