# DotNative single-project template

On Apple Silicon, install the Rust iOS, iOS Simulator, Mac, and Android targets,
Xcode, .NET SDK 9 or newer, Android SDK 36 and NDK 29.0.14206865, then package:

```sh
rustup target add aarch64-apple-ios aarch64-apple-ios-sim aarch64-apple-darwin aarch64-linux-android
bash scripts/pack-local.sh
dotnet new uninstall DotNative.Templates  # if an older version is installed
dotnet new install ./bin/packages/DotNative.Templates.0.1.0-beta.1.nupkg
dotnet new dotnative -n MyApp -o MyApp --bundleId com.example.myapp
```

Configure the generated app's NuGet sources to include this repository's absolute
bin/packages path and nuget.org. No packages are published by these scripts.
Generated apps consume the prebuilt Rust archives and do not need Rust installed.

The generated app has one MyApp.csproj, root App.cs, and optional native projects
under Platform/iOS, Platform/Android, Platform/Mac, and Platform/Windows.
See Samples/Counter for the working counter. Repository samples use source references;
see Samples/README.md for the development workflow.

From the generated directory, with the corresponding simulator/emulator booted:

```sh
dotnet run
dotnet run -r osx-arm64
dotnet run -r linux-bionic-arm64 -p:DisableUnsupportedError=true
```

The Mac runner creates a real AppKit app. Android uses the experimental .NET 9
Bionic NativeAOT runtime and JNI, plus Gradle for APK packaging. Only ARM64 targets
are currently packaged. Windows is not implemented. iOS device signing remains
an Xcode configuration step. These are prototype runners, not an app-store release pipeline.

DotNative.App supplies C# bootstrap and linking integration and transitively
references DotNative, the platform native archives, and DotNative.Essentials.
Essentials bundles the separate DeviceInfo module. Native UIKit/AppKit/Android
buttons enqueue stable IDs; C# state changes flow through the binary diff protocol.

Bump versions before redistributing changed packages: NuGet caches by version.
The package validator checks the native import paths and a single generated csproj.

References: [NativeAOT interop](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop),
[Android Bionic NativeAOT](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/docs/android-bionic.md).
