"""Old/custom runners cannot silently omit declared SDK resources on publish."""
import pathlib
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[2]
with tempfile.TemporaryDirectory(prefix='dotnative-staging-contract-') as directory:
    folder = pathlib.Path(directory)
    project = ET.Element('Project')
    properties = ET.SubElement(project, 'PropertyGroup')
    ET.SubElement(properties, 'RuntimeIdentifier').text = 'iossimulator-arm64'
    ET.SubElement(ET.SubElement(project, 'ItemGroup'), 'DotNativeAppleDependencyManifest', Include='sdk.json')
    ET.SubElement(project, 'Import', Project=str(root / 'packaging/DotNative.App/buildTransitive/DotNative.App.targets'))
    path = folder / 'Fixture.proj'
    ET.ElementTree(project).write(path)
    def run(extra=()):
        return subprocess.run(['dotnet', 'msbuild', str(path), '-nologo', '-target:ValidateDotNativeAppleBundleStaging', *extra], text=True, capture_output=True)
    failed = run()
    assert failed.returncode != 0 and 'DNAPPLE001' in failed.stdout, failed.stdout + failed.stderr
    runner = folder / 'Platform/iOS/Runner.xcodeproj/project.pbxproj'
    runner.parent.mkdir(parents=True)
    runner.write_text((root / 'templates/DotNative.App/Platform/iOS/Runner.xcodeproj/project.pbxproj').read_text())
    good = run()
    assert good.returncode == 0, good.stdout + good.stderr
    runner.unlink()
    external = run(['-p:DotNativeAppleBundleStagingHandled=true'])
    assert external.returncode == 0, external.stdout + external.stderr
print('PASS runner embedding requirement and explicit external packager contract')
