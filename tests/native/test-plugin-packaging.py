"""Compile and stage first-party native sources using transitive NuGet build rules."""
import json
import pathlib
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[2]


def run(*args, capture=False):
    return subprocess.run(
        list(map(str, args)), cwd=root, check=True, text=True,
        stdout=subprocess.PIPE if capture else None,
    ).stdout


with tempfile.TemporaryDirectory(prefix="dotnative-plugin-package-") as temporary:
    work = pathlib.Path(temporary)
    plugin = work / "NativeService"
    run("dotnet", "run", "--project", root / "tools/DotNative.Cli", "--", "plugin", "new",
        "--output", plugin, "--name", "NativeService")
    consumer = ET.Element("Project")
    ET.SubElement(consumer, "Import", Project=str(plugin / "buildTransitive/NativeService.targets"))
    ET.ElementTree(consumer).write(work / "consumer.proj")
    run("dotnet", "build", root / "tools/DotNative.NativeBuild")
    builder = root / "tools/DotNative.NativeBuild/bin/Debug/net9.0/DotNative.NativeBuild.dll"
    for rid in ("osx-arm64", "linux-bionic-arm64"):
        items = json.loads(run("dotnet", "msbuild", work / "consumer.proj",
            f"-p:RuntimeIdentifier={rid}", "-getItem:DotNativeNativeSource,DotNativePluginEntry",
            capture=True))["Items"]
        output = work / rid
        output.mkdir()
        for item, filename in [("DotNativeNativeSource", "plugin-sources.txt"),
                               ("DotNativePluginEntry", "plugin-entries.txt")]:
            (output / filename).write_text("\n".join(value["Identity"] for value in items[item]))
        run("dotnet", builder, output, rid, "13.0", root / "native/plugins")
        if rid == "osx-arm64":
            assert list((output / "plugin-objects").glob("*.o")), "Native implementation was not compiled"
        else:
            generated = list((output / "plugin-java").rglob("*.kt"))
            names = {f.name for f in generated}
            assert {"PluginChannels.kt", "EventChannels.kt", "GeneratedPlugins.kt"} <= names
            assert "NativeServicePlugin(activity)" in (output / "plugin-java/GeneratedPlugins.kt").read_text()
    print("PASS native transitive packaging: Swift compilation and Kotlin channel staging")
