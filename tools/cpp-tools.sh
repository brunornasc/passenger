#!/bin/sh
# Installs the C/C++ tools Passenger needs: CMake, Conan, clangd and Ninja.
# The in-IDE wizard uses the same commands; this is its CLI counterpart.
#   ./tools/cpp-tools.sh              # interactive, user-local (no sudo)
#   ./tools/cpp-tools.sh --user --yes # user-local, no questions
#   ./tools/cpp-tools.sh --check      # report what is installed
#   ./tools/cpp-tools.sh --system     # system package-manager commands
# The user-local install uses ~/.local/share/passenger-cpp/venv, so it needs
# no administrator. Cross compilers come from their own toolchains.

set -eu

HOME_DIR="${HOME:-/root}"
TOOLS_DIR="$HOME_DIR/.local/share/passenger-cpp"
VENV="$TOOLS_DIR/venv"
BIN_DIR="$HOME_DIR/.local/bin"
PACKAGES="cmake conan clangd ninja"
WAY=user
CHECK=0
YES=0

for arg in "$@"; do
    case "$arg" in
        --user) WAY=user ;;
        --system) WAY=system ;;
        --check) CHECK=1 ;;
        --yes|-y) YES=1 ;;
        --help|-h)
            sed -n '2,9p' "$0" | sed 's/^# \{0,1\}//'
            exit 0 ;;
        *) echo "Unknown argument: $arg" >&2; exit 2 ;;
    esac
done

say() { printf '%s\n' "$*"; }
ask() {
    [ "$YES" -eq 1 ] && return 0
    printf '%s [y/N] ' "$1"
    read -r answer || answer=
    case "$answer" in y|Y|yes|YES) return 0 ;; *) return 1 ;; esac
}

which_tool() { command -v "$1" 2>/dev/null || true; }

version_of() {
    tool="$1"
    program=$(which_tool "$tool")
    [ -n "$program" ] || { say "  $tool: not found"; return 1; }
    line=$("$program" --version 2>/dev/null | head -1 || true)
    say "  $tool: ${line:-no version said} ($program)"
}

check() {
    say "C/C++ tools on this machine:"
    for tool in cmake conan clangd ninja gdb cc c++; do
        version_of "$tool" || true
    done
    say "Cross compilers (optional, for m68k and other targets):"
    for cross in m68k-elf-gcc m68k-elf-g++ m68k-amigaos-g++ m68k-atari-mint-g++; do
        version_of "$cross" || true
    done
}

distro() {
    if [ -f /etc/os-release ]; then
        # shellcheck disable=SC1091
        id=$(. /etc/os-release && printf '%s' "$ID")
        case "$id" in
            debian|ubuntu|linuxmint|pop|neon|raspbian) printf 'debian' ;;
            fedora|rhel|centos|rocky|almalinux) printf 'fedora' ;;
            arch|manjaro|endeavouros) printf 'arch' ;;
            opensuse*|sles|suse) printf 'suse' ;;
            alpine) printf 'alpine' ;;
            *) printf 'unknown' ;;
        esac
    else
        printf 'unknown'
    fi
}

system_commands() {
    case "$(distro)" in
        debian)
            say "  sudo apt install -y cmake clangd ninja-build gdb build-essential python3-pip python3-venv"
            say "  python3 -m pip install --user conan   # or: pipx install conan"
            ;;
        fedora)
            say "  sudo dnf install -y cmake clang-tools-extra ninja-build gdb gcc gcc-c++ python3-pip"
            say "  python3 -m pip install --user conan"
            ;;
        arch)
            say "  sudo pacman -S --noconfirm cmake clang ninja gdb base-devel python-pip"
            say "  python3 -m pip install --user conan"
            ;;
        suse)
            say "  sudo zypper install -y cmake clang-tools ninja gdb gcc gcc-c++ python3-pip"
            say "  python3 -m pip install --user conan"
            ;;
        alpine)
            say "  sudo apk add cmake clang clang-tools ninja gdb build-base py3-pip"
            say "  python3 -m pip install --user conan"
            ;;
        *)
            say "  Install CMake, clangd, Ninja, GDB and a C/C++ compiler with your package manager."
            say "  Install Conan with: python3 -m pip install --user conan"
            ;;
    esac
}

install_user() {
    if ! command -v python3 >/dev/null 2>&1; then
        say "python3 is needed for a user-local install. Install it, or use --system." >&2
        exit 1
    fi
    if [ ! -x "$VENV/bin/python" ]; then
        say "Creating a Python environment in $VENV"
        python3 -m venv "$VENV"
    fi
    say "Installing CMake, Conan, clangd and Ninja for you alone (no administrator needed)"
    "$VENV/bin/python" -m pip install --quiet --upgrade pip
    # shellcheck disable=SC2086
    "$VENV/bin/python" -m pip install --quiet $PACKAGES
    mkdir -p "$BIN_DIR"
    for tool in cmake conan clangd ninja; do
        if [ -x "$VENV/bin/$tool" ]; then
            ln -sf "$VENV/bin/$tool" "$BIN_DIR/$tool"
        fi
    done
    say "Installed into $VENV, with links in $BIN_DIR."
    say "If $BIN_DIR is not on your PATH, name the full paths in Settings | C/C++."
    say "GDB is a system debugger: if it is missing, use --system for its install command."
}

if [ "$CHECK" -eq 1 ]; then
    check
    exit 0
fi

if [ "$WAY" = system ]; then
    say "System install commands for this machine:"
    system_commands
    if ask "Run these commands now (they need administrator rights)?"; then
        say "Run the commands above yourself, then: $0 --check"
    fi
    exit 0
fi

say "Passenger's C/C++ tools"
say "  1. Check what is installed"
check
say "  2. Install what is missing, for you alone, in $VENV"
say "     (CMake, Conan, clangd, Ninja; compilers come with the system or a cross toolchain)"
say "  3. Check again"
if ! ask "Go on?"; then
    say "Nothing was installed."
    exit 0
fi
install_user
say ""
say "After the install:"
check
say ""
say "In Passenger: Tools | C/C++ Toolchain... chooses the compilers and C/C++ standards."
say "For m68k: install a cross toolchain, then choose its preset (or Custom) in the wizard."
