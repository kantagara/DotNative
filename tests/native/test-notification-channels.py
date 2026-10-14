"""Run notification completion/APNs tests and typecheck the Apple runtime SDK."""
import pathlib
import subprocess
import tempfile

root = pathlib.Path(__file__).resolve().parents[2]
swift = root / 'native/plugins/apple'
with tempfile.TemporaryDirectory(prefix='dotnative-notifications-') as directory:
    binary = pathlib.Path(directory) / 'notification-tests'
    subprocess.run(['xcrun', 'swiftc', '-swift-version', '5', '-parse-as-library',
                    str(swift / 'NotificationCompletion.swift'), str(swift / 'APNsObservers.swift'),
                    str(root / 'tests/native/plugins/notification-completion-test.swift'), '-o', str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
    for sdk, triple in [('iphonesimulator', 'arm64-apple-ios16.0-simulator'), ('iphoneos', 'arm64-apple-ios16.0')]:
        sysroot = subprocess.check_output(['xcrun', '--sdk', sdk, '--show-sdk-path'], text=True).strip()
        subprocess.run(['xcrun', 'swiftc', '-swift-version', '5', '-parse-as-library', '-typecheck',
                        '-target', triple, '-sdk', sysroot, *map(str, sorted(swift.glob('*.swift')))], check=True)
print('PASS Apple notification runtime: host tests and device/simulator typecheck')
