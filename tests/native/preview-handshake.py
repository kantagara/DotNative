"""A pre-handshake disconnect must not consume the dev host's --once session.
Uses real TCP, then verifies a valid client receives and acknowledges a UI frame.
"""
import argparse
import os
import pathlib
import socket
import struct
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument("--host", type=pathlib.Path, required=True)
parser.add_argument("--platform", choices=("IOS", "ANDROID", "MACOS", "WINDOWS", "LINUX"), required=True)
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
platform = {"IOS": 1, "ANDROID": 2, "MACOS": 3, "WINDOWS": 4, "LINUX": 5}[args.platform]
args.output.mkdir(parents=True, exist_ok=True)
ready = args.output / "ready.txt"
ready.unlink(missing_ok=True)
(args.output / "PASS.txt").unlink(missing_ok=True)
env = os.environ.copy()
env["DotNativePreviewReadyFile"] = str(ready)


def read_exact(stream, size):
    chunks = bytearray()
    while len(chunks) < size:
        chunk = stream.recv(size - len(chunks))
        if not chunk:
            raise AssertionError("Host disconnected before delivering its first UI frame")
        chunks.extend(chunk)
    return bytes(chunks)


with (args.output / "host.log").open("w") as log:
    host = subprocess.Popen([str(args.host), args.platform, "--once"], stdout=log,
                            stderr=subprocess.STDOUT, env=env)
    try:
        deadline = time.monotonic() + 15
        while not ready.exists():
            if host.poll() is not None or time.monotonic() >= deadline:
                raise AssertionError("Host did not listen; see host.log")
            time.sleep(.02)
        # Reproduce a connection cancelled before its hello (or a port probe).
        with socket.create_connection(("127.0.0.1", 4970 + platform), timeout=5):
            pass
        time.sleep(.2)
        assert host.poll() is None, "--once exited after an empty connection, before any application session"
        with socket.create_connection(("127.0.0.1", 4970 + platform), timeout=5) as invalid:
            invalid.sendall(struct.pack("<II", 99, 0))
            assert invalid.recv(1) == b"", "Invalid hello was not rejected"
        with socket.create_connection(("127.0.0.1", 4970 + platform), timeout=8) as silent:
            assert silent.recv(1) == b"", "A silent client must time out before blocking future launches"
        assert host.poll() is None, "Pre-handshake failures consumed --once"
        with socket.create_connection(("127.0.0.1", 4970 + platform), timeout=5) as client:
            hello = struct.pack("<IIffIifi", 1, platform, 640., 720., 1, 0, 1., 0)
            client.sendall(struct.pack("<II", 1, len(hello)) + hello)
            kind, size = struct.unpack("<II", read_exact(client, 8))
            assert kind == 100 and 16 <= size <= 16 * 1024 * 1024, (kind, size)
            commands = read_exact(client, size)
            assert commands.startswith(b"DNTV"), "Expected a native UI command batch"
            client.sendall(struct.pack("<II", 6, 0))
        assert host.wait(timeout=10) == 0, "Host failed to stop after the real application session"
        (args.output / "PASS.txt").write_text("PASS empty/invalid/silent connections rejected, valid handshake renders, --once exits after the application session\n")
        print((args.output / "PASS.txt").read_text(), end="")
    finally:
        if host.poll() is None:
            host.terminate()
            try:
                host.wait(timeout=5)
            except subprocess.TimeoutExpired:
                host.kill()
                host.wait()
