"""Run system-input tests against the real native preview + C# host.
Requires a connected ARM64 Android device/emulator or a booted iOS simulator. All outputs stay in --output.
"""
import argparse
import os
import pathlib
import signal
import subprocess
import sys
import tempfile
import time

parser = argparse.ArgumentParser()
parser.add_argument("platform", choices=("ios", "android"))
parser.add_argument("--device", required=True)
parser.add_argument("--test-filter", help="Optional XCTest class/method to run")
parser.add_argument("--navigation", action="store_true", help="Run the navigation timing/lifetime fixture")
parser.add_argument("--virtual-list", action="store_true", help="Run the 10,000-row virtualization fixture")
parser.add_argument("--output", type=pathlib.Path)
parser.add_argument("--reduce-motion", action="store_true", help="iOS navigation: verify stationary interactive Back with Reduce Motion")
args = parser.parse_args()
if args.virtual_list and args.navigation:
    parser.error("Choose one fixture: --virtual-list or --navigation")
if args.reduce_motion:
    if args.platform != "ios" or not args.navigation:
        parser.error("--reduce-motion requires ios --navigation")
    args.test_filter = "InteractionUITests/NavigationUITests/testCustomInteractiveCancellationAndCompletion"
root = pathlib.Path(__file__).resolve().parents[2]
out = (args.output or pathlib.Path(tempfile.mkdtemp(prefix="dotnative-input-"))).resolve()
out.mkdir(parents=True, exist_ok=True)
env = os.environ.copy()
env.setdefault("DOTNET_ROLL_FORWARD", "Major")
env.setdefault("JAVA_HOME", "/opt/homebrew/opt/openjdk@17")
env.setdefault("ANDROID_HOME", str(pathlib.Path.home() / "Library/Android/sdk"))
app = out / "app"

def run(command, log, timeout=600):
    with (out / log).open("w") as stream:
        result = subprocess.run(command, cwd=root, env=env, stdout=stream,
                                stderr=subprocess.STDOUT, timeout=timeout)
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}); see {out / log}")

run([sys.executable, str(root / "tests/native/stage-interaction-app.py"), str(app)], "stage.log")
if args.navigation:
    import shutil
    shutil.copy2(root / "tests/native/NavigationApp/App.cs", app / "App.cs")
    shutil.copy2(root / "tests/native/NavigationInputTest.java", app / "Platform/Android/app/src/androidTest/java/com/dotnative/runtime/NavigationInputTest.java")
if args.virtual_list:
    import shutil
    shutil.copy2(root / "tests/native/VirtualListApp/App.cs", app / "App.cs")
    shutil.copy2(root / "tests/native/VirtualListInputTest.java", app / "Platform/Android/app/src/androidTest/java/com/dotnative/runtime/VirtualListInputTest.java")
preview_log = out / "preview.log"
with preview_log.open("w") as stream:
    preview = subprocess.Popen(["dotnet", "run", "--project", "tools/DotNative.Cli", "--",
        "run", args.platform, "--hot-reload", "--project", str(app), "--device", args.device],
        cwd=root, env=env, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
original_reduce_motion = None
try:
    if args.reduce_motion:
        preference = ["xcrun", "simctl", "spawn", args.device, "defaults"]
        original_reduce_motion = subprocess.check_output(preference + ["read", "com.apple.Accessibility", "ReduceMotionEnabled"], text=True).strip()
        subprocess.run(preference + ["write", "com.apple.Accessibility", "ReduceMotionEnabled", "-bool", "true"], check=True)
    deadline = time.monotonic() + 600
    while "DotNative preview ready." not in preview_log.read_text():
        if preview.poll() is not None:
            raise RuntimeError(f"Preview exited; see {preview_log}")
        if time.monotonic() > deadline:
            raise TimeoutError(f"Preview did not start; see {preview_log}")
        time.sleep(.25)
    if args.platform == "android":
        env["ANDROID_SERIAL"] = args.device
        native = root / "tools/DotNative.DevHost/obj/Previews/InteractionApp/android/Android"
        try:
            run(["bash", str(native / "gradlew"), "-p", str(native),
                "-Pandroid.testInstrumentationRunnerArguments.class=com.dotnative.runtime." + ("VirtualListInputTest" if args.virtual_list else "NavigationInputTest" if args.navigation else "InteractionInputTest"),
                "connectedDebugAndroidTest"], "android-input.log")
        finally:
            # Preserve failing XML/logcat before the next fixture overwrites obj.
            import shutil
            for source, destination in [("app/build/reports/androidTests/connected", "android-report"),
                                        ("app/build/outputs/androidTest-results", "android-results")]:
                if (native / source).exists():
                    shutil.copytree(native / source, out / destination, dirs_exist_ok=True)
    else:
        project = out / "ios"
        source = [str(root / "tests/native/ios-virtual-list/VirtualListUITests.swift")] if args.virtual_list else [str(root / "tests/native/ios-navigation/NavigationUITests.swift")] if args.navigation else []
        run(["ruby", str(root / "tests/native/ios-interactions/generate-project.rb"), str(project), *source], "project.log")
        command = ["xcodebuild", "-project", str(project / "InteractionUITests.xcodeproj"),
            "-scheme", "InteractionUITests", "-destination", f"platform=iOS Simulator,id={args.device}",
            "-derivedDataPath", str(project / "build"), "-resultBundlePath", str(out / "input.xcresult"),
            "-parallel-testing-enabled", "NO", *(["-only-testing:" + args.test_filter] if args.test_filter else []), "test"]
        if args.virtual_list or (args.navigation and args.test_filter and "testCustomInteractive" not in args.test_filter):
            run(command, "ios-input.log")
        else:
            probe = out / "pixel-probe"
            run(["xcrun", "swiftc", str(root / "tests/native/ios-interactions/PixelProbe.swift"),
                 "-o", str(probe)], "pixel-probe-build.log")
            from ios_hold_probe import run_with_hold_probes
            run_with_hold_probes(command, device=args.device, output=out, probe=probe, cwd=root, env=env, navigation=args.navigation, reduce_motion=args.reduce_motion)
    print(f"PASS {args.platform} system-input tests. Results: {out}")
finally:
    if original_reduce_motion is not None:
        subprocess.run(preference + ["write", "com.apple.Accessibility", "ReduceMotionEnabled", "-bool",
                                     "true" if original_reduce_motion == "1" else "false"], check=True)
    if preview.poll() is None:
        os.killpg(preview.pid, signal.SIGINT)
        try:
            preview.wait(timeout=10)
        except subprocess.TimeoutExpired:
            os.killpg(preview.pid, signal.SIGTERM)
            preview.wait(timeout=10)
