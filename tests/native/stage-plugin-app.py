"""Stage plugin fixtures with fresh native runners. Output is a disposable test app."""
import argparse, pathlib, shutil, subprocess, sys, xml.etree.ElementTree as ET
root = pathlib.Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('output', type=pathlib.Path)
parser.add_argument('--managed', action='store_true')
args = parser.parse_args()
out = args.output.resolve()
if out.exists() and any(out.iterdir()): raise SystemExit('Use an empty output directory.')
out.mkdir(parents=True, exist_ok=True)
for platform in ('Android', 'iOS', 'Mac'):
    shutil.copytree(root / 'templates/DotNative.App/Platform' / platform, out / 'Platform' / platform,
                   ignore=shutil.ignore_patterns('bin', 'obj', 'build', '.gradle', 'artifacts'))
project = ET.Element('Project', Sdk='Microsoft.NET.Sdk')
properties = ET.SubElement(project, 'PropertyGroup')
ET.SubElement(properties, 'TargetFramework').text = 'net10.0'
ET.SubElement(properties, 'RootNamespace').text = 'InteractionApp'
ET.SubElement(properties, 'Nullable').text = 'enable'
ET.SubElement(properties, 'ImplicitUsings').text = 'enable'
refs = ET.SubElement(project, 'ItemGroup')
ET.SubElement(refs, 'ProjectReference', Include=str(root/'src/DotNative/DotNative.csproj'))
ET.SubElement(refs, 'ProjectReference', Include=str(root/'tests/native/plugins/NativeProbe/NativeProbe.csproj'))
text = (root/'tests/native/PluginApp/App.cs').read_text()
if args.managed:
    ET.SubElement(refs, 'ProjectReference', Include=str(root/'tests/native/plugins/ManagedProbe/ManagedProbe.csproj'))
    text = 'using ManagedProbe;\n' + text.replace('.UseNativeProbe()', '.UseNativeProbe().UseManagedProbe()').replace('GetRequiredService<NativeService>', 'GetRequiredService<NativeProbe.NativeService>').replace('"PASS " + first', '"PASS " + first + " | " + app.Services.GetRequiredService<ManagedProbe.NativeService>().Read()')
(out/'App.cs').write_text(text)
ET.indent(project)
ET.ElementTree(project).write(out/'InteractionApp.csproj', encoding='unicode')
print(out)
