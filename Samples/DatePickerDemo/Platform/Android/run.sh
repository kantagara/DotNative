#!/bin/bash
set -euo pipefail
runner="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$runner/../.." && pwd)"
sdk="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
ndk="${ANDROID_NDK_HOME:-$sdk/ndk/29.0.14206865}"
export PATH="$ndk/toolchains/llvm/prebuilt/darwin-x86_64/bin:$sdk/platform-tools:$PATH"
export ANDROID_HOME="$sdk"
out="$runner/obj/DotNative/native"
dotnet publish "$root/DatePickerDemo.csproj" -c Release -r linux-bionic-arm64 -p:DisableUnsupportedError=true -p:IlcExportUnmanagedEntrypoints=true -p:CppLibCreator="$ndk/toolchains/llvm/prebuilt/darwin-x86_64/bin/llvm-ar" -o "$out"
mkdir -p "$runner/obj/DotNative/jniLibs/arm64-v8a"
python3 - "$out" "$ndk" "$runner/obj/DotNative/jniLibs/arm64-v8a/libdotnative.so" <<'PY'
import pathlib,subprocess,sys
out,ndk,dest=map(pathlib.Path,sys.argv[1:])
toolchain=ndk/'toolchains/llvm/prebuilt/darwin-x86_64'
libs=sorted((out/'link-inputs').iterdir())
core=next(p for p in libs if p.name=='libcore.a')
exports=out/'android.exports'
# Keep runtime internals local, as the NativeAOT shared-library linker does.
exports.write_text('DOTNATIVE_1 { global: Java_com_dotnative_runtime_NativeHost_*; Java_com_dotnative_plugins_NativeChannels_*; local: *; };')
args=[str(toolchain/'bin/aarch64-linux-android30-clang'),'-shared','--unwindlib=none','-Wl,-z,max-page-size=16384','-Wl,--no-undefined','-Wl,--version-script='+str(exports),
      '-Wl,--whole-archive',str(out/'DatePickerDemo.a'),str(core),'-Wl,--no-whole-archive']
args += ['-Wl,--start-group']+[str(p) for p in libs if p!=core]+['-Wl,--end-group']
args += [str(toolchain/'sysroot/usr/lib/aarch64-linux-android/libc++_static.a'),str(toolchain/'sysroot/usr/lib/aarch64-linux-android/libc++abi.a'),'-ldl','-lm','-llog','-lz','-latomic','-o',str(dest)]
subprocess.run(args,check=True)
PY
(cd "$runner" && bash ./gradlew --console=plain assembleDebug)
adb ${ANDROID_SERIAL:+-s "$ANDROID_SERIAL"} install -r "$runner/app/build/outputs/apk/debug/app-debug.apk"
adb ${ANDROID_SERIAL:+-s "$ANDROID_SERIAL"} shell am force-stop com.example.dotnativeapp
adb ${ANDROID_SERIAL:+-s "$ANDROID_SERIAL"} shell am start -n com.example.dotnativeapp/com.dotnative.runtime.NativeHost
