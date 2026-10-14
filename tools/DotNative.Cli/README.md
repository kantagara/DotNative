# DotNative CLI

Install the .NET tool to get the `dotnative` command. Requires the .NET 10 SDK.
The package includes versioned SDK sources and templates; a repository clone is
not required. Native builds still require Rust and each platform's build tools.
SDK build intermediates live in the user's local application-data directory.

Build and install from a local feed:

```sh
dotnet pack tools/DotNative.Cli -c Release -o artifacts/cli-feed
dotnet tool install --global DotNative.Cli --version 0.1.0-beta.1 \
  --add-source artifacts/cli-feed
dotnative --help
dotnative devices
dotnative plugin new --name Acme.Device --output ./Acme.Device
```

After publishing this version to a configured NuGet feed, the `--add-source`
argument is unnecessary. Packing/installing locally does not publish the package.
For a project-pinned install, create a .NET tool manifest and replace `--global`
with `--local`; run `dotnet tool restore` after cloning that application.

```sh
dotnative run mac --hot-reload --project ./MyApp
dotnative run windows --bindings --project ./MyApp
dotnative run linux --bindings --project ./MyApp
```

Commands retain their existing platform coverage and experimental limitations.
Installation requires the platform workloads and native toolchains for the target.
Running inside a DotNative checkout uses that
checkout's SDK sources, supporting framework development.

## Application runtime

Application builds can select either NativeAOT or a packaged managed .NET
runtime. The native renderer is the same in both cases. Managed builds preserve
assemblies and enable System.Text.Json reflection; they can also reference
reflection-based serializers such as Newtonsoft.Json without requiring the
application to use the framework's serializer.

```sh
dotnative run mac --project ./MyApp --runtime managed
dotnative run ios --project ./MyApp --runtime managed --device <simulator-UDID>
dotnative run android --project ./MyApp --runtime managed --device emulator-5554
dotnative run windows --project ./MyApp --runtime managed
dotnative run linux --project ./MyApp --runtime nativeaot --build-only
```

The project can set the default instead:

```xml
<PropertyGroup>
  <DotNativeRuntime>managed</DotNativeRuntime>
</PropertyGroup>
```

Precedence is `--runtime`, then the evaluated `DotNativeRuntime` property, then
an explicitly configured `PublishAot` property. If none is specified, NativeAOT
remains the default. Outputs for the two runtimes use separate directories.
The native Windows renderer still has its own NativeAOT build; selecting managed
changes the application host, where application dependencies execute.

These deployment paths remain experimental:

| Platform | Managed application runtime | Assembly preservation |
| --- | --- | --- |
| macOS | CoreCLR, packaged by the macOS SDK | `TrimMode=copy` |
| iOS | Mono; interpreter enabled for dynamic managed code | `TrimMode=copy` |
| Android | Mono, packaged by the Android SDK | Trimming and profiled AOT disabled |
| Windows / Linux | Self-contained CoreCLR host beside the renderer | `PublishTrimmed=false` |

Apple and Android builds currently target ARM64 and require an Apple Silicon
build host with the .NET 10 platform workloads. They use an in-process callback
bridge, not the hot-reload server. The application starts after the .NET runtime
registers a versioned callback table with Rust; UI, channels, lifecycle and
accessibility continue through the existing native renderer. NativeAOT builds
retain their direct C entry points.

For a connected physical iPhone, select its ID from `dotnative devices` and
supply signing configuration:

```sh
dotnative run ios --project ./MyApp --runtime managed \
  --device <device-ID> --team <Apple-Team-ID>
```

`DOTNATIVE_APPLE_TEAM` can supply the team instead. The Apple SDK handles
provisioning and signing. Simulator and device builds are separate targets;
iOS uses Mono/interpreter rather than unrestricted native JIT. Hot reload
remains a separate development command.

Build-only checks produced Counter managed bundles for macOS and iOS Simulator.
Physical iPhone signing and on-device startup have not been validated. A
packaged application's Newtonsoft.Json usage has not been validated yet.

## Preview updates

The managed preview references and builds the application's `.csproj`. It loads
its compiled entry point instead of compiling application source inside DevHost.
Compiler settings, managed resources and project/package references remain part
of the application's build. Preview outputs are isolated from release outputs;
the preview disables AOT and preserves the project's Directory.Build imports.
During design-time evaluation, outer multi-target nodes retain build dependencies
but do not claim assembly references. Their inner target-framework nodes retain
normal compiler references, so the SDK watcher can resolve the metadata graph.


