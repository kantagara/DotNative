"""Local consumer check: install the tool and build a plugin outside the checkout."""
import os
import pathlib
import platform
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path(__file__).resolve().parents[2]
version = ET.parse(root / "tools/DotNative.Cli/DotNative.Cli.csproj").findtext(".//Version")
with tempfile.TemporaryDirectory(prefix="dotnative-cli-consumer-") as temporary:
    work = pathlib.Path(temporary)
    feed, tools, plugin = work / "feed", work / "tools", work / "Acme.Device"
    subprocess.run(["dotnet", "pack", str(root / "tools/DotNative.Cli"), "-c", "Release", "-o", str(feed)], check=True)
    with zipfile.ZipFile(feed / f"DotNative.Cli.{version}.nupkg") as archive:
        files = archive.namelist()
        for path in files:
            if any(part in path.split("/") for part in ("bin", "obj", "target", ".idea", ".git")):
                raise RuntimeError("Unexpected development artifact in package: " + path)
        for required in ("native/core/Cargo.lock", "tools/DotNative.DevHost/DotNative.DevHost.csproj",
                         "templates/DotNative.App/AppName.csproj"):
            if "tools/net10.0/any/sdk/" + required not in files:
                raise RuntimeError("Missing SDK input: " + required)
    environment = dict(os.environ, NUGET_PACKAGES=str(work / "packages"))
    subprocess.run(["dotnet", "tool", "install", "DotNative.Cli", "--version", version,
                    "--tool-path", str(tools), "--add-source", str(feed)], cwd=work, env=environment, check=True)
    environment["PATH"] = str(tools) + os.pathsep + environment["PATH"]
    def run(*arguments):
        subprocess.run(list(arguments), cwd=work, env=environment, check=True)
    run("dotnative", "--help")
    run("dotnative", "plugin", "new", "--name", "Acme.Device", "--output", str(plugin))
    if str(root) in (plugin / "Acme.Device.csproj").read_text():
        raise RuntimeError("Installed tool generated a reference into the checkout")
    run("dotnet", "build", str(plugin / "Acme.Device.csproj"), "-c", "Release")
    if platform.system() == "Darwin":
        run("dotnative", "plugin", "build", "--project", str(plugin), "--platform", "mac")
print("PASS installed dotnative tool and external consumer plugin")
