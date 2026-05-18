#!/usr/bin/env pwsh
# Initialize submodules and ensure the repo is in a buildable state.
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path "$PSScriptRoot/.."

Write-Host "[bootstrap] Updating submodules under $repo/external"
git -C $repo submodule update --init --recursive

Write-Host "[bootstrap] Submodule status:"
git -C $repo submodule status --recursive
