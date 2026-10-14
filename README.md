# DotNative

Declarative C# UI backed by native platform controls, with a Rust/Taffy layout core.

**Early beta (`0.1.0-beta.1`), licensed under MIT.** The same C# component and state API
runs on macOS, iOS, Android, Windows and Linux. Platform and plugin coverage is
uneven; each package documents its supported native implementations.

```csharp
public sealed class Counter : Component
{
    private readonly State<int> _count = new(0);

    public override Element Build() =>
        new VStack(
            new Text($"Count: {_count.Value}").FontSize(28),
            new Button("Increment", () => _count.Value++)
        ).Padding(24);
}
```

C# builds immutable UI descriptions. Incremental binary commands cross into Rust,
Taffy computes layout, and platform adapters create and update native controls.
This is not a WebView UI. Platform adapters also perform custom decoration and
painting; not every visual element is an unmodified stock control.

## Try it

The installable .NET tool provides the `dotnative` command and includes its SDK
sources and templates. It requires .NET 10 and the target platform's native build
tools. See [CLI installation](tools/DotNative.Cli/README.md). This version can be
installed from a local package feed; these instructions do not imply it is already
published to NuGet.org.

```sh
# Build the tool package from this checkout:
dotnet pack tools/DotNative.Cli -c Release -o artifacts/cli-feed
dotnet tool install --global DotNative.Cli --version 0.1.0-beta.1 \
  --add-source artifacts/cli-feed
# These commands also work outside the framework checkout:
dotnative devices
dotnative plugin new --name Acme.Device --output ./Acme.Device
dotnative run mac --hot-reload --project /path/to/MyApp
```

Start with the [desktop quickstart](docs/getting-started.md) for platform prerequisites.
Framework contributors can still run the CLI from source:

```sh
dotnet run --project tools/DotNative.Cli -- run mac --hot-reload --project Samples/Counter
# On Windows or Linux, replace mac with windows or linux.
```

[Counter](Samples/Counter/App.cs) is the smallest working example.
[FaliNamIgrac](Samples/FaliNamIgrac/README.md) is the showcase port of an existing
production app, with commissioned assets, authentication and backend integration requiring your own configuration.
Its 1:1 replica is still in progress.
[WidgetSamples](Samples/WidgetSamples/App.cs) exercises forms, themes, navigation,
sidebars, mobile bottom navigation and plugins. See the
[platform support and evidence matrix](docs/platform-support.md) before choosing a target.

## Release status

**Implemented:** native UI/runtime and channel-based native plugins; shared iOS
binary SDK resolution with pinned checksums, resource/privacy bundles and notification
callback dispatch. Implementation does not imply every platform path was exercised.

**Verified in the publication preparation:** macOS Apple Silicon Counter from a
clean checkout, Increment/Reset and hot reload; Apple SDK device/simulator fixture
builds with resource and signature checks. See [publication evidence](docs/publication-readiness.md)
for scope and outstanding release gates, and the per-platform evidence below.

**Planned:** reusable plugin host adapters for .NET MAUI, Avalonia and Uno Platform.
They are not included or supported in this release. Signed store distribution,
full showcase parity and real-device/two-account journeys need separate validation.

## What is here

- Reactive `State<T>`, keyed native identity, scoped themes and .NET DI.
- Layout, scrolling, forms, navigation, configurable transitions and styling.
- Native AppKit and WinUI desktop sidebars; UIKit and Android Material bottom bars.
- Native service plugins and platform-targeted .NET plugin integration.
- NativeAOT build paths and a separate managed hot-reload development mode.

Windows uses **WinUI 3 for the entire UI**, including controls, text measurement,
scrolling and the `NavigationView` sidebar. A thin native shell owns the OS window
and message loop; it does not render Win32 controls. The WinUI runtime is required,
with no fallback to the old renderer. See [Windows coverage](docs/desktop-preview.md)
for migration limits and build requirements.
Linux currently uses GTK4, including a composed sidebar; there is no Qt/KDE backend.
Desktop NativeAOT demos use two processes: a compiled C# host and a Rust renderer.
Android platform-binding NativeAOT integration remains experimental.

[`ListView`](docs/virtual-lists.md) is virtualized by default. It keeps only the viewport and overscan rows mounted, supports fixed or
automatically measured heights, stable keys, scroll-to-index/end, and preserves
the visible anchor through data and height changes on iOS, Android, macOS, Windows
and Linux. Scrolling within the same row window retains its descriptions; unchanged
overlapping rows retain their content. UIKit/Android collection cells and AppKit/GTK
row containers are pooled. Windows uses a WinUI `ScrollViewer` with the shared
bounded row window. Compatible layout/text/button row subtrees are recycled across items. Stateful
form-row recycling, broader accessibility/IME validation, signed distribution and full platform parity remain open. There is no
blanket 120 Hz guarantee. See [production milestones](docs/production-roadmap.md).

For a small **nonvirtual list**, build ordinary layout children. `VStack` measures
and mounts every item and takes their combined height; put it inside a single
`ScrollView` only if that content needs to scroll:

```csharp
var rows = items.Select(item =>
    new Text(item.Name).Padding(8).WithKey(item.Id.ToString())).ToArray();

var content = new VStack(rows).Spacing(4); // all items are mounted
// Optional, for a standalone scrolling region:
var scrolling = new ScrollView(content).Height(240);
```

Use this for a few items inside a page, such as invoice lines. Use `ListView<T>`
for large or growing collections.


## Documentation

- [Architecture](docs/architecture.md) and [startup / DI](docs/startup-and-di.md)
- [Styling](docs/styling-capabilities.md), [themes](docs/theming.md),
  [adaptive navigation](docs/adaptive-navigation.md), [scrolling](docs/nested-scrolling.md)
- [Plugin authoring](docs/plugin-authoring.md) and [platform .NET bindings](docs/platform-bindings.md)
- [Hot reload](docs/hot-reload.md), [desktop preview](docs/desktop-preview.md),
  [runtime tests](docs/runtime-interaction-tests.md)
- [Samples and templates](Samples/README.md), [protocol](docs/protocol.md)

## Build and test

Install .NET SDKs 9 and 10, and Rust 1.94.0. Native builds additionally need the
platform tools listed in the quickstart.

```sh
bash scripts/check-core.sh
```

[Core CI](https://github.com/kantagara/DotNative/actions/workflows/ci.yml) and
[desktop CI](https://github.com/kantagara/DotNative/actions/workflows/desktop.yml)
are manually triggered and cover different native paths. Run development checks locally; hosted runners are opt-in. Passing unit tests does not establish device performance.
The manual desktop-preview workflow builds downloadable NativeAOT demo archives;
these are development artifacts, not signed installers.

Mobile login/chat editor options and current limits: [text input](docs/text-input.md).

## Repository and contributions

`src/` contains the C# API, `native/core/` the Rust engine and platform adapters,
`plugins/` first-party capabilities, `tools/` the CLI, and `templates/` app scaffolding.
Generated outputs belong in ignored build directories.

Bug reports are welcome: include commit, OS/architecture, exact command, SDK versions,
and a minimal reproduction. See [CONTRIBUTING.md](CONTRIBUTING.md).

[MIT license](LICENSE). Third-party dependencies retain their own licenses.
