#!/usr/bin/env bash
# Initialize submodules and ensure the repo is in a buildable state.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo "[bootstrap] Updating submodules under $repo/external"
git -C "$repo" submodule update --init --recursive

echo "[bootstrap] Submodule status:"
git -C "$repo" submodule status --recursive
