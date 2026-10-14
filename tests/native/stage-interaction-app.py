"""Stage the shared C# fixture with fresh production runners; no sample is modified."""
import pathlib, shutil, sys
root = pathlib.Path(__file__).resolve().parents[2]
out = pathlib.Path(sys.argv[1]).resolve()
out.mkdir(parents=True, exist_ok=True)
for name in ("InteractionApp.csproj", "App.cs"):
    shutil.copy2(root / "tests/native/InteractionApp" / name, out / name)
project = out / "InteractionApp.csproj"
project.write_text(project.read_text().replace("../../../src/DotNative/DotNative.csproj", str(root / "src/DotNative/DotNative.csproj")))
for platform in ("Android", "iOS", "Mac"):
    shutil.copytree(root / "templates/DotNative.App/Platform" / platform,
                    out / "Platform" / platform, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns("bin", "obj", "build", ".gradle", "artifacts"))
dest = out / "Platform/Android/app/src/androidTest/java/com/dotnative/runtime"
dest.mkdir(parents=True, exist_ok=True)
shutil.copy2(root / "tests/native/InteractionInputTest.java", dest)
print(out)
