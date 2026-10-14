"""Regression: a plugin-free app must link the native runner's registrar call."""
import pathlib
import subprocess
import tempfile

root = pathlib.Path(__file__).resolve().parents[2]
subprocess.run(["dotnet", "build", str(root / "tools/DotNative.NativeBuild")], check=True)
builder = root / "tools/DotNative.NativeBuild/bin/Debug/net9.0/DotNative.NativeBuild.dll"
with tempfile.TemporaryDirectory(prefix="dotnative-empty-registration-") as directory:
    output = pathlib.Path(directory)
    subprocess.run(["dotnet", str(builder), str(output), "osx-arm64", "13.0", str(root / "native/plugins")], check=True)
    main = output / "main.c"
    main.write_text("extern void dotnative_register_plugins(void);\nextern void dotnative_apns_registered(const unsigned char *, long);\nextern void dotnative_apns_failed(const char *);\nint main(void) { dotnative_register_plugins(); dotnative_apns_registered(0, 0); dotnative_apns_failed(0); return 0; }\n")
    executable = output / "registration-test"
    subprocess.run(["xcrun", "clang", str(main), "@" + str(output / "plugins.link.rsp"), "-o", str(executable)], check=True)
    subprocess.run([str(executable)], check=True)
print("PASS plugin-free Apple registration compiles, links and runs")
