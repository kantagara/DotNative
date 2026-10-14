#!/bin/bash
set -euo pipefail
runner="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$runner/../.." && pwd)"
out="$root/obj/DotNative/Mac"
dotnet publish "$root/DatePickerDemo.csproj" -c Release -r osx-arm64 -o "$out/native"
mkdir -p "$out/DatePickerDemo.app/Contents/MacOS"
python3 - "$out" <<'PY'
import pathlib,plistlib,sys
out=pathlib.Path(sys.argv[1])
args=['"'+str(p)+'"' for p in sorted((out/'native/link-inputs').iterdir())]
args += ['-framework','AppKit','-framework','QuartzCore','-framework','CoreText','-framework','CoreGraphics','-framework','Foundation','-framework','Security','-framework','GSS','-licucore','-lc++']
args.append((out/'native/plugins.link.rsp').read_text())
(out/'link.rsp').write_text('\n'.join(args))
with (out/'DatePickerDemo.app/Contents/Info.plist').open('wb') as f:
 plistlib.dump(dict(CFBundleExecutable='DatePickerDemo',CFBundleIdentifier='com.example.dotnativeapp',CFBundleName='DatePickerDemo',CFBundlePackageType='APPL',NSHighResolutionCapable=True,LSMinimumSystemVersion='13.0'),f)
PY
xcrun clang -fobjc-arc -arch arm64 -mmacosx-version-min=13.0 "$runner/Runner/main.m" \
  -Wl,-force_load,"$out/native/DatePickerDemo.a" @"$out/link.rsp" -o "$out/DatePickerDemo.app/Contents/MacOS/DatePickerDemo"
codesign --force --sign - "$out/DatePickerDemo.app"
open "$out/DatePickerDemo.app"
