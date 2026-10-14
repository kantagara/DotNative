# Show HN submission draft

This is a draft for the maintainer to post after the release checks; it has not
been submitted to Hacker News.

Title: **Show HN: DotNative – declarative C# UI with native controls and a Rust layout core**

URL: https://github.com/kantagara/DotNative

Suggested opening comment (edit to reflect your own motivation and experience):

> DotNative is an early developer preview of a declarative C# UI framework.
> C# describes the UI and manages reactive state, a binary protocol sends changes
> to Rust, Taffy computes layout, and platform adapters update native controls.
>
> The repository includes Counter and a larger widget/navigation demo, with run
> instructions for macOS, Windows and Linux. There are also iOS and Android paths.
> The same component API is used across targets; the implementation and verified
> coverage differ, and the support matrix documents those differences.
>
> The current release is a preview with partial plugin coverage. Lists virtualize by default,
> Linux currently uses GTK rather than switching to Qt on KDE, and Android .NET
> binding integration with NativeAOT is experimental. Desktop NativeAOT demos use
> a C# host and Rust renderer in separate processes. I am especially interested
> in feedback on the C# API, platform behavior, and the first-run experience.

## Demo recording sequence

Use WidgetSamples on a tested target. Record 45–60 seconds: native controls,
Light/Dark switch including sidebar, Cart push/Back preserving state, sidebar
collapse/reveal, and a file-picker round trip. Show a short C# component beside
it. Label OS and architecture. Record actual application output; do not imply
frame-rate measurements from a video or simulator.

## Before posting

- License selected and included.
- Latest release commit has passing core and desktop checks.
- A fresh-checkout first-run command reaches an interactive Counter window.
- Release binaries, if offered, have been tested after extraction; label unsigned
  or unnotarized status and OS/runtime requirements.
- README links, support matrix, known limits and release commit agree.
- Maintainer has reviewed this text and is available to answer questions.

A video is useful context, but the working repository/demo is the submission.
Do not ask friends for votes. See https://news.ycombinator.com/showhn.html.
