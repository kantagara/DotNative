# Public preview preparation — 2026-10-14

This is evidence for an early public preview, not a production certification.
MAUI, Avalonia and Uno plugin host adapters are planned, not implemented.

## Scope

The framework repository (including FaliNamIgrac) and 34 sibling `DotNative.*`
plugin/tool repositories were inventoried. Remote branches and tags were fetched
before the final Git-history scan. Counts describe repositories, not fully tested
cross-platform capabilities. Ignored local environments, caches and private user
attachments are not publication inputs and were not exported.

## Secrets and Git history

Gitleaks 8.30.1 scanned all locally fetched refs and exported tracked content.
The framework history contained four Google-key occurrences in three old source
files, representing two distinct keys. Current tracked framework content did not
contain those keys. These findings must not be dismissed merely because the current
source uses environment configuration.

After validation in an isolated mirror, the owner requested cleanup of the actual
private repository. Its branch history was rewritten to remove the two values
from historical blobs and commit messages. A private recovery bundle was saved
outside the repository before rewriting. Local configuration remains in ignored
`.env`/public configuration files and is not a publication input. The recovery
bundle intentionally retains the old history and must never be published.

Changed commit identities are inherent in this cleanup. Remote updates use exact
old-ref leases; existing clones should be recloned or reset to the cleaned history.
Git hosting caches and old object URLs are outside the scope of reachable-ref
scanning. No scanner proves the absence of all possible secrets.

Preferences produced a false positive on an exact Swift argument-validation line.
Its checked-in scanner configuration exempts only that line in that file, preserving
the default scanner rules. The other plugin histories had no scanner findings.
No automated scan proves the absence of all possible secrets.

**Provider-side blocker confirmed:** read-only Google Cloud inspection matched both
historical values to active Firebase Android/iOS keys. Each permits 61 APIs,
including Maps/Places, with empty application restriction lists. No key values
were written to this document. This is overly broad configuration; no misuse or
billing impact was established. No production key was changed or revoked.

Firebase client keys are generally public identifiers, but using them for other
Google APIs requires separate scrutiny: see [Firebase API-key guidance](https://firebase.google.com/docs/projects/api-keys)
and [Maps security guidance](https://developers.google.com/maps/api-security-best-practices).
The existing application also used one of these values for Places. Before narrowing
or revoking it, migrate Maps/Places usage to a separately restricted integration,
verify the deployed application's Firebase/Places flows, then remove unnecessary
API access and rotate as appropriate. Restricting the live shared keys blindly
could break already installed clients. Historical cleanup alone does not fix this.

To repeat the source check after fetching remote refs:

```sh
bash scripts/check-publication-secrets.sh
```

The cleaned repository passes history scanning; there is no broad ignore for
historical Google keys. Re-run the check before publication.

## Licensing and assets

Framework and all 34 sibling repositories have MIT license files. Framework NuGet
packages now declare the license and carry existing third-party notices; the CLI
also excludes local environments and signing material from source packaging.
Dependencies retain their own licenses; a top-level MIT file does not relicense them.
Localization includes its existing BSD/Unicode notices and fonts retain OFL/Apache
licenses. Downloaded native SDK binaries are resolved from vendor archives rather
than committed as part of these source repositories.

The owner reports that application artwork was commissioned and paid for. This is
recorded in [asset provenance](../Samples/FaliNamIgrac/Resources/ASSET_PROVENANCE.md).
Commission contracts and any stock components were not independently inspected.
Retain the ownership/licensing records; payment alone is not the evidence reviewed
here for every component's redistribution rights.

## Executed verification

- Isolated Git clone, with no copied build outputs or local plugin overrides.
- macOS Apple Silicon Counter quickstart: native window, Increment 0→1, Reset 1→0,
  and a saved C# text edit visible through hot reload.
- FaliNamIgrac managed regression suite passed on net10.0 after replacing a
  production-configuration-dependent fixture with an in-memory HTTP transport and
  using a synthetic `.invalid` AppSync endpoint.
- Found and fixed a real quickstart failure: NativeAOT platform validation/build
  targets ran for the managed preview graph. `test-preview-build.py` guards it.
- Clean FaliNamIgrac plugin restoration from its pinned Git tags completed.
  All 17 pinned tags were also resolved with credential helpers, prompts and global
  Git configuration disabled, confirming anonymous availability at check time.
- CLI NuGet package built from the isolated checkout, installed into a temporary
  tool directory, and executed outside the repository: help, plugin scaffolding and
  native macOS plugin compilation passed. LICENSE and THIRD_PARTY_NOTICES are in
  the archive; a synthetic ignored `.env` file was excluded.
- Apple SDK fixture builds for device and simulator, with real Firebase/Ads
  frameworks, 15 resource/privacy bundles and ad-hoc signature verification
  (preceding native dependency work). These are build fixtures, not live push/ad tests.

The preparation used .NET SDK 10.0.300 and Rust 1.94.0 on this Mac. Windows/Linux
quickstarts and physical mobile E2E were not repeated during this preparation.
See [platform evidence](platform-support.md) for separate platform-specific checks.

## Before making a repository public

Use the cleaned repository history and repeat secret/package scans on the exact
publication refs and artifacts. Resolve the Google-key restriction/rotation item
and retain asset ownership evidence. Repeat anonymous availability checks for pinned plugin tags if versions change.

The owner authorized updating rewritten refs in the private remote. This does
not change GitHub visibility, publish NuGet packages, create releases or rotate
production credentials. Signed store delivery,
full visual parity, accessibility and live-account journeys remain separate milestones.
