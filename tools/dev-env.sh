#!/bin/sh
# Source this file to build or run Passenger from a shell when the .NET SDK was
# installed for your user alone (as tools/setup-dev.sh installs it). It changes
# only this shell's environment; nothing is installed system-wide.
#
#   . tools/dev-env.sh
#   just build
#   just run

DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$HOME/.local/bin:$PATH"
export AVALONIA_TELEMETRY_OPTOUT="${AVALONIA_TELEMETRY_OPTOUT:-1}"
