"""Run XCTest against a separately built/installed Release fixture; retain xcresult."""
import argparse
import hashlib
import json
import pathlib
import plistlib
import re
import subprocess
import time

p = argparse.ArgumentParser()
p.add_argument("--framework", choices=("dotnative", "maui"), required=True)
p.add_argument("--platform", choices=("ios", "macos"), required=True)
p.add_argument("--app", type=pathlib.Path, required=True)
p.add_argument("--destination", required=True, help="xcodebuild destination, e.g. platform=iOS Simulator,id=UDID")
p.add_argument("--output", type=pathlib.Path, required=True)
p.add_argument("--test", choices=("startup", "fixedRows", "variableRows", "mutations", "scrolling"), help="Run one journey; default runs the complete suite")
p.add_argument("--iterations", type=int, default=10)
p.add_argument("--runtime-mode", default="unknown", help="Record the actual build runtime, e.g. nativeaot or maui-mono-aot; unknown is not a qualified comparison")
p.add_argument("--build-revision", default="unknown", help="Source revision of the measured app, not the test runner")
p.add_argument("--team", help="Apple development team for a provisioned physical-device test runner")
a = p.parse_args()
if a.iterations < 1: p.error("iterations must be positive")
physical_ios = a.platform == "ios" and "Simulator" not in a.destination
if physical_ios and not a.team: p.error("Physical iOS tests require --team and a provisioned test runner")
root = pathlib.Path(__file__).resolve().parent
output = a.output.resolve()
if output.exists(): p.error("Choose a fresh output directory to retain prior evidence")
output.mkdir(parents=True)
if (output / "results.xcresult").exists(): p.error("Choose a fresh output directory; results already exist")
app = a.app.resolve()
info_path = app / ("Contents/Info.plist" if (app / "Contents").exists() else "Info.plist")
info = plistlib.loads(info_path.read_bytes())
binary = app / ("Contents/MacOS" if (app / "Contents/MacOS").exists() else "") / info["CFBundleExecutable"]
bundle = "com.dotnative.benchmark" if a.framework == "dotnative" else "com.dotnative.mauibenchmark"
if info["CFBundleIdentifier"] != bundle: p.error("App bundle does not match selected framework")
# Install the hashed artifact on the selected iOS target before XCTest launches it.
# A shared bundle identifier must never silently select an older installed build.
if a.platform == "ios":
    match = re.search(r"(?:^|,)id=([^,]+)", a.destination)
    if not match: p.error("iOS requires an explicit destination id so the measured artifact can be installed")
    device = match.group(1)
    install = (["xcrun", "devicectl", "device", "install", "app", "--device", device, str(app)]
               if physical_ios else ["xcrun", "simctl", "install", device, str(app)])
    subprocess.run(install, check=True, timeout=300)
bundle_files = [file for file in app.rglob("*") if file.is_file() and not file.is_symlink()]
metadata = dict(test=a.test or "all", runtime_mode=a.runtime_mode, build_revision=a.build_revision,
                application_bundle_bytes=sum(file.stat().st_size for file in bundle_files),
                contract="dotnative-maui-v4", framework=a.framework, platform=a.platform,
                destination=a.destination, iterations=a.iterations, collected_at_unix=time.time(),
                executable_sha256=hashlib.sha256(binary.read_bytes()).hexdigest(),
                info_plist_sha256=hashlib.sha256(info_path.read_bytes()).hexdigest(),
                os=subprocess.check_output(["sw_vers"], text=True).strip(),
                hardware_model=subprocess.check_output(["sysctl", "-n", "hw.model"], text=True).strip(),
                collector_dirty=bool(subprocess.check_output(["git", "-C", str(root.parent), "status", "--porcelain"], text=True).strip()),
                collector_revision=subprocess.check_output(["git", "-C", str(root.parent), "rev-parse", "HEAD"], text=True).strip(),
                architecture=subprocess.check_output(["lipo", "-archs", str(binary)], text=True).strip(),
                xcode=subprocess.check_output(["xcodebuild", "-version"], text=True).strip(),
                smoke_only=True,
                note="Read raw XCTest metric identifiers. UI journey clock duration is not FPS. Verify build/runtime settings separately.")
