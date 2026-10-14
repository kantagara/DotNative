"""Run the managed navigation fixture against an actual macOS/GTK native runner.
Build DotNative.DevHost with DotNativeApp=tests/native/NavigationApp and
DotNativeStartupType=InteractionApp.Program, plus the matching DotNativePlatform.
On Linux invoke this script under xvfb-run/dbus-run-session or a desktop session.
"""
import argparse
import os
import pathlib
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument("platform", choices=("MACOS", "LINUX"))
parser.add_argument("--hide-during-transition", action="store_true", help="GTK/X11 hidden-window completion check (requires xdotool)")
parser.add_argument("--native", required=True)
parser.add_argument("--managed", required=True)
parser.add_argument("--output", required=True, type=pathlib.Path)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, DOTNATIVE_NAVIGATION_AUTORUN="1")
processes = []
hidden_until = None
hidden_window = None
hide_tested = False
try:
    with (args.output / "managed.log").open("w") as managed_log, (args.output / "native.log").open("w") as native_log:
        processes.append(subprocess.Popen([args.managed, args.platform, "--once"], env=env,
                                          stdout=managed_log, stderr=subprocess.STDOUT))
        processes.append(subprocess.Popen([args.native], env=env, stdout=native_log, stderr=subprocess.STDOUT))
        deadline = time.monotonic() + 90
        while time.monotonic() < deadline:
            log = (args.output / "managed.log").read_text()
            if "NAV_RESULT FAIL" in log or any(p.poll() is not None for p in processes):
                raise RuntimeError(f"Navigation failed; see {args.output}")
            if args.hide_during_transition and not hide_tested and "NAV_TIMING Slide Linear 150 pop" in log:
                hidden_window = subprocess.check_output(["xdotool", "search", "--name", "DotNative Preview"], text=True).splitlines()[0]
                subprocess.run(["xdotool", "windowunmap", hidden_window], check=True)
                hidden_until = time.monotonic() + 1.5
                hide_tested = True
            if hidden_until is not None and time.monotonic() >= hidden_until:
                subprocess.run(["xdotool", "windowmap", hidden_window], check=True)
                hidden_until = None
            if "NAV_RESULT PASS 24 round trips" in log:
                assert not args.hide_during_transition or hide_tested, "Missing hidden-window test"
                assert log.count("NAV_TIMING ") == 48, "Missing push/pop measurements"
                print(f"PASS {args.platform}: 24 timed push/pop pairs and scoped disposal; {args.output}")
                break
            time.sleep(.1)
        else:
            raise TimeoutError(f"Navigation did not finish; see {args.output}")
finally:
    for process in processes:
        if process.poll() is None:
            process.terminate()
    for process in processes:
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