`run --hot-reload` supervises its managed host and native renderer:

- If the SDK reports unmatched project/metadata references, the supervisor announces a rebuild fallback and relaunches with hot reload disabled. Subsequent source edits rebuild and relaunch the preview rather than depending on inconsistent metadata. The original SDK warnings remain visible. Restart the CLI to retry hot reload after correcting the graph or updating the SDK.
- Ordinary C# edits with a consistent metadata graph use `dotnet watch` hot reload and retain the running application's state.
- Unsupported managed edits restart the host automatically. The supervisor then rebuilds/relaunches the native preview so it reconnects to the new host.
- Changes to native sources, SDK templates, resources, project files, build configuration and referenced project inputs trigger a rebuild after saves settle. Generated `bin`, `obj`, `target`, `build` and package/tool caches are excluded.
- Each launch has a build ID. `[preview] Displayed build …, managed update …` is emitted only after the native renderer acknowledges the corresponding command frame. Compilation or connection alone does not mark a revision displayed.
- A failed session gets one recovery attempt per source revision. Repeated failure or a failed build waits for a source edit, keeping diagnostics visible instead of repeatedly restarting. Ctrl+C stops owned processes.

A restart recreates in-memory application state. Native build tools remain required.
The supervisor watches the application, resolved project-reference directories and
SDK inputs. NuGet native inputs are rediscovered after restoring changed project
configuration; installed packages are treated as immutable.

## Native build reuse

Swift plugin objects, desktop plugin archives and signed Apple preview bundles
are reused only when source contents, compiler identity, SDK inputs, build
configuration and output contents match their recorded fingerprints. Removed
sources invalidate the cache. Failed builds never update its stamp. Cargo,
Gradle and Xcode retain their own incremental build behavior. Android staging
preserves unchanged inputs and removes obsolete plugin sources. Device installs
are skipped only when the same product is recorded and the app is still installed.

Delete the app's preview output directory to force a complete native rebuild.
Managed source changes continue to use hot reload. Restart diagnostics include
the SDK reason or edited file and explicitly report that in-memory state resets.
Expected termination of owned processes is not reported as a crash; compiler,
signing, connection and unexpected simulator failures remain visible.

## Develop a plugin

```sh
dotnative plugin new --name Acme.Device --output ./Acme.Device
dotnative plugin run ios --project ./Acme.Device
dotnative plugin run android --project ./Acme.Device --device emulator-5554
```

Every new scaffold includes `example/`, a service registered with `Add...`,
constructor injection and a button calling its native API. `plugin run` forwards
the selected device/team to the ordinary app runner. Managed native scaffolds
select bindings mode, with the same platform limitations as `run --bindings`.
Plugin sources exclude the example from compilation and packaging. Git ignores
`dotnative.local.props`; copied projects use the versioned package reference or
a developer's own source override.

## Portable dependencies

`dotnative restore --project ./MyApp` reads `dotnative.dependencies.props` and
clones its HTTPS `DotNativeDependency` repositories at their full pinned commit
SHA into `.dotnative/dependencies/`. Existing checkouts at another revision are
reported for manual resolution; the command never resets or overwrites local
work. It then restores the application's NuGet dependencies.

Keep dependency roots relative to that file. Import an ignored
`dotnative.local.props` for optional development checkouts. Both installed CLI
payloads and a source CLI invoked from the app directory resolve their SDK root.

## Apple preview Info.plist

An app may provide `Platform/iOS/Info.plist` or `Platform/Mac/Info.plist` as a
plist dictionary overlay. The preview merges its entries into the generated
bundle plist, including camera/photo permission descriptions and URL schemes.
Bundle identity, executable and version keys remain generated by the CLI;
overriding them produces a diagnostic. Changes to the overlay rebuild the
native preview bundle.


Dependency manifests accept published version tags such as `Revision="v0.1.0-preview.1"`
as well as legacy 40-character commit IDs. `dotnative restore --project <path>` resolves
lightweight and annotated tags from the HTTPS Git repository and checks out their
commit. Existing checkouts must match the resolved version; local changes are preserved.
Missing tags and mismatched existing checkouts stop restoration rather than silently
switching dependency versions. GitHub release `.nupkg` assets are separate from this
source-tag restoration workflow.
