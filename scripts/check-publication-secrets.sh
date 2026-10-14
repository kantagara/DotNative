#!/usr/bin/env bash
# Read-only scan. Reports are redacted; exit nonzero on findings or scanner errors.
set -euo pipefail
repo="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
command -v gitleaks >/dev/null || { echo 'Install gitleaks (tested with 8.30.1).' >&2; exit 2; }
temporary="$(mktemp -d)"
trap 'rm -rf "$temporary"' EXIT
# --all includes every locally available branch/tag. Fetch remote refs before release.
gitleaks git "$repo" --log-opts=--all --redact=100 --no-banner
# Also inspect current tracked contents, including binaries/archives supported by gitleaks.
# Export the index, not ignored .env files, caches or user attachments.
git -C "$repo" checkout-index --all --prefix="$temporary/"
gitleaks dir "$temporary" --redact=100 --max-archive-depth=2 --no-banner
