DotNative desktop developer preview — WidgetSamples

Windows x64: extract the entire ZIP and run WidgetSamples.exe.
Ubuntu 24.04 x64: extract the archive, install libgtk-4-1, then run:
  bash ./run-linux.sh
A graphical desktop session is required. Other Linux distributions/older glibc
versions have not been established as compatible with this build.

No .NET SDK is needed to run these NativeAOT demo packages. The native renderer
starts/uses a separate NativeAOT C# process under managed/. Keep all files together,
including Windows App SDK DLLs, the activation manifest and resources.pri.
Only one preview per platform can use the default loopback port at a time.

Try Light/Dark, navigate to Cart and Back, edit inputs, and collapse/reopen the
sidebar. Linux uses a composed GTK sidebar; Windows uses WinUI NavigationView.

These are unsigned development artifacts, not signed installers or evidence of
production readiness. Operating-system trust prompts may apply. Do not disable
system security protections to run a preview. The source-build path is documented
at https://github.com/kantagara/DotNative/blob/main/docs/getting-started.md.

Known limits and platform evidence:
https://github.com/kantagara/DotNative/blob/main/docs/platform-support.md

DotNative is MIT licensed; see LICENSE. Included third-party dependencies retain
their respective licenses. SOURCE.txt identifies the source commit and build OS.
