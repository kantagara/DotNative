# Fali nam igrač — DotNative showcase

An early-preview port of the owner's existing application, demonstrating the
DotNative runtime and native plugins together. It is not a store-ready release
or a verified 1:1 Flutter replacement.

## Implemented

The repository contains authentication and recovery, onboarding, profile editing
and account deletion, game browsing/details/editor, my appointments, chat,
notification routing, monetization and platform action integrations. See `Features/`
for service contracts, state and UI implementations. Implementation presence is
not evidence that every journey works against a live backend or on every device.

Chat has REST/AppSync transports, subscriptions, local history/draft persistence,
message deduplication and recovery paths. Notification and ad integrations require
native SDK configuration and device validation. Theme/localization and visual
parity remain subject to comparison with the reference application.

## Run from source

Install the framework [prerequisites](../../docs/getting-started.md) first. From the
repository root, restore the versioned plugin sources:

```sh
dotnet run --project tools/DotNative.Cli -- restore --project Samples/FaliNamIgrac
```

`dotnative.dependencies.props` pins Git tags under the ignored
`.dotnative/dependencies` directory. New local plugin changes are not automatically
included by those pins. For plugin development, copy `dotnative.local.props.example`
to `dotnative.local.props` and set your own checkout paths.

Copy `.env.example` to `.env` and supply your **own approved backend configuration**.
No accounts or access to the original service are provided. `FNI_CHAT_URL` is
required; there is no implicit shared chat backend. `Core/BackendConfiguration.cs`
defines required settings. Public mobile identifiers may also be supplied in the
ignored `fni.public.json` resource; `Core/PublicConfiguration.cs` restricts its
allowed keys. Private service credentials, passwords and access tokens must never
be embedded in that file. Platform manifests, entitlements, service IDs and signing
must match the environment you configure.

```sh
dotnet run --project tools/DotNative.Cli -- run ios --hot-reload --project Samples/FaliNamIgrac
# For a booted ARM64 Android emulator, replace ios with android.
```

This is a mobile showcase; start with Counter for the desktop quickstart. Source
restoration/building does not require signing into an application account.

Debug-only visual routes accept `DOTNATIVE_FNI_PREVIEW_PAGE=login`, `registration`,
`reset-password`, `new-password`, `onboarding`, or `onboarding-error`. They do not
supply backend credentials or establish successful registration. Normal startup
uses the saved session when the variable is absent; Release builds omit these routes.

## Verification and remaining work

[Publication evidence](../../docs/publication-readiness.md) records checks actually
run. `tests/FaliNamIgrac.Tests` and the feature test projects contain managed
regressions; they do not establish notification delivery, ad behavior, keyboard
geometry, visual parity or store acceptance.

Real-account authentication/recovery, two-account chat/reconnect, push deep links,
permissions, signed device distribution, accessibility and full light/dark visual
comparison still require separate release validation. The owner's live device
testing is separate from build/fixture checks. Full Flutter parity is a goal.

The application uses a private, environment-specific backend contract. Publishing
this client does not publish or provision that backend. Do not point public test
installations at a production environment by copying another installation's values.

## Code and assets

`Core/` owns configuration and shared infrastructure; `Features/` contains feature
services/state/UI; `App` composes the application lifetime. Plugin implementations
live in independently versioned repositories, rather than copied application code.

Owner-reported commissioned artwork and third-party font attribution are recorded
in [Resources/ASSET_PROVENANCE.md](Resources/ASSET_PROVENANCE.md). The font license
files remain alongside their binaries. The repository MIT license does not replace
third-party licenses or establish rights in third-party marks.
