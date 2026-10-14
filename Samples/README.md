# Samples

`FaliNamIgrac/` is the showcase port of an existing production application.
It includes original assets, authentication and backend integration, and mobile
native runners. The replica is still in progress; see its [README](FaliNamIgrac/README.md)
for implemented features, remaining work and plugin restore instructions.
Run it from the repository root:

```sh
dotnet run --project tools/DotNative.Cli -- restore --project Samples/FaliNamIgrac
dotnet run --project tools/DotNative.Cli -- run ios --hot-reload --project Samples/FaliNamIgrac
```

`MauiComparison/` is a separate Windows-only MAUI comparison app, deliberately
outside `Samples.sln` so it does not require MAUI workloads for DotNative development.
See its README for Release build instructions and comparison limitations.

Open `Samples.sln`. Each application has one project, root `Program.cs` and `App.cs`, and
native runners under `Platform/`. WidgetSamples groups demonstrations under
`Widgets/`; Counter is the smallest state-and-button example.

Counter and WidgetSamples were generated with the actual DotNative project template:

```sh
# From the repository root:
dotnet new install ./templates/DotNative.App --force
dotnet new dotnative -n MySample -o Samples/MySample --bundleId com.dotnative.mysample
dotnet sln Samples/Samples.sln add Samples/MySample/MySample.csproj --in-root --include-references false
```

Do not copy an existing sample's project or Platform folder. Fix native runner
scaffolding in `templates/DotNative.App` so newly generated apps get the same fix.
The repository CLI lives in `tools/DotNative.Cli`; run it with
`dotnet run --project tools/DotNative.Cli -- <command>`. `dotnet new dotnative`
creates projects after template installation. The first-run path does not require
a globally installed tool; see [desktop quickstart](../docs/getting-started.md).

`Directory.Build.props` and `.targets` make all projects here reference the
framework source automatically. Generated apps outside Samples use NuGet.
No edits to a generated sample's csproj are necessary for source development.

Run on a booted ARM64 iOS Simulator:

```sh
bash Samples/WidgetSamples/Platform/iOS/run.sh
```

Native publish and runner intermediates live in ignored `obj/DotNative/`
directories; normal .NET outputs use `bin/` and `obj/`. They can be regenerated.
Repository NuGet packages go to `bin/packages/`. No build products belong beside
sample source files in an `artifacts/` directory.
