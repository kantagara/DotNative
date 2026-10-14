#!/usr/bin/env bash
# Usage: run-linux-interactions.sh NATIVE_EXECUTABLE MANAGED_DIRECTORY OUTPUT_DIRECTORY
# Starts a dedicated X11/AT-SPI session; preview TCP port 4975 must be free.
set -euo pipefail
if [[ ${1:-} != --inside ]]; then
    if [[ $# != 3 && !( $# == 4 && $4 == --virtual-list ) ]]; then
        echo "Usage: $0 NATIVE_EXECUTABLE MANAGED_DIRECTORY OUTPUT_DIRECTORY [--virtual-list]" >&2
        exit 2
    fi
    mkdir -p "$3"
    exec xvfb-run -a -s '-screen 0 1200x1000x24' dbus-run-session -- \
        bash "$0" --inside "$(realpath "$1")" "$(realpath "$2")" "$(realpath "$3")" "${4:-}"
fi
shift
native_executable=$1
managed_directory=$2
output_directory=$3
script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
input_test=linux-interactions.py
if [[ ${4:-} == --virtual-list ]]; then input_test=linux-virtual-list.py; fi
export GTK_A11Y=atspi GDK_BACKEND=x11 GSK_RENDERER=cairo LANG=C.UTF-8
rm -f "$output_directory/PASS.txt" "$output_directory/FAIL.txt"
native_pid=
managed_pid=
cleanup() {
    for pid in "$native_pid" "$managed_pid"; do
        [[ -z $pid ]] || kill "$pid" 2>/dev/null || true
    done
    for attempt in {1..50}; do
        local live=0
        for pid in "$native_pid" "$managed_pid"; do
            if [[ -n $pid ]] && kill -0 "$pid" 2>/dev/null; then live=1; fi
        done
        [[ $live == 0 ]] && break
        sleep .1
    done
    for pid in "$native_pid" "$managed_pid"; do
        [[ -z $pid ]] || kill -KILL "$pid" 2>/dev/null || true
        [[ -z $pid ]] || wait "$pid" 2>/dev/null || true
    done
}
trap cleanup EXIT
if ! "$native_executable" --smoke >"$output_directory/smoke.log" 2>&1; then
    cp "$output_directory/smoke.log" "$output_directory/FAIL.txt"
    cat "$output_directory/smoke.log" >&2
    exit 1
fi
"$managed_directory/DotNative.DevHost" LINUX --once >"$output_directory/managed.log" 2>&1 & managed_pid=$!
"$native_executable" >"$output_directory/preview.log" 2>&1 & native_pid=$!
if ! /usr/bin/python3 "$script_directory/$input_test" --output "$output_directory" >"$output_directory/input.log" 2>&1; then
    cp "$output_directory/input.log" "$output_directory/FAIL.txt"
    cat "$output_directory/input.log" >&2
    exit 1
fi
cat "$output_directory/PASS.txt"
