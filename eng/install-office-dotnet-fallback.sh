#!/usr/bin/env bash
# Recover Office CI from a failed aka.ms/origin download using Microsoft's
# public builds feed. An explicit version prevents another aka.ms redirect.
set -euo pipefail

INSTALL_DIRECTORY="${1:?Usage: install-office-dotnet-fallback.sh <install-directory>}"
DOTNET_FEED="https://builds.dotnet.microsoft.com/dotnet"
SDK_VERSION="$(curl --fail --silent --show-error --location --retry 3 --connect-timeout 20 --max-time 120 \
    "$DOTNET_FEED/Sdk/10.0/latest.version" | tr -d '\r\n')"
if [[ ! "$SDK_VERSION" =~ ^10\.0\.[0-9]+$ ]]; then
    echo "Unexpected .NET 10 SDK version: $SDK_VERSION" >&2
    exit 1
fi

INSTALL_SCRIPT="$(mktemp)"
trap 'rm -f "$INSTALL_SCRIPT"' EXIT
curl --fail --silent --show-error --location --retry 3 --connect-timeout 20 --max-time 120 \
    https://dot.net/v1/dotnet-install.sh --output "$INSTALL_SCRIPT"
bash "$INSTALL_SCRIPT" --version "$SDK_VERSION" --azure-feed "$DOTNET_FEED" \
    --install-dir "$INSTALL_DIRECTORY" --no-path
"$INSTALL_DIRECTORY/dotnet" --info

# The SDK includes its matching runtime. Prefer the recovered install over any
# incomplete setup-dotnet installation for every subsequent workflow step.
if [[ -n "${GITHUB_PATH:-}" ]]; then
    printf '%s\n' "$INSTALL_DIRECTORY" >> "$GITHUB_PATH"
fi
if [[ -n "${GITHUB_ENV:-}" ]]; then
    printf 'DOTNET_ROOT=%s\n' "$INSTALL_DIRECTORY" >> "$GITHUB_ENV"
fi
