#!/bin/bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
case "$1" in
  ios-arm64) target=aarch64-apple-ios ;;
  iossimulator-arm64) target=aarch64-apple-ios-sim ;;
  osx-arm64) target=aarch64-apple-darwin ;;
  linux-bionic-arm64)
    target=aarch64-linux-android
    ndk="${ANDROID_NDK_HOME:-${ANDROID_HOME:-$HOME/Library/Android/sdk}/ndk/29.0.14206865}"
    export CARGO_TARGET_AARCH64_LINUX_ANDROID_LINKER="$ndk/toolchains/llvm/prebuilt/darwin-x86_64/bin/aarch64-linux-android30-clang"
    ;;
  *) echo "Unsupported DotNative development RID: $1" >&2; exit 1 ;;
esac
cargo build --manifest-path "$repo/native/core/Cargo.toml" --release --target "$target"
