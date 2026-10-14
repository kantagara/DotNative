# Native plugin packages

Each package provides a C# API and a DotNative native or managed implementation
in its own repository. Source and metadata use DotNative channels.

| Package | Current support |
| --- | --- |
| [DotNative.Paths](https://github.com/kantagara/DotNative.Paths) | Public; desktop implementation |
| [DotNative.Preferences](https://github.com/kantagara/DotNative.Preferences) | Public; desktop implementation |
| [DotNative.Launcher](https://github.com/kantagara/DotNative.Launcher) | Public; desktop implementation |
| [DotNative.ImagePicker](https://github.com/kantagara/DotNative.ImagePicker) | Public; wraps FilePicker and validates raster extensions |
| [DotNative.SecureStorage](https://github.com/kantagara/DotNative.SecureStorage) | Public; desktop implementation |
| [DotNative.Permissions](https://github.com/kantagara/DotNative.Permissions) | Public; Android/iOS camera, microphone, location, notifications |
| [DotNative.Connectivity](https://github.com/kantagara/DotNative.Connectivity) | Public; desktop implementation |
| [DotNative.DeviceInfo](https://github.com/kantagara/DotNative.DeviceInfo) | Public; iOS major version only |
| [DotNative.PackageInfo](https://github.com/kantagara/DotNative.PackageInfo) | Public; managed assembly metadata |
| [DotNative.WebView](https://github.com/kantagara/DotNative.WebView) | Public; Android, iOS, macOS top-level surface |
| [DotNative.InAppWebView](https://github.com/kantagara/DotNative.InAppWebView) | Public; Android, iOS, macOS top-level surface with cookies and HTML loading |
| [DotNative.Camera](https://github.com/kantagara/DotNative.Camera) | Public; Android/iOS one-shot photo capture |
| [DotNative.Geolocator](https://github.com/kantagara/DotNative.Geolocator) | Public; Android/iOS one-shot foreground location |
| [DotNative.LocalAuth](https://github.com/kantagara/DotNative.LocalAuth) | Public; Android device credential, iOS/macOS LocalAuthentication |
| [DotNative.Share](https://github.com/kantagara/DotNative.Share) | Public; Android, iOS, macOS text sharing |
| [DotNative.FilePicker](https://github.com/kantagara/DotNative.FilePicker) | Public; five-platform native picker |
| [DotNative.VideoPlayer](https://github.com/kantagara/DotNative.VideoPlayer) | Public; Android/iOS top-level playback |
| [DotNative.LocalNotifications](https://github.com/kantagara/DotNative.LocalNotifications) | Public; Android/iOS immediate text notifications |
| [DotNative.LocalDatabase](https://github.com/kantagara/DotNative.LocalDatabase) | Public; desktop SQLite |
| [DotNative.Sensors](https://github.com/kantagara/DotNative.Sensors) | Public; Android/iOS accelerometer and gyroscope streams |
| [DotNative.Cognito](https://github.com/kantagara/DotNative.Cognito) | Public; Cognito hosted UI, PKCE and token exchange. iOS authentication session, Android browser redirect |

Native view embedding remains a separate renderer milestone. Current WebView
and video packages use top-level surfaces; camera supports one-shot capture.
See each repository for platform limits, permissions and build evidence.

## Automatic application initialization

A configured plugin may register an `IApplicationInitializer` using
`TryAddEnumerable(ServiceDescriptor.Singleton<IApplicationInitializer, ...>())`.
`Build()` resolves the initializer graph. `Run<T>()` mounts the first frame and
starts each initializer once without blocking the native UI event loop. Each
receives the application's stopping token. A failed initializer is logged and
does not prevent the other initializers from starting; the aggregate completion
and error state remain available through `DotNativeApplication.Initialization`.

`DisposeAsync()` cancels startup and awaits completion before disposing the
service provider. Synchronous disposal rejects a still-running initializer, so
async work cannot use already-disposed dependencies. Configured Firebase and
AdMob registration use this lifecycle; consumers need no manual service lookup
or SDK startup block.
