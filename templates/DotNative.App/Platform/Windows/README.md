# Windows

The repository preview uses WinUI 3 controls, scrolling, form controls and a
retained navigation stack with push/Back transitions. Rust owns the native host
window and calls the NativeAOT WinUI renderer through its C ABI.
Install .NET 10, Rust 1.94 (MSVC), C++ Build Tools and Windows SDK, then run:

```sh
dotnet run --project tools/DotNative.Cli -- run windows --hot-reload --project Samples/WidgetSamples
```

The runner builds in Release; the managed host supports UI Hot Reload.
Platform/Windows/*.cs and DOTNATIVE_WINDOWS select platform-specific C# code.
For a standalone NativeAOT managed host, use `--bindings --build-only` instead
of `--hot-reload`. The package still contains the Rust/WinUI renderer and a
separate managed host process; include both when measuring memory. Native plugin
handler support has the limits documented in the desktop preview guide.
See docs/desktop-preview.md in the framework repository for limits and verification.
