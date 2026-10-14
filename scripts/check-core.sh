#!/bin/bash
# Local equivalent of the portable core CI checks. Native platform journeys
# remain in tests/native and must run on their matching OS/device.
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
cd "$repo"
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
bash scripts/format.sh check
dotnet run --project tests/DotNative.Tests -c Release
dotnet run --project tests/DotNative.Cli.Tests -c Release
cargo test --locked --manifest-path native/core/Cargo.toml
dotnet new install ./templates/DotNative.App --force
dotnet run --project tests/DotNative.Template.Tests -c Release
