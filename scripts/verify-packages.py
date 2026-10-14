"""Verify the NuGet file layout that dotnet new and MSBuild actually consume."""
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile

repo = Path(__file__).resolve().parents[1]
version = ET.parse(repo / 'src/DotNative/DotNative.csproj').findtext('.//Version')
feed = repo / 'bin/packages'
expected = {
    'DotNative': {'lib/net9.0/DotNative.dll', 'README.md'},
    'DotNative.Native.iOS': {
        'buildTransitive/DotNative.Native.iOS.targets',
        'runtimes/ios-arm64/native/libcore.a',
        'runtimes/iossimulator-arm64/native/libcore.a', 'README.md',
    },
    'DotNative.Native.Mac': {'buildTransitive/DotNative.Native.Mac.targets','runtimes/osx-arm64/native/libcore.a'},
    'DotNative.Native.Android': {'buildTransitive/DotNative.Native.Android.targets','runtimes/linux-bionic-arm64/native/libcore.a'},
    'DotNative.DeviceInfo': {'lib/net9.0/DotNative.DeviceInfo.dll', 'native/ios/device_info.m', 'buildTransitive/DotNative.DeviceInfo.targets', 'README.md'},
    'DotNative.Essentials': {'README.md'},
    'DotNative.App': {'buildTransitive/DotNative.App.targets', 'tools/AppEntry.cs', 'tools/build-ios-plugins.py', 'README.md'},
    'DotNative.Templates': {
        'content/DotNative.App/.template.config/template.json',
        'content/DotNative.App/AppName.csproj',
        'content/DotNative.App/App.cs',
        'content/DotNative.App/Program.cs',
        'content/DotNative.App/Platform/iOS/Runner.xcodeproj/project.pbxproj',
        'content/DotNative.App/Platform/iOS/run.sh', 'README.md',
    },
}
for name, required in expected.items():
    package_version = ET.parse(repo / 'templates/DotNative.Templates.csproj').findtext('.//Version') if name == 'DotNative.Templates' else version
    with zipfile.ZipFile(feed / f'{name}.{package_version}.nupkg') as package:
        files = set(package.namelist())
        assert required <= files, f'{name}: missing {required - files}'
        assert not any(any(f'/{part}/' in p for part in ('bin', 'obj', 'artifacts', 'xcuserdata', '.gradle')) for p in files)
        if name in ('DotNative.App', 'DotNative.Essentials'):
            nuspec = ET.fromstring(package.read(name + '.nuspec'))
            deps = {d.attrib['id'] for d in nuspec.iter() if d.tag.endswith('dependency')}
            expected_dep = 'DotNative.Essentials' if name == 'DotNative.App' else 'DotNative.DeviceInfo'
            assert expected_dep in deps, f'{name} missing bundled dependency {expected_dep}'
        if name == 'DotNative.Templates':
            assert not any(p.startswith('lib/') for p in files)
            assert sum(p.endswith('.csproj') for p in files) == 1
            assert not any(p.endswith('.sln') or p.endswith('Bootstrap.cs') for p in files)
            assert sum(p.endswith('/.template.config/template.json') for p in files) == 1
    print(f'PASS {name}: package structure')
