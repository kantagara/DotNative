# Plugin channels

DotNative channels are a separate binary transport from the UI command buffer. A service plugin is a C# package plus native source; it does not add node kinds to the renderer. Visual plugins still require a separate view-factory API.

The app registers the package's public service through ordinary .NET DI. The plugin obtains a `MethodChannel` with a readable name, such as `channels.Get("dotnative.file-picker")`. Swift and Kotlin use `NativeChannels.channel("dotnative.file-picker")`; C++ uses `registrar.Channel("dotnative.file-picker")`. Use the same name and version on every side; version defaults to 1. The SDK derives the transport ID internally from SHA-256 of UTF-8 `name/vN`. No csproj channel declaration or generated channel files are needed.

## Wire format

All integer fields are little-endian. Requests contain an eight-byte positive call ID, four-byte channel ID, four-byte operation kind, and encoded payload. Kind 1 invokes a method with a map containing `method` and `arguments`. Kind 2 cancels a call. Internal kind 3 resets the native session; registrations survive on Apple platforms while resources are released. Android detaches registrations when its Activity session ends.

Replies contain the call ID, four-byte status and encoded value. Status 0 succeeds, 1 returns an error map (`code`, `message`), and 2 cancels. Each call has one terminal result; late replies are ignored. At most 256 calls are pending. Rust bounds its reply queue and converts pressure into a `busy` error rather than waiting forever.

The codec supports null, booleans, signed Int64, strict UTF-8 strings, byte arrays, string-keyed maps and lists. Tags are 0=null, 1=false, 2=true, 3=integer, 4=string, 5=bytes, 6=map, 7=list. Strings/bytes and collections carry four-byte lengths. Maps encode key/value pairs. Maximum payload is 1 MiB, nesting is limited to 16, collections to 4096 elements. JSON and reflection are not part of the runtime protocol.

## Thread and memory ownership

C# queues calls through its UI dispatcher. Native dispatch borrows bytes for the duration of the C ABI callback. Swift/Kotlin copy those bytes before asynchronous work. Native replies are copied into Rust's queue and polled on the C# UI thread. Native handles never become managed pointers.

Native input callbacks establish DotNative's `DispatcherSynchronizationContext`. Therefore an ordinary `await` inside a button/input callback resumes on the app's UI dispatcher. `ConfigureAwait(false)` or `Task.Run` deliberately leaves that context; explicitly post state changes back in those cases. Release uses the OS UI thread; remote preview uses the managed host's dedicated UI thread.

A resource-producing operation needs more than dropping a late reply. FilePicker uses an operation-scoped `cancelPick` method and continues receiving the original result, releasing a selected file if success races cancellation. Native resources are also released on session reset/disconnect. Reads keep their response even if cancellation arrives in flight, preserving stream position.

## Release and development

Release uses generated `LibraryImport` calls into a small C ABI: send, poll, register, reply and reset. Native implementations register one callback per channel. Android has one generic JNI bridge; plugin methods need no individual JNI exports.

Remote preview carries requests in envelope 101 and replies in envelope 7 over the existing development connection. The native device presents the dialog and reads files. Direct native imports remain local/release-only. No file is implicitly copied to disk on the development host.

DotNative.App ships a C# native build tool. Plugin `buildTransitive` targets contribute source paths and registration entries; the build compiles Swift into link inputs or stages Kotlin for Gradle. The repository CLI performs the equivalent preview build from project references. Its NuGet dependency discovery is not yet generalized.

The first implementation is `plugins/DotNative.FilePicker`. The next contract-generator layer can emit strongly typed methods on top of this transport; it must preserve wire-version compatibility and ownership rules.

## iOS notifications and APNs registration

The Apple plugin SDK owns one `UNUserNotificationCenterDelegate` multiplexer.
Register a plugin delegate with `NativeNotifications.register("vendor.plugin", delegate: handler)`;
remove it with `unregister`. Registration by ID replaces only that plugin. Plugins
must not assign `UNUserNotificationCenter.current().delegate` themselves. The
multiplexer preserves a delegate that the app installed before registration.
It cannot prevent an unrelated SDK from replacing the global delegate afterwards.

Callbacks are forwarded on the main actor. Foreground presentation options are
unioned; the OS completion is called once after all subscribers reply, or after a
five-second deadline. Duplicate/late replies do not finish the OS callback again.
Plugins must filter their notifications (for example, remote push versus their
own local notification identifiers). A Firebase subscriber must not treat local
notification taps as remote messages. Delegate registrations are process-scoped,
so callbacks remain available while the app is backgrounded.

The iOS runner forwards APNs registration success/failure through
`dotnative_apns_registered` / `dotnative_apns_failed`. Subscribe using
`NativeNotifications.observeAPNs(id, registered:failed:)`; remove using
`removeAPNsObserver`. Late subscribers receive the last device token. Plugins do
not need to swizzle AppDelegate registration methods. This API covers token
registration, not background-fetch/silent-push processing.

Existing custom iOS runners must add these forwarding methods from the current
runner template before using plugins migrated to this API. Rebuild the runtime
and plugins together; this is not compatible with an older Apple plugin SDK.
