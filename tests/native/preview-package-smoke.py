"""Linux packaged startup check. Does not establish interactive UI correctness."""
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import time

bundle = Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix="dotnative-package-smoke-") as temp:
    log_path = Path(temp) / "startup.log"
    with log_path.open("w") as log:
        process = subprocess.Popen(
            ["bash", str(bundle / "run-linux.sh")], cwd=temp,
            stdout=log, stderr=subprocess.STDOUT, start_new_session=True,
        )
        try:
            deadline = time.monotonic() + 30
            connected = False
            while time.monotonic() < deadline:
                text = log_path.read_text(errors="replace")
                if process.poll() is not None:
                    raise RuntimeError("Packaged demo exited early:\n" + text)
                if any(message in text for message in ("DotNative fatal", "Preview ended:", "Unhandled exception")):
                    raise RuntimeError(text)
                if "Native preview connected." in text:
                    if connected:
                        print("PASS packaged Linux startup and host/renderer handshake")
                        break
                    connected = True
                    # Keep the process alive across several seconds of rendering.
                    time.sleep(3)
                else:
                    time.sleep(.1)
            else:
                raise RuntimeError("No host/renderer handshake:\n" + text)
        finally:
            try:
                os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL)
                process.wait(timeout=5)
