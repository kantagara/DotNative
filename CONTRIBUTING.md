# Contributing

DotNative is an early developer preview. Start with the desktop quickstart in
`docs/getting-started.md` and keep changes focused on one behavior.

Service plugins follow the required C# shape in
[plugin authoring](docs/plugin-authoring.md#required-c-service-shape): one
`{Name}Service`, one interface per product, one `Add{Name}` registration, and
no separate routing/channel pair that only forwards `InvokeAsync`.

For bugs, include the commit, OS and architecture, SDK/toolchain versions, exact
command, expected/actual behavior and a minimal reproduction. Screenshots help for
layout; input and navigation bugs also need the action sequence.

Run the C# and Rust checks in the README. Run `bash scripts/format.sh` before
submitting; changes to platform adapters additionally need native runtime checks
on that platform. State explicitly when only compilation was verified. New native
behavior should preserve control identity, focus, accessibility and resource ownership.

Edit generated app scaffolding in `templates/DotNative.App`, then update samples
and run the template drift tests. Do not commit credentials, generated build
outputs or machine-specific SDK paths.

Contributions are made under the repository's MIT license. Preserve third-party
copyright and license notices.
