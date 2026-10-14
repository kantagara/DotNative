# Show HN preview release preparation

## Verified locally

2026-10-05, macOS 26 / Apple Silicon, Xcode 26.6, .NET SDK 10.0.300,
Rust 1.94.0. A fresh source export with no `bin`, `obj`, `artifacts` or Cargo
`target` directories ran:

```sh
dotnet run --project tools/DotNative.Cli -- run mac --hot-reload --project Samples/Counter
```

The native window mounted; Increment changed 0 to 1 and Reset changed 1 to 0,
verified through native accessibility state. The shell needed .NET 10 first on
PATH because the machine also has Homebrew .NET 8. SDK/NuGet/Cargo download caches
were available; this is not an offline or empty-user-profile installation test.

Local managed runtime (37 groups), CLI routing/import/scaffold tests, formatting
and template drift checks passed during this release pass. CI remains the
commit-specific authority; see the workflow links in the README.

## Desktop archives

Run **Build downloadable desktop previews** manually in GitHub Actions. It builds
WidgetSamples on Windows x64 and Ubuntu 24.04 x64 from the chosen source revision,
with a NativeAOT host and native renderer. Outputs contain `SOURCE.txt`, license,
launch instructions and runtime dependencies. Linux package extraction/startup
is tested under Xvfb; that establishes the handshake and initial rendering, not
interactive behavior. Windows package compilation alone is not a runtime check.

These artifacts are unsigned previews, not installers. Before attaching them to
a public release, test the exact extracted package on its target desktop and
retain dependency notices. A CI artifact should not silently become a production
release. No NuGet package publication is implied by this workflow.

## Remaining publication actions

- Confirm core/desktop CI for the release commit.
- Test the generated Windows archive interactively and the Linux archive in a
  real desktop session before advertising those binaries as tested downloads.
- Record the short demo described in `show-hn.md` if desired.
- Review the submission text and publish it personally. No HN post has been sent.
