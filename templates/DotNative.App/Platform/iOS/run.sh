#!/bin/bash
set -euo pipefail
app_root="$(cd "$(dirname "$0")/../.." && pwd)"
# Pass a device UDID when multiple simulators are booted.
simulator="${1:-booted}"
if ! xcrun simctl getenv "$simulator" HOME >/dev/null 2>&1; then
  echo "Boot an iOS Simulator in Xcode, then run: dotnet run -- <UDID>" >&2
  exit 1
fi
xcodebuild -project "$app_root/Platform/iOS/Runner.xcodeproj" -scheme Runner \
  -configuration Debug -sdk iphonesimulator -destination 'generic/platform=iOS Simulator' \
  -derivedDataPath "$app_root/obj/DotNative/xcode" CODE_SIGNING_ALLOWED=NO build
app="$app_root/obj/DotNative/xcode/Build/Products/Debug-iphonesimulator/AppName.app"
xcrun simctl install "$simulator" "$app"
xcrun simctl launch "$simulator" com.example.dotnativeapp
