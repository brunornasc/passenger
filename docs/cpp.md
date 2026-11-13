# C and C++ in Passenger

Passenger opens a folder with a `CMakeLists.txt` or a `conanfile.txt` / `conanfile.py` as a **C/C++ project** alongside
its .NET projects: CMake configures and builds it, Conan installs its packages, and **clangd** answers completion,
diagnostics, hover, go-to-definition and find-usages from CMake's `compile_commands.json`.

The toolchain is yours to choose: the **C standard** (C89 … C23) and the **C++ standard** (C++11 … C++26), the
compilers (`gcc`/`g++`, `clang`/`clang++`, or an external cross compiler), a target triple, a sysroot, a CMake
generator and a CMake toolchain file. Presets for **m68k** bare metal, AmigaOS and Atari MiNT are built in.

## Getting started

1. **Tools | C/C++ Toolchain…** opens the wizard. It asks each tool for its version, offers to install what is missing
   (system package manager or a user-local install with no administrator), lets you choose the compilers and
   standards, and shows the exact CMake, Conan and clangd commands before saving.
2. **Tools | CMake: Configure…** runs `conan install` (only when the recipe actually asks for packages) and `cmake`
   with `-DCMAKE_EXPORT_COMPILE_COMMANDS=ON`. The output goes to the Build window. The first **Run** or **Debug**
   configures and builds the target for you if the build folder is not there yet; a new project with an empty recipe
   never invokes Conan, so Run and Debug work immediately.
3. Open a `.c`, `.cpp`, `.hpp`, `.h`, `.s` or `.asm` file. clangd starts for that project and reads the compilation
   database from the build folder; completion and diagnostics come from the compiler's own words. C, C++ and
   assembly files are coloured by TextMate grammars.
4. Each `add_executable` target becomes a **run configuration** in the toolbar: **Run** builds and starts its binary,
   **Debug** starts it under GDB (see below). **Tools | CMake: Build** and **Tools | C/C++: Run Target…** remain
   as direct commands.

No step runs behind your back: the wizard installs nothing until **Install** is pressed, and Passenger writes no
build files until a configure, run or debug is asked for.

## Run and Debug with GDB

C/C++ targets are debugged by **GDB's own Debug Adapter Protocol interpreter** (`gdb -i=dap`, GDB 14 or later),
started through Passenger's DAP client and shown in the Debug window like any other session:

- **breakpoints** in `.c`, `.cpp`, `.h` and assembly files are sent to GDB before the program starts (GDB binds
  them as the executable loads), with conditions, hit counts and logpoints where GDB supports them;
- **stepping** (over, into, out), pause, continue, Run to Cursor, and Stop work in the Debug toolbar;
- the **frames** and **variables** panes show GDB's stack frames, locals and registers, and the expression box
  evaluates in the selected frame;
- the program's stdout/stderr goes to the Debug tab's console.

`gdb` is found on the PATH or named in *gdb executable* (Settings | C/C++); the wizard detects it and offers a
system install command. GDB's DAP starts the inferior at `launch`, so Passenger sends its breakpoints while they
are still pending and does not send `configurationDone` (GDB answers that with `notStopped`). A cross-compiled
m68k binary cannot be run here, but its host-side tests can be debugged the same way.

Hot Reload and the CPU/memory profiler remain .NET-only; a C/C++ target uses its own tools for those.

## New projects

**File | New Solution** offers *CMake C/C++ Project* (in C or C++). It writes:

- `CMakeLists.txt` with the standards you chose,
- `conanfile.txt` with the `CMakeDeps` and `CMakeToolchain` generators,
- `CMakePresets.json`,
- `.clangd` with the fallback flags and target for a cross toolchain,
- `main.cpp` (or `main.c`) and a `.gitignore` for the build folder.

Its folder opens as the workspace; `CMakeLists.txt` is its project file.

## Settings (Settings | C/C++)

