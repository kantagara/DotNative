#!/usr/bin/env bash
set -euo pipefail
bundle="$(cd "$(dirname "$0")" && pwd)"
state="$(mktemp -d "${TMPDIR:-/tmp}/dotnative-preview.XXXXXX")"
host_pid=""
renderer_pid=""
cleanup() {
    if [ -n "$renderer_pid" ]; then kill "$renderer_pid" 2>/dev/null || true; wait "$renderer_pid" 2>/dev/null || true; fi
    if [ -n "$host_pid" ]; then kill "$host_pid" 2>/dev/null || true; wait "$host_pid" 2>/dev/null || true; fi
    rm -f "$state/ready"
    rmdir "$state" 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
DotNativePreviewReadyFile="$state/ready" "$bundle/managed/DotNative.DevHost" Linux --once &
host_pid=$!
for ((attempt=0; attempt<300; attempt++)); do
    [ -f "$state/ready" ] && break
    if ! kill -0 "$host_pid" 2>/dev/null; then
        echo 'DotNative host exited before startup. Check the error above.' >&2
        exit 1
    fi
    sleep 0.1
done
if [ ! -f "$state/ready" ]; then echo 'Timed out starting DotNative host.' >&2; exit 1; fi
"$bundle/WidgetSamples" &
renderer_pid=$!
wait "$renderer_pid"