if physical_ios:
    details = output / "device.json"
    subprocess.run(["xcrun", "devicectl", "device", "info", "details", "--device", device,
                    "--json-output", str(details)], check=True, stdout=subprocess.DEVNULL, timeout=30)
    target_info = json.loads(details.read_text())["result"]
    metadata["host_os"] = metadata["os"]
    metadata["host_hardware_model"] = metadata["hardware_model"]
    metadata["os"] = target_info["deviceProperties"]["osVersionNumber"]
    metadata["hardware_model"] = target_info["hardwareProperties"]["productType"]
    metadata["physical_device"] = True
(output / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
project = output / "project"
subprocess.run(["ruby", str(root / "apple/generate-project.rb"), str(project),
                "ios" if a.platform == "ios" else "osx", bundle, str(a.iterations), str(app)], check=True)
command = ["xcodebuild", "test", "-project", str(project / "PerformanceTests.xcodeproj"),
           "-scheme", "PerformanceTests", "-configuration", "Release", "-destination", a.destination,
           "-derivedDataPath", str(output / "build"), "-resultBundlePath", str(output / "results.xcresult"), "-parallel-testing-enabled", "NO",
           "CODE_SIGNING_ALLOWED=YES" if physical_ios or a.platform == "macos" else "CODE_SIGNING_ALLOWED=NO"]
if a.platform == "macos":
    command += ["CODE_SIGN_IDENTITY=-"]
if a.test == "scrolling":
    command += ["-only-testing:PerformanceTests/PerformanceTests/testFixedRows", "-only-testing:PerformanceTests/PerformanceTests/testVariableRows"]
elif a.test:
    command += ["-only-testing:PerformanceTests/PerformanceTests/test" + a.test[0].upper() + a.test[1:]]
if physical_ios:
    command += ["DEVELOPMENT_TEAM=" + a.team, "CODE_SIGN_IDENTITY=Apple Development"]
with (output / "xcodebuild.log").open("w") as log:
    try:
        result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=max(600, a.iterations * 180))
    except (subprocess.TimeoutExpired, KeyboardInterrupt) as error:
        metadata.update(passed=False, aborted=True, failure=type(error).__name__, frame_timing_comparison_ready=False)
        (output / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
        raise
metadata["passed"] = result.returncode == 0
log_text = (output / "xcodebuild.log").read_text(errors="replace")
scroll_evidence = [json.loads(line.split("SCROLL_EVIDENCE ", 1)[1])
                   for line in log_text.splitlines() if line.startswith("SCROLL_EVIDENCE ")]
(output / "scroll-evidence.json").write_text(json.dumps(scroll_evidence, indent=2) + "\n")
metadata["reported_metric_ids"] = sorted(set(re.findall(r"performanceMetricID:([^,\n]+)", log_text)))
metadata["scroll_signpost_reported"] = any("Scroll_DraggingAndDeceleration" in metric for metric in metadata["reported_metric_ids"])
metadata["frame_timing_comparison_ready"] = False
(output / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
if result.returncode: raise SystemExit(f"XCTest failed; see {output / 'xcodebuild.log'}")
with (output / "metrics.json").open("w") as metrics:
    subprocess.run(["xcrun", "xcresulttool", "get", "test-results", "metrics", "--path",
                    str(output / "results.xcresult")], stdout=metrics, check=True, timeout=60)
raw_metrics = json.loads((output / "metrics.json").read_text())
launch_metrics = [metric for test in raw_metrics for run in test.get("testRuns", [])
                  for metric in run.get("metrics", [])
                  if "XCTMetric_ApplicationLaunch-" in metric.get("identifier", "")]
metadata["startup_sample_count"] = sum(len(metric.get("measurements", [])) for metric in launch_metrics)
metadata["startup_metric_reported"] = metadata["startup_sample_count"] > 0
metadata["startup_comparison_ready"] = False
metadata["journeys_passed"] = metadata["passed"]
if a.test == "startup" and metadata["startup_sample_count"] != a.iterations:
    metadata["passed"] = False
    metadata["failure"] = "Expected launch samples for every measured iteration"
(output / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
if a.test in (None, "startup") and not metadata["startup_metric_reported"]:
    print("WARNING: XCTest exported no launch samples; startup performance is unqualified.")
if not metadata["passed"]:
    raise SystemExit(f"Launch journey passed but expected {a.iterations} metric samples, got {metadata['startup_sample_count']}; evidence: {output}")
print(f"PASS journeys {a.framework} {a.platform}; scroll_signpost_reported={metadata['scroll_signpost_reported']}; raw XCTest results: {output / 'results.xcresult'}")
