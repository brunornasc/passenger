#!/bin/sh
# Installs what a fresh clone of Passenger needs to build and run, for your user
# alone (no sudo): the .NET 11 SDK (global.json), the .NET 10 SDK (the programs
# the tests debug) and `just`. Optionally installs the C/C++ tools too.
#
#   ./tools/setup-dev.sh              # .NET SDKs + just
#   ./tools/setup-dev.sh --cpp        # also CMake, Conan, clangd, Ninja
#
# After it finishes, either start a new shell (a link to dotnet is placed in
# ~/.local/bin) or: . tools/dev-env.sh

set -eu

HOME_DIR="${HOME:-/root}"
DOTNET_DIR="$HOME_DIR/.dotnet"
BIN_DIR="$HOME_DIR/.local/bin"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT INT TERM
WITH_CPP=0
for arg in "$@"; do
    case "$arg" in
        --cpp) WITH_CPP=1 ;;
        --help|-h) sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "Unknown argument: $arg" >&2; exit 2 ;;
    esac
done

say() { printf '\n== %s ==\n' "$*"; }
need() { command -v "$1" >/dev/null 2>&1 || { echo "$1 is needed" >&2; exit 1; }; }
need curl
need tar
mkdir -p "$BIN_DIR" "$DOTNET_DIR"

arch=$(uname -m)
case "$arch" in
    x86_64|amd64) just_arch=x86_64; just_libc=unknown-linux-musl ;;
    aarch64|arm64) just_arch=aarch64; just_libc=unknown-linux-musl ;;
    *) echo "No just binary is offered for $arch here; install just from just.systems" >&2; just_arch= ;;
esac

say "Installing the .NET 11 SDK (global.json) into $DOTNET_DIR"
curl -fsSL -o "$TMP/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh
sh "$TMP/dotnet-install.sh" --channel 11.0 --quality GA --install-dir "$DOTNET_DIR" --no-path

say "Installing the .NET 10 SDK (the programs the tests debug)"
sh "$TMP/dotnet-install.sh" --channel 10.0 --quality GA --install-dir "$DOTNET_DIR" --no-path

ln -sf "$DOTNET_DIR/dotnet" "$BIN_DIR/dotnet"

if [ -n "$just_arch" ]; then
    say "Installing just into $BIN_DIR"
    release=$(curl -fsSL https://api.github.com/repos/casey/just/releases/latest | grep -m1 '"tag_name"' | sed -E 's/.*"([^"]+)".*/\1/')
    curl -fsSL -o "$TMP/just.tar.gz" "https://github.com/casey/just/releases/download/$release/just-$release-$just_arch-$just_libc.tar.gz"
    tar -xzf "$TMP/just.tar.gz" -C "$TMP" just
    mv "$TMP/just" "$BIN_DIR/just"
    chmod +x "$BIN_DIR/just"
fi

if [ "$WITH_CPP" -eq 1 ] && [ -f "$(dirname "$0")/cpp-tools.sh" ]; then
    say "Installing the C/C++ tools"
    sh "$(dirname "$0")/cpp-tools.sh" --user --yes
fi

say "Installed versions"
"$DOTNET_DIR/dotnet" --list-sdks
"$BIN_DIR/just" --version 2>/dev/null || true
say "Build and run with:  . tools/dev-env.sh && just build && just run"
