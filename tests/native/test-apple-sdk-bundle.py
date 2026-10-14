"""Build an unsigned iOS app with real declared SDKs and verify packaged resources.
No app is launched and no notifications/ad/network service operations are invoked.
Usage: python3 tests/native/test-apple-sdk-bundle.py [iossimulator-arm64|ios-arm64]
Requires the sibling Firebase, GoogleMobileAds and LocalNotifications source repos.
"""
import json
import os
import pathlib
import plistlib
import shutil
import subprocess
import sys
import tempfile

root = pathlib.Path(__file__).resolve().parents[2]
rid = sys.argv[1] if len(sys.argv) > 1 else 'iossimulator-arm64'
sdk = {'iossimulator-arm64': 'iphonesimulator', 'ios-arm64': 'iphoneos'}[rid]
work = pathlib.Path(tempfile.mkdtemp(prefix='dotnative-sdk-bundle-'))
output = work / 'native'
output.mkdir()
plugins = [root.parent / ('DotNative.' + name) for name in ['Firebase', 'GoogleMobileAds', 'LocalNotifications']]
(output / 'plugin-sources.txt').write_text('\n'.join(str(p) for plugin in plugins for p in (plugin / 'Platform/iOS').glob('*.swift')))
(output / 'plugin-entries.txt').write_text('FirebasePlugin\nMobileAdsPlugin\nLocalNotificationsPlugin\n')
(output / 'plugin-apple-manifests.txt').write_text('\n'.join(str(plugin / 'Platform/iOS/apple-dependencies.json') for plugin in plugins[:2]))
subprocess.run(['dotnet', 'build', str(root / 'tools/DotNative.NativeBuild'), '-v', 'quiet'], check=True)
builder = root / 'tools/DotNative.NativeBuild/bin/Debug/net9.0/DotNative.NativeBuild.dll'
subprocess.run(['dotnet', str(builder), str(output), rid, '16.0', str(root / 'native/plugins')], check=True)
project = work / 'project'
shutil.copytree(root / 'templates/DotNative.App/Platform/iOS', project)
# Keep the real resource phase. Replace only the NativeAOT publish script with a
# no-op; this fixture links the native plugin object and real SDKs directly.
(project / 'build-managed.sh').write_text('#!/bin/bash\nexit 0\n')
(project / 'Runner/main.m').write_text('''#import <UIKit/UIKit.h>
#include <stdint.h>
int32_t dotnative_register_channel(uint32_t id, void *callback) { return 0; }
int32_t dotnative_plugin_reply(const void *bytes, intptr_t count) { return 0; }
int32_t dotnative_register_platform_view(const char *name, intptr_t count, void *create, void *detach, void *rebind, void *context) { return 0; }
@interface FixtureDelegate : UIResponder <UIApplicationDelegate>
@end
@implementation FixtureDelegate
@end
int main(int argc, char **argv) { @autoreleasepool { return UIApplicationMain(argc, argv, nil, @"FixtureDelegate"); } }
''')
link = '@"' + str(output / 'plugins.link.rsp') + '" -framework UIKit -framework Foundation -framework QuartzCore -framework CoreGraphics -framework CoreText -lc++ -lsqlite3 -lz -L/usr/lib/swift'
subprocess.run(['xcodebuild', '-project', str(project / 'Runner.xcodeproj'), '-scheme', 'Runner', '-configuration', 'Debug', '-sdk', sdk,
                '-derivedDataPath', str(work / 'xcode'), 'CODE_SIGNING_ALLOWED=NO', 'DOTNATIVE_OUT=' + str(output), 'OTHER_LDFLAGS=' + link, 'build'], check=True)
app = work / ('xcode/Build/Products/Debug-' + sdk + '/AppName.app')
plan = json.loads((output / 'apple-dependencies.json').read_text())
assert plan['bundleInputs'], 'Real SDKs did not contribute any resource bundles'
for resource in plan['bundleInputs']:
    assert (app / resource['destination']).is_dir(), resource
    for source in pathlib.Path(resource['source']).rglob('PrivacyInfo.xcprivacy'):
        copied = app / resource['destination'] / source.relative_to(resource['source'])
        assert copied.read_bytes() == source.read_bytes(), copied
        plistlib.loads(copied.read_bytes())
subprocess.run(['codesign', '--force', '--sign', '-', str(app)], check=True)
subprocess.run(['codesign', '--verify', '--deep', '--strict', str(app)], check=True)
print('PASS real SDK Xcode link, resources/privacy manifests and ad-hoc bundle signature:', app)
print('Resource bundles:', len(plan['bundleInputs']))
