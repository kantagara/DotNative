#!/bin/bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
feed="$repo/bin/packages"
ndk="${ANDROID_NDK_HOME:-${ANDROID_HOME:-$HOME/Library/Android/sdk}/ndk/29.0.14206865}"
export CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER="$ndk/toolchains/llvm/prebuilt/darwin-x86_64/bin/aarch64-linux-android30-clang"
mkdir -p "$feed"
cargo build --manifest-path "$repo/native/core/Cargo.toml" --release --target aarch64-apple-ios
cargo build --manifest-path "$repo/native/core/Cargo.toml" --release --target aarch64-apple-ios-sim
cargo build --manifest-path "$repo/native/core/Cargo.toml" --release --target aarch64-apple-darwin
cargo build --manifest-path "$repo/native/core/Cargo.toml" --release --target aarch64-linux-android
dotnet pack "$repo/packaging/DotNative.Native.Mac/DotNative.Native.Mac.csproj" -c Release -o "$feed"
dotnet pack "$repo/packaging/DotNative.Native.Android/DotNative.Native.Android.csproj" -c Release -o "$feed"
dotnet pack "$repo/src/DotNative/DotNative.csproj" -c Release -o "$feed"
dotnet pack "$repo/packaging/DotNative.Native.iOS/DotNative.Native.iOS.csproj" -c Release -o "$feed"
dotnet pack "$repo/packaging/DotNative.Essentials/DotNative.Essentials.csproj" -c Release -o "$feed"
dotnet pack "$repo/packaging/DotNative.App/DotNative.App.csproj" -c Release -o "$feed"
dotnet pack "$repo/plugins/DotNative.DeviceInfo/DotNative.DeviceInfo.csproj" -c Release -o "$feed"
dotnet pack "$repo/templates/DotNative.Templates.csproj" -c Release -o "$feed"
python3 "$repo/scripts/verify-packages.py"
