"""Build, link and execute native + C# service plugins on Windows/Linux.
Run Linux under xvfb-run. Windows needs a Visual Studio developer environment.
"""
import contextlib, gc, os, pathlib, shutil, subprocess, sys, tempfile, time, xml.etree.ElementTree as ET
root = pathlib.Path(__file__).resolve().parents[2]
platform = 'windows' if sys.platform == 'win32' else 'linux'
if sys.platform not in ('win32', 'linux'): raise SystemExit('Windows/Linux only')
def run(*args, **kwargs): subprocess.run(list(map(str,args)), cwd=root, check=True, **kwargs)
@contextlib.contextmanager
def workspace():
    temporary=tempfile.mkdtemp(prefix='dotnative-plugin-')
    try: yield temporary
    finally:
        # Windows can retain an executable mapping briefly after process exit.
        # Do not ignore persistent locks or leave running children behind.
        for attempt in range(50):
            try: shutil.rmtree(temporary); break
            except PermissionError:
                if attempt==49: raise
                time.sleep(.1)
with workspace() as temporary:
    work = pathlib.Path(temporary)
    app = work/'app'
    run(sys.executable, root/'tests/native/stage-plugin-app.py', app, '--managed')
    cli = root/'tools/DotNative.Cli/bin/Release/net10.0/DotNative.Cli.dll'
    run('dotnet', 'build', root/'tools/DotNative.Cli', '-c', 'Release')
    run('dotnet', cli, 'plugin', 'build', '--project', app, '--platform', platform)
    archive = app/'obj/DotNativePlugin'/platform/'native-plugins'/('dotnative_plugins.lib' if platform == 'windows' else 'libdotnative_plugins.a')
    run('cargo', 'build', '--release', '--locked', '--manifest-path', root/'native/core/Cargo.toml', '--features', 'desktop,dev-host', '--bin', 'dotnative-desktop', env=dict(os.environ, DOTNATIVE_PLUGIN_ARCHIVE=str(archive)))
    if platform == 'windows':
        run('dotnet', 'publish', root/'native/winui/DotNative.WinUI.csproj', '-c', 'Release',
            '-r', 'win-x64', '-o', pathlib.Path(os.environ.get('CARGO_TARGET_DIR', root/'native/core/target'))/'release')
    props = ET.Element('Project'); items = ET.SubElement(props, 'ItemGroup')
    for name in ('NativeProbe', 'ManagedProbe'):
        ET.SubElement(items,'ProjectReference',Include=str(root/'tests/native/plugins'/name/(name+'.csproj')))
    ET.ElementTree(props).write(work/'plugins.props')
    hostdir = work/'host'
    run('dotnet','publish',root/'tools/DotNative.DevHost','-c','Release','-p:PublishAot=true',
        f'-p:DotNativeBindingsPlatform={platform}', f'-p:DotNativeApp={app}', '-p:DotNativeStartupType=InteractionApp.Program',
        f'-p:DotNativePlatform={platform.upper()}', f'-p:DotNativePreviewPlugins={work / "plugins.props"}', '-o',hostdir)
    suffix = '.exe' if platform == 'windows' else ''
    ready = work/'ready'
    with (work/'runtime.log').open('w') as log:
        host=subprocess.Popen([str(hostdir/('DotNative.DevHost'+suffix)),platform.capitalize(),'--once'],cwd=root,stdout=log,stderr=log,env=dict(os.environ,DotNativePreviewReadyFile=str(ready)))
        renderer=None
        try:
            until=time.monotonic()+30
            while not ready.exists():
                if host.poll() is not None or time.monotonic()>until: raise RuntimeError('Plugin host did not start')
                time.sleep(.1)
            renderer=subprocess.Popen([str(pathlib.Path(os.environ.get('CARGO_TARGET_DIR',root/'native/core/target'))/'release'/('dotnative-desktop'+suffix))],cwd=root,stdout=log,stderr=log)
            until=time.monotonic()+30
            while time.monotonic()<until:
                text=(work/'runtime.log').read_text(errors='replace')
                if 'PLUGIN_RESULT PASS' in text and ' | ' in text: print(text);break
                if 'PLUGIN_RESULT FAIL' in text or host.poll() is not None: raise RuntimeError(text)
                time.sleep(.1)
            else: raise RuntimeError('Plugin timeout:\n'+text)
        finally:
            if renderer: renderer.terminate();renderer.wait(timeout=10)
            host.terminate();host.wait(timeout=10)
            # Releasing Popen objects closes Windows process handles before the
            # temporary directory removes the NativeAOT executable.
            renderer=None;host=None
            gc.collect()
