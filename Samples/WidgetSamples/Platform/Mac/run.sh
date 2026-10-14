#!/bin/bash
set -euo pipefail
runner="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$runner/../.." && pwd)"
out="$root/obj/DotNative/Mac"
dotnet publish "$root/WidgetSamples.csproj" -c Release -r osx-arm64 -o "$out/native"
mkdir -p "$out/WidgetSamples.app/Contents/MacOS"
python3 - "$out" <<'PY'
import pathlib,plistlib,sys
out=pathlib.Path(sys.argv[1])
args=['"'+str(p)+'"' for p in sorted((out/'native/link-inputs').iterdir())]
args += ['-framework','AppKit','-framework','QuartzCore','-framework','CoreText','-framework','CoreGraphics','-framework','Foundation','-framework','Security','-framework','GSS','-licucore','-lc++']
args.append((out/'native/plugins.link.rsp').read_text())
(out/'link.rsp').write_text('\n'.join(args))
with (out/'WidgetSamples.app/Contents/Info.plist').open('wb') as f:
 plistlib.dump(dict(CFBundleExecutable='WidgetSamples',CFBundleIdentifier='com.dotnative.widgetsamples',CFBundleName='WidgetSamples',CFBundlePackageType='APPL',NSHighResolutionCapable=True,LSMinimumSystemVersion='13.0'),f)
PY
xcrun clang -fobjc-arc -arch arm64 -mmacosx-version-min=13.0 "$runner/Runner/main.m" \
  -Wl,-force_load,"$out/native/WidgetSamples.a" @"$out/link.rsp" -o "$out/WidgetSamples.app/Contents/MacOS/WidgetSamples"
codesign --force --sign - "$out/WidgetSamples.app"
open "$out/WidgetSamples.app"
