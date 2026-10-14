"""Stage a source-built, NativeAOT benchmark using the production app runners."""
import argparse
import pathlib
import shutil

parser = argparse.ArgumentParser()
parser.add_argument("output", type=pathlib.Path)
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
out = args.output.resolve()
out.mkdir(parents=True, exist_ok=True)
shutil.copytree(root / "templates/DotNative.App/Platform", out / "Platform", dirs_exist_ok=True,
                ignore=shutil.ignore_patterns("bin", "obj", "build", ".gradle"))
for file in (out / "Platform").rglob("*"):
    if not file.is_file():
        continue
    try:
        text = file.read_text()
    except UnicodeDecodeError:
        continue
    text = text.replace("AppName", "DotNativeBenchmark").replace("com.example.dotnativeapp", "com.dotnative.benchmark")
    file.write_text(text)
shutil.copy2(root / "benchmarks/DotNative/App.cs", out / "App.cs")
shared = out / "Shared"
shared.mkdir(exist_ok=True)
for file in (root / "benchmarks/Shared").glob("*.cs"):
    shutil.copy2(file, shared / file.name)
project = (root / "templates/DotNative.App/AppName.csproj").read_text()
project = project.replace("<TargetFramework>", f"<DotNativeDevelopment>true</DotNativeDevelopment><DotNativeRepositoryRoot>{root}/</DotNativeRepositoryRoot><TargetFramework>", 1)
project = project.replace("</Project>", f'<Import Project="{root}/eng/DotNative.Development.targets" /></Project>')
(out / "DotNativeBenchmark.csproj").write_text(project)
# NativeAOT code and the Java/ObjC runner must both be Release builds. The
# Android debug key is used only to install this local test artifact.
gradle = out / "Platform/Android/app/build.gradle"
gradle.write_text(gradle.read_text() + '\nandroid { buildTypes { release { debuggable false; signingConfig signingConfigs.debug } } }\n')
manifest = out / "Platform/Android/app/src/main/AndroidManifest.xml"
manifest.write_text(manifest.read_text().replace("</application>", '<profileable android:shell="true" /></application>'))
android = out / "Platform/Android/run.sh"
android.write_text(android.read_text().replace("assembleDebug", "assembleRelease").replace("apk/debug/app-debug.apk", "apk/release/app-release.apk"))
ios = out / "Platform/iOS/run.sh"
ios.write_text(ios.read_text().replace("-configuration Debug", "-configuration Release").replace("Debug-iphonesimulator", "Release-iphonesimulator"))
print(out)
