#!/bin/bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo"
mode="${1:-format}"
case "$mode" in
  format|check) ;;
  *) echo 'Usage: bash scripts/format.sh [format|check]' >&2; exit 2 ;;
esac
dotnet tool restore
for directory in src packaging plugins tests templates Samples; do
  dotnet csharpier "$mode" "$directory"
done
if [ "$mode" = check ]; then
  cargo fmt --manifest-path native/core/Cargo.toml --check
else
  cargo fmt --manifest-path native/core/Cargo.toml
fi
