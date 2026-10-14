"""Run identical AndroidX Macrobenchmark journeys against installed Release apps.
Emulators require --allow-emulator and are always labelled smoke-only.
"""
import argparse
import hashlib
import json
import os
import pathlib
import subprocess
import time

p = argparse.ArgumentParser()
p.add_argument("--serial", required=True)
p.add_argument("--framework", choices=("dotnative", "maui"), required=True)
p.add_argument("--apk", type=pathlib.Path, required=True)
p.add_argument("--output", type=pathlib.Path, required=True)
p.add_argument("--instrumentation-apk", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parent / "android/runner/build/outputs/apk/androidTest/debug/runner-debug-androidTest.apk")
p.add_argument("--iterations", type=int, default=10)
p.add_argument("--allow-emulator", action="store_true")
p.add_argument("--defer-traces", action="store_true", help="Pull AndroidX JSON now and retain raw Perfetto traces on the device")
p.add_argument("--runtime-mode", default="unknown")
p.add_argument("--build-revision", default="unknown")
p.add_argument("--test", choices=("startup", "fixedRows", "variableRows", "mutations"))
a = p.parse_args()
if a.iterations < 1: p.error("iterations must be positive")
if not a.instrumentation_apk.is_file(): p.error("Build the Android instrumentation APK first")
package = "com.dotnative.benchmark" if a.framework == "dotnative" else "com.dotnative.mauibenchmark"
if a.output.exists(): p.error("Use a fresh output directory to preserve previous evidence")
# Hold one device-wide lock through performance, trace pull and memory capture.
# Installing a new collector during another run kills its instrumentation process.
lock_dir = pathlib.Path.home() / ".cache/DotNative/benchmarks"
lock_dir.mkdir(parents=True, exist_ok=True)
lock = (lock_dir / (hashlib.sha256(a.serial.encode()).hexdigest() + ".lock")).open("a")
try:
    if os.name == "nt":
        import msvcrt
        lock.write("0")
        lock.flush()
        lock.seek(0)
        msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
    else:
        import fcntl
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
except OSError:
    p.error("Another local benchmark owns this device; wait for its memory capture to finish")
a.output.mkdir(parents=True)

def adb(*args, timeout=120):
    return subprocess.check_output(["adb", "-s", a.serial, *args], text=True, stderr=subprocess.STDOUT, timeout=timeout)

emulated = adb("shell", "getprop", "ro.kernel.qemu").strip() == "1"
if emulated and not a.allow_emulator: p.error("Use a physical device, or explicitly pass --allow-emulator for smoke-only results")
metadata = {
    "contract": "dotnative-maui-v4", "framework": a.framework, "platform": "android",
    "device": a.serial, "emulated": emulated, "hardware_candidate": not emulated and a.iterations >= 10 and a.runtime_mode != "unknown" and a.build_revision != "unknown",
    "iterations": a.iterations, "compilation_mode": "None (packaged Release settings)",
    "runtime_mode": a.runtime_mode, "build_revision": a.build_revision,
    "collector_revision": subprocess.check_output(["git", "rev-parse", "HEAD"], text=True).strip(),
    "collector_dirty": bool(subprocess.check_output(["git", "status", "--porcelain"], text=True).strip()),
    "selection": a.test or "all", "apk_bytes": a.apk.stat().st_size,
    "instrumentation_apk_sha256": hashlib.sha256(a.instrumentation_apk.read_bytes()).hexdigest(),
    "apk_sha256": hashlib.sha256(a.apk.read_bytes()).hexdigest(),
    "fingerprint": adb("shell", "getprop", "ro.build.fingerprint").strip(),
    "abi": adb("shell", "getprop", "ro.product.cpu.abi").strip(),
    "model": adb("shell", "getprop", "ro.product.model").strip(),
    "collected_at_unix": time.time(),
}
(a.output / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
(a.output / "thermal-before.txt").write_text(adb("shell", "dumpsys", "thermalservice"))
(a.output / "battery-before.txt").write_text(adb("shell", "dumpsys", "battery"))
adb("install", "-r", str(a.instrumentation_apk), timeout=300)
adb("install", "-r", str(a.apk), timeout=300)
info = adb("shell", "dumpsys", "package", package)
(a.output / "package.txt").write_text(info)
flag_lines = [line for line in info.splitlines() if "flags=[" in line or "pkgFlags=[" in line]
if any("DEBUGGABLE" in line for line in flag_lines): raise RuntimeError("Target is debuggable; refuse a performance run")
remote = f"/sdcard/Android/media/com.dotnative.benchmarks.test/run-{a.framework}-{time.time_ns()}"
adb("shell", "mkdir", "-p", remote)
metadata["trace_remote_directory"] = remote
metadata["trace_pull_deferred"] = a.defer_traces
(a.output / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
args = ["shell", "am", "instrument", "-w", "-r", "-e", "targetPackage", package,
        "-e", "iterations", str(a.iterations), "-e", "additionalTestOutputDir", remote, "-e", "androidx.benchmark.enabledRules", "Macrobenchmark"]
args += ["-e", "class", "com.dotnative.benchmarks.PerformanceTest" + ("#" + a.test if a.test else "")]
if emulated: args += ["-e", "androidx.benchmark.suppressErrors", "EMULATOR"]
args += ["com.dotnative.benchmarks.test/androidx.test.runner.AndroidJUnitRunner"]
# Stream output to disk so progress and failures survive an interrupted run.
with (a.output / "instrumentation.txt").open("w") as log:
    completed = subprocess.run(["adb", "-s", a.serial, *args], text=True,
                               stdout=log, stderr=subprocess.STDOUT,
                               timeout=max(600, a.iterations * 300))
result = (a.output / "instrumentation.txt").read_text()
metadata["instrumentation_exit_code"] = completed.returncode
metadata["passed"] = completed.returncode == 0 and "OK (" in result and "FAILURES!!!" not in result and "INSTRUMENTATION_FAILED" not in result
(a.output / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
# Preserve AndroidX's JSON and Perfetto traces, including failed/suppressed-metric
# annotations. They remain the source of truth; no synthetic FPS is computed.
if a.defer_traces:
    destination = a.output / "androidx"
    destination.mkdir()
    filename = "com.dotnative.benchmarks.test-benchmarkData.json"
    adb("pull", remote + "/" + filename, str(destination / filename))
else:
    subprocess.run(["adb", "-s", a.serial, "pull", remote, str(a.output / "androidx")], check=True, timeout=120)
(a.output / "thermal-after.txt").write_text(adb("shell", "dumpsys", "thermalservice"))
(a.output / "battery-after.txt").write_text(adb("shell", "dumpsys", "battery"))
if metadata["passed"]:
    memory = adb("shell", "am", "instrument", "-w", "-r", "-e", "targetPackage", package,
                 "-e", "class", "com.dotnative.benchmarks.MemoryTest",
                 "com.dotnative.benchmarks.test/androidx.test.runner.AndroidJUnitRunner", timeout=180)
    (a.output / "memory.txt").write_text(memory)
    metadata["memory_passed"] = "OK (" in memory and "FAILURES!!!" not in memory and "INSTRUMENTATION_FAILED" not in memory
    (a.output / "metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
    if not metadata["memory_passed"]: raise RuntimeError("Live-process memory journey failed; see memory.txt")
if not metadata["passed"]:
    raise RuntimeError("Benchmark failed; inspect instrumentation.txt and saved traces")
print(f"PASS {a.framework} AndroidX benchmarks; {a.output}; smoke_only={emulated}")
