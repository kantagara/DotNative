"""Observe rendered pixels independently while XCTest holds a real touch down."""
import concurrent.futures
import json
import os
import signal
import subprocess
import threading
import time


def run_with_hold_probes(command, *, device, output, probe, cwd, env, timeout=600, navigation=False, reduce_motion=False):
    deadline = time.monotonic() + timeout
    expected = ({"navigation-slide": [None], "navigation-fade": [None]} if navigation
                else {"pressed": [153], "disabled-held": [153, 204]})
    started, completed, futures = set(), set(), {}
    records = []
    lock = threading.Lock()

    def observe(spec, ended):
        phase = spec["phase"]
        samples = []
        remaining = list(expected[phase])
        # Gesture synthesis takes a moment; leave a generous margin before release.
        time.sleep(.8)
        for attempt in range(12):
            if ended.is_set():
                break
            path = output / f"{phase}-{attempt}.png"
            capture = subprocess.run(["xcrun", "simctl", "io", device, "screenshot", str(path)],
                                     capture_output=True, text=True, timeout=10)
            if capture.returncode:
                raise RuntimeError(f"Simulator screenshot failed: {capture.stderr}")
            pixels = subprocess.check_output([str(probe), str(path), str(spec["x"]), str(spec["y"])],
                                             text=True, timeout=10)
            rgba = json.loads(pixels)
            before_release = not ended.is_set()
            samples.append({"screenshot": path.name, "rgba": rgba, "before_release": before_release})
            if navigation:
                right = json.loads(subprocess.check_output([str(probe), str(path), ".8", str(spec["y"])], text=True, timeout=10))
                samples[-1]["right_rgba"] = right
                matched = (all(c > 240 for c in rgba[:3]) and abs(right[0] - 40) < 15 and abs(right[2] - 200) < 15
                           if phase == "navigation-slide" else 55 < rgba[0] < 225 and rgba[0] < rgba[1] < rgba[2])
                if reduce_motion:
                    matched = all(abs(pixel[i] - target) < 12 for pixel in (rgba, right) for i, target in enumerate((40, 120, 200)))
            else:
                matched = rgba[0] > 240 and all(abs(rgba[i] - remaining[0]) <= 12 for i in (1, 2))
            if before_release and matched:
                remaining.pop(0)
                if not remaining:
                    with lock:
                        records.append({"phase": phase, "samples": samples, "passed": True})
                    return
            time.sleep(.25)
        with lock:
            records.append({"phase": phase, "samples": samples, "passed": False})
        raise AssertionError(f"No {phase} pixels observed during touch hold: {samples}")

    process = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, text=True, bufsize=1, start_new_session=True)
    # readlines can block during a hung xcodebuild, so enforce its deadline independently.
    def terminate():
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass

    timer = threading.Timer(timeout, terminate)
    timer.start()
    try:
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool, (output / "ios-input.log").open("w") as log:
            for line in process.stdout:
                log.write(line)
                log.flush()
                if "DOTNATIVE_HOLD_BEGIN " in line:
                    spec = json.JSONDecoder().raw_decode(line.split("DOTNATIVE_HOLD_BEGIN ", 1)[1])[0]
                    phase = spec["phase"]
                    if phase not in expected or phase in started:
                        raise AssertionError(f"Unexpected hold phase: {phase}")
                    started.add(phase)
                    ended = threading.Event()
                    futures[phase] = (pool.submit(observe, spec, ended), ended)
                elif "DOTNATIVE_HOLD_END " in line:
                    phase = line.split("DOTNATIVE_HOLD_END ", 1)[1].strip()
                    if phase not in futures:
                        raise AssertionError(f"Hold ended before start: {phase}")
                    futures[phase][1].set()
                    completed.add(phase)
            code = process.wait()
            if time.monotonic() >= deadline:
                raise TimeoutError("Xcode input test timed out")
            if code:
                raise RuntimeError(f"Xcode input test failed ({code}); see {output / 'ios-input.log'}")
            if started != set(expected) or completed != set(expected):
                raise AssertionError(f"Missing touch hold evidence: started={started}, completed={completed}")
            for future, _ in futures.values():
                future.result()
    finally:
        timer.cancel()
        if process.poll() is None:
            terminate()
        process.wait()
        (output / "held-pixels.json").write_text(json.dumps(records, indent=2) + "\n")