| Setting | What it does |
| --- | --- |
| Toolchain preset | System, GCC, Clang, `m68k-elf`, `m68k-amigaos`, `m68k-atari-mint`, or Custom with your own paths. |
| C / C++ standard | Passed as `CMAKE_C_STANDARD` / `CMAKE_CXX_STANDARD` and to clangd as `-std=…`; *From CMakeLists.txt* leaves the project's own setting alone. |
| CMake generator | Ninja (default), Unix Makefiles, NMake Makefiles, Visual Studio, or CMake's own default. |
| C / C++ compiler | `CMAKE_C_COMPILER` / `CMAKE_CXX_COMPILER`. A cross compiler's path is also what clangd queries for its system headers. |
| clangd / cmake / conan / gdb | Executable paths; empty means the PATH, or the wizard's user-local install. GDB 14+ speaks DAP itself. |
| Build folder / Build type | Where `compile_commands.json` and the binaries go (`cmake-build-debug`, `Debug`). |
| Target triple / Sysroot | `--target` and `--sysroot` for clangd, `CMAKE_*_COMPILER_TARGET` and `CMAKE_SYSROOT` for CMake. |
| CMake toolchain file | One of your own takes the place of Conan's toolchain where both are named. |
| Conan profile | `-pr` for `conan install`. |
| Run Conan before configuring | `conan install . --build=missing -s build_type=…` before `cmake`, with `conan_toolchain.cmake` passed as the toolchain file. |
| Extra CMake arguments | More `-D…` words, quoted as a shell would. |

Changing any of these restarts clangd on the open C/C++ files, so completion follows the toolchain at once.

## Cross compilers (m68k and others)

Choose a cross preset, or *Custom* and name, for example, `m68k-elf-gcc` and `m68k-elf-g++` with target triple
`m68k-elf`. Passenger then:

- configures CMake with those compilers and `-DCMAKE_CXX_COMPILER_TARGET=m68k-elf`,
- gives clangd `--query-driver=<the cross compilers>` so it asks the compiler for its built-in include paths,
- writes `--target=m68k-elf` and `--sysroot=…` into `.clangd` and clangd's `fallbackFlags`,
- leaves Conan off for the bare-metal presets (a cross build needs its own host profile and packages).

A cross toolchain is installed by its vendor, not by Passenger: the wizard's last install step says so and lets you
name its full path. On Debian/Ubuntu, `gcc-m68k-linux-gnu` is one source of an m68k Linux cross compiler;
AmigaOS and Atari MiNT toolchains are published by their communities.

## The commands the toolchain makes

```
conan install <project> --output-folder <build>/conan --build=missing -s build_type=Debug -s compiler.cppstd=20
cmake -S <project> -B <build> -G Ninja -DCMAKE_EXPORT_COMPILE_COMMANDS=ON -DCMAKE_BUILD_TYPE=Debug \
      -DCMAKE_CXX_COMPILER=g++ -DCMAKE_CXX_STANDARD=20 -DCMAKE_CXX_STANDARD_REQUIRED=ON \
      -DCMAKE_TOOLCHAIN_FILE=<build>/conan/conan_toolchain.cmake
cmake --build <build> --config Debug --parallel [--target <name>]
clangd --background-index --compile-commands-dir=<build> --query-driver=… --completion-style=detailed
```

## Installing the tools without the IDE

`tools/cpp-tools.sh` is the wizard's command-line counterpart:

```sh
./tools/cpp-tools.sh --check           # what is installed and what is missing
./tools/cpp-tools.sh --user --yes      # CMake, Conan, clangd, Ninja into ~/.local/share/passenger-cpp/venv
./tools/cpp-tools.sh --system          # print the system package-manager commands (needs sudo)
```

On Windows and macOS the wizard offers `winget` / `brew` commands; the user-local path uses a Python virtual
environment everywhere, so no administrator rights are needed.

## Installing packages with Conan

**Tools | Install C/C++ dependencies with Conan…** opens a wizard that reads the recipe list from
[conan.io/center](https://conan.io/center) (the Conan 2 remote's `/v2/conans/search` API, ~1,900 recipes with their
versions). Filter the list, choose a recipe and the version of it, press **Add**, and repeat; a reference can also be
typed by hand (`fmt/11.0.2`) when the web is unreachable. The list falls back to the older remote and then to
`conan list "*" -r=conancenter`.

The **Chosen packages** list starts with the packages the recipe already asks for, so installing a second dependency
never removes the first; **Remove** takes one out of the list. **Install** then, in order:

