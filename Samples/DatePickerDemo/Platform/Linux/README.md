# Linux

The repository preview uses GTK 4.8+ controls, scrolling, form controls and a native
header with Back. Install .NET 10, Rust 1.94, GTK development libraries and pkg-config:

```sh
sudo apt-get install build-essential pkg-config libgtk-4-dev
dotnet run --project tools/DotNative.Cli -- run linux --hot-reload --project Samples/WidgetSamples
```

The runner builds in Release; the managed host supports UI Hot Reload.
Platform/Linux/*.cs and DOTNATIVE_LINUX select platform-specific C# code.
Linux presents the active screen; retained-page transitions, native plugin handlers
and standalone NativeAOT packaging are not implemented. See docs/desktop-preview.md.
