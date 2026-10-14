"""Build the real quickstart managed graph without invoking release platform tooling."""
import os
import pathlib
import subprocess
import tempfile

root = pathlib.Path(__file__).resolve().parents[2]
with tempfile.TemporaryDirectory(prefix="dotnative-preview-build-") as output:
    env = os.environ.copy()
    env.update({
        "DotNativeApp": str(root / "Samples/Counter"),
        "DotNativeDevelopment": "true",
        "DotNativeSourceRoot": str(root),
        "DotNativePreviewAppProject": str(root / "Samples/Counter/Counter.csproj"),
        "DotNativePreviewAppAssembly": "Counter",
        "DirectoryBuildPropsPath": str(root / "eng/DotNative.Preview.props"),
        "DirectoryBuildTargetsPath": str(root / "eng/DotNative.Preview.targets"),
        "DotNativeStartupType": "Counter.Program",
        "DotNativePlatform": "MACOS",
        "DotNativePreviewBuildRoot": output,
    })
    subprocess.run(["dotnet", "build", str(root / "tools/DotNative.DevHost"),
                    "--property:DotNativeDevelopment=true"], env=env, check=True)
    assert list(pathlib.Path(output).rglob("Counter.dll")), "Counter assembly missing"
print("PASS managed Counter graph builds without native release targets")