1. rewrites `conanfile.txt`'s `[requires]` from that whole list (removing an old `[settings]` section, which Conan 2
   refuses — settings come from a profile);
2. runs `conan install` with `CMakeDeps` and `CMakeToolchain` (detecting a default profile where the cache has none);
3. takes the `find_package()` names and CMake targets **from Conan's own output** (`fmt::fmt`, `ZLIB::ZLIB`, …) and
   adds them to `CMakeLists.txt` as `find_package(X REQUIRED)` and
   `target_link_libraries(<target> PRIVATE …)`, without duplicating lines a second install already added;
4. **configures the project right away** with Conan's `conan_toolchain.cmake` and `CMAKE_PREFIX_PATH`, replacing a
   build cache made before the packages existed (CMake ignores a toolchain added to an existing cache). The recipe
   is read from disk, so this also works when the folder watcher has not re-read it yet.

Nothing is written before **Install**. When it succeeds the button becomes **OK** (Enter closes the wizard) and the
project is ready: **Run** or **Debug** only builds and starts it. `Tools | Conan: Install…` remains a direct
re-install of the recipe as it stands; Run, Debug and Build reconfigure automatically if the recipe changed by hand.

## Syntax colours

`.c`/`.h` files use VS Code's C grammar, `.cpp`/`.hpp` and the rest its C++ grammar (both carried, unchanged,
from VS Code 1.96.0's `extensions/cpp`, MIT — see `THIRD_PARTY_NOTICES.md`), which also colour C and C++ fenced
code blocks in Markdown. `.s`, `.asm` and `.nasm` files use a small assembly grammar of Passenger's own:
comments (`;`, `#`, `//`, `/* */`), directives, labels, registers, instructions, numbers and strings.

## What is .NET-only still

Hot Reload and the CPU/memory profiler are built on the .NET runtime, so C/C++ targets use their own tools for
those (and the Profile action says so). Debugging, running and building are integrated: the Run/Debug toolbar
starts a CMake target through CMake and GDB. The unit-test runner stays .NET-only.

## Troubleshooting

- **"clangd was not found"** — run the wizard or `tools/cpp-tools.sh`, then name its path in Settings | C/C++.
- **No completion yet** — run **CMake: Configure**; clangd needs `compile_commands.json` in the build folder.
  Until then it uses the fallback flags (`.clangd`) for the chosen standard and target.
- **Headers of a cross compiler are missing** — check the compiler paths in the wizard: clangd's `--query-driver`
  is built from them.
- **Conan cannot find a package** — check the recipe's `[requires]`, then **Tools | Conan: Install…**. A `conanfile.py`
  with `cmake_layout(self)` writes its generators under `build/<config>/generators` instead of `build/conan`; name that
  file in *CMake toolchain file* (Settings | C/C++) in that case.
- **`Could not find a package configuration file provided by "fmt"`** — the build folder was configured before the
  packages were added, so its cache has no Conan toolchain. Passenger now reconfigures when the recipe gains
  packages (wizard, Run, Debug and Build all detect it); to fix an old folder by hand, delete
  `cmake-build-debug/CMakeCache.txt` and run **Tools | CMake: Configure…**. Conan's `fmt-config.cmake` lives in
  `cmake-build-debug/conan`, named by `CMAKE_PREFIX_PATH`.
- **`ConfigParser: Unrecognized field 'settings'`** — a `conanfile.txt` written by an older Passenger had a
  `[settings]` section, which Conan 2 does not accept. The package wizard rewrites it; settings belong to a profile
  (`conan profile detect`). Passenger's new projects no longer write that section, and no longer create an empty
  `.slnx` or run NuGet either.
- **"Could not debug …: gdb"** — GDB 14 or later is needed for `-i=dap`; older GDB speaks only GDB/MI. Name its
  full path in *gdb executable* if it is off the PATH.
- **A breakpoint stays pending** — GDB binds it when the executable loads; make sure the binary was built with
  debug info (`-g`, which CMake's Debug/RelWithDebInfo build types add).
- **The build folder is stale after changing compilers** — delete it (or change *Build folder*) and configure again;
  CMake caches the compiler it configured with.
