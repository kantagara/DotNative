"""Build DevHost for Samples/Counter, then run this loopback protocol smoke test.
No native UI or external service is required. Uses the Linux preview port (4975).
"""
from pathlib import Path
import os
import tempfile
import socket
import struct
import subprocess
import time

root = Path(__file__).resolve().parents[2]
temporary = tempfile.TemporaryDirectory(prefix='dotnative-preview-smoke-')
ready = Path(temporary.name)/'ready'
process = subprocess.Popen(
    ['dotnet', str(root/'tools/DotNative.DevHost/bin/Debug/net10.0/DotNative.DevHost.dll'), 'Linux', '--once'],
    stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
    env={**os.environ, "DotNativePreviewReadyFile": str(ready)},
)
client = None
try:
    for _ in range(100):
        if process.poll() is not None:
            raise RuntimeError(process.communicate()[0])
        if not ready.exists():
            time.sleep(.05)
            continue
        assert ready.read_text() == str(process.pid)
        try:
            client = socket.create_connection(('127.0.0.1', 4975), timeout=.1)
            break
        except ConnectionRefusedError:
            time.sleep(.05)
    assert client is not None, 'preview did not start'
    client.settimeout(5)

    def send(kind, body=b''):
        client.sendall(struct.pack('<II', kind, len(body)) + body)

    def read_exact(size):
        result = b''
        while len(result) < size:
            part = client.recv(size-len(result))
            assert part, 'preview disconnected'
            result += part
        return result

    def read():
        kind, size = struct.unpack('<II', read_exact(8))
        assert size <= 16*1024*1024
        return kind, read_exact(size)

    send(1, struct.pack('<IIffIIfI', 1, 5, 640, 480, 1, 0, 1., 0))
    kind, body = read()
    assert kind == 100 and body[:4] == b'DNTV', 'missing initial render'
    for _ in range(3):
        send(9)
        while True:
            kind, body = read()
            if kind == 102:
                assert body == b''
                break
            assert kind == 100 and body[:4] == b'DNTV'
    send(8, struct.pack('<IfI', 1, 2., 1))
    kind, body = read()
    assert kind == 100 and body[:4] == b'DNTV', 'font scale did not redraw'
    send(9)
    assert read() == (102, b''), 'missing frame acknowledgement after accessibility update'
    client.close()
    client = None
    output, _ = process.communicate(timeout=10)
    assert process.returncode == 0 and 'Preview ended:' not in output, output
    print('PASS preview frame acknowledgements, accessibility redraw and clean disconnect')
finally:
    if client is not None:
        client.close()
    if process.poll() is None:
        process.terminate()
        process.communicate(timeout=5)
    temporary.cleanup()
