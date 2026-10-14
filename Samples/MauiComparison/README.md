# MAUI comparison sample

A separate Windows-only .NET MAUI app for comparing the interaction and navigation of WidgetSamples. It does not reference DotNative or change its renderer. UI and Windows startup are written in C#.

Includes the shared cart, per-visit DI scope and note, native NavigationPage push/back, system/light/dark theme selection, entry and focus controls, switch, slider, 30-row CollectionView inside the main ScrollView, and the MAUI file picker with cancellation/error handling.

Build on Windows with the .NET 10 SDK and `maui-windows` workload:

```powershell
dotnet workload install maui-windows
dotnet publish MauiComparison.csproj -c Release -r win-arm64 -o publish
.\publish\MauiComparison.exe
```

The published directory includes the .NET and Windows App SDK runtimes. Keep the complete directory together; the EXE alone is not a portable distribution.

MAUI Controls is pinned to 10.0.110. This is an interaction comparison, not a controlled performance benchmark: this sample is a single-process ARM64 MAUI Release app, while the current DotNative Windows demo uses a separate ARM64 managed preview host and an x64 native renderer under emulation. MAUI uses its own default navigation, control styling and CollectionView scrolling behavior. DotNative's custom header opacity/nested scrolling policies are not emulated here.
