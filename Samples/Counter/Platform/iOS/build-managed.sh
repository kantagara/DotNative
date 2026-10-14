#!/bin/bash
set -euo pipefail
runner_dir="$(cd "$(dirname "$0")" && pwd)"
app_root="$(cd "$runner_dir/../.." && pwd)"
case "${PLATFORM_NAME:-iphonesimulator}" in
  iphonesimulator) rid=iossimulator-arm64 ;;
  iphoneos) rid=ios-arm64 ;;
  *) echo "Unsupported platform: ${PLATFORM_NAME}" >&2; exit 1 ;;
esac
out="$runner_dir/obj/DotNative/$rid"
mkdir -p "$out"
# Xcode exports TARGETNAME=Runner; MSBuild environment properties are case-insensitive.
# Remove it for all projects, including local framework ProjectReferences.
env -u TARGETNAME dotnet publish "$app_root/Counter.csproj" -c Release -r "$rid" -o "$out"
# NativeAOT static archives contain app code only. Link the actual runtime and
# native package dependencies staged by MSBuild, plus Apple system frameworks.
python3 - "$out" <<'PY'
import pathlib, sys
out = pathlib.Path(sys.argv[1]).resolve()
libs = sorted((out / 'link-inputs').iterdir())
if not libs or not any(p.name == 'libcore.a' for p in libs):
    raise SystemExit('NativeAOT link inputs were not staged')
args = ['"' + str(p) + '"' for p in libs]
args += ['-framework', 'UIKit', '-framework', 'CoreGraphics', '-framework', 'QuartzCore', '-framework', 'Foundation',
         '-framework', 'CoreText', '-framework', 'CoreFoundation', '-framework', 'Security', '-framework', 'CryptoKit',
         '-framework', 'GSS', '-licucore', '-lc++', '-lswiftCore', '-lswiftFoundation']
plugin_args = out / 'plugins.link.rsp'
if plugin_args.exists():
    args.append(plugin_args.read_text())
(out / 'link.rsp').write_text('\n'.join(args) + '\n')
PY
