namespace Passenger.Workspace.Cpp;

/// <summary>A program a C/C++ project needs: the build system, its package manager, the language server, a generator and
/// the compilers.</summary>
public enum CppTool
{
    CMake,
    Conan,
    Clangd,
    Ninja,
    Gdb,
    CCompiler,
    CxxCompiler,
}

/// <summary>How missing tools are installed: for everyone through the system's package manager, or for this user alone
/// into a Python virtual environment, which needs no administrator.</summary>
public enum CppInstallWay
{
    System,
    UserLocal,
}

/// <summary>What to run to ask a tool whether it is there and what version it is.</summary>
public sealed record CppToolCheck(CppTool Tool, string Label, string Program, IReadOnlyList<string> VersionArguments);

/// <summary>A tool as the wizard found it: where it is, what it said of its version, or why it is missing.</summary>
public sealed record CppToolStatus(CppTool Tool, string Label, string? Path, string? Version)
{
    public bool Found => Path is not null;
}

/// <summary>One command a wizard offers to install what is missing; a tool no command can install has a note alone.</summary>
public sealed record CppInstallStep(CppTool Tool, string Label, string? Command, string? Note);

/// <summary>The tools a C/C++ toolchain needs and the commands that install them, by system and without one. The commands
/// are words alone here: the wizard shows them before it runs any of them, and a person may copy one instead.</summary>
public static class CppTools
{
    /// <summary>Where the user-local installs go, so the wizard can name their programs when they are off the PATH.</summary>
    public const string UserFolder = ".local/share/passenger-cpp";

    /// <summary>The checks a wizard runs, the compilers named by the toolchain chosen.</summary>
    public static IReadOnlyList<CppToolCheck> Checks(CppToolchain toolchain) =>
    [
        new(CppTool.CMake, "CMake", toolchain.CMakeProgram, ["--version"]),
        new(CppTool.Conan, "Conan", toolchain.ConanProgram, ["--version"]),
        new(CppTool.Clangd, "clangd", toolchain.ClangdProgram, ["--version"]),
        new(CppTool.Ninja, "Ninja", "ninja", ["--version"]),
        new(CppTool.Gdb, "GDB", toolchain.GdbProgram, ["--version"]),
        new(CppTool.CCompiler, "C compiler", toolchain.CCompiler ?? (OperatingSystem.IsWindows() ? "cl.exe" : "cc"), ["--version"]),
        new(CppTool.CxxCompiler, "C++ compiler", toolchain.CxxCompiler ?? (OperatingSystem.IsWindows() ? "cl.exe" : "c++"), ["--version"]),
    ];

    /// <summary>A tool's version as its first nonempty line says it, kept short enough for a wizard's row.</summary>
    public static string Version(string output)
    {
        string? line = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return line is null ? string.Empty : line.Length <= 100 ? line : line[..100] + "…";
    }

    /// <summary>The install commands for the missing tools: one for the Python-based tools under a user-local install,
    /// the system's own for the rest, each said in the words of the distribution named.</summary>
    public static IReadOnlyList<CppInstallStep> Plan(IEnumerable<CppTool> missing, string distribution, CppInstallWay way)
    {
        HashSet<CppTool> tools = [.. missing];
        List<CppInstallStep> steps = [];
        if (tools.Count == 0)
        {
            return steps;
        }
        if (way == CppInstallWay.UserLocal)
        {
            string venv = OperatingSystem.IsWindows() ? "%USERPROFILE%/.local/share/passenger-cpp/venv" : "~/" + UserFolder + "/venv";
            string python = OperatingSystem.IsWindows() ? "py -3 -m venv" : "python3 -m venv";
            string pip = venv + (OperatingSystem.IsWindows() ? "/Scripts/pip.exe" : "/bin/pip");
            List<string> packages = [];
            if (tools.Contains(CppTool.CMake))
            {
                packages.Add("cmake");
            }
            if (tools.Contains(CppTool.Conan))
            {
                packages.Add("conan");
            }
            if (tools.Contains(CppTool.Clangd))
            {
                packages.Add("clangd");
            }
            if (tools.Contains(CppTool.Ninja))
            {
                packages.Add("ninja");
            }
            if (packages.Count > 0)
            {
                steps.Add(new CppInstallStep(CppTool.CMake, "CMake, Conan, clangd and Ninja",
                    $"{python} {venv} && {pip} install {string.Join(' ', packages)}",
                    $"Installed for you alone in {venv}: no administrator rights needed. The wizard then points Passenger at those programs."));
            }
            if (tools.Contains(CppTool.Gdb))
            {
                steps.Add(new CppInstallStep(CppTool.Gdb, "GDB", null,
                    "GDB is a system debugger, not a Python package: install it with your package manager "
                    + "(sudo apt install -y gdb, sudo dnf install -y gdb, brew install gdb), then name its path in Settings | C/C++."));
            }
            Compilers(tools, steps, null);
            return steps;
        }
        foreach ((CppTool tool, string label, string? command, string? note) in System(distribution))
        {
            if (tools.Contains(tool))
            {
                steps.Add(new CppInstallStep(tool, label, command, note));
            }
        }
        Compilers(tools, steps, distribution);
        return steps;
    }

    private static IEnumerable<(CppTool Tool, string Label, string? Command, string? Note)> System(string distribution) => distribution switch
    {
        "debian" =>
        [
            (CppTool.CMake, "CMake", "sudo apt install -y cmake", null),
            (CppTool.Conan, "Conan", "python3 -m pip install --user conan", null),
            (CppTool.Clangd, "clangd", "sudo apt install -y clangd", null),
            (CppTool.Ninja, "Ninja", "sudo apt install -y ninja-build", null),
            (CppTool.Gdb, "GDB", "sudo apt install -y gdb", "GDB 14 or later is needed for its built-in DAP interpreter."),
        ],
        "fedora" =>
        [
            (CppTool.CMake, "CMake", "sudo dnf install -y cmake", null),
            (CppTool.Conan, "Conan", "python3 -m pip install --user conan", null),
            (CppTool.Clangd, "clangd", "sudo dnf install -y clang-tools-extra", null),
            (CppTool.Ninja, "Ninja", "sudo dnf install -y ninja-build", null),
            (CppTool.Gdb, "GDB", "sudo dnf install -y gdb", "GDB 14 or later is needed for its built-in DAP interpreter."),
        ],
        "arch" =>
        [
            (CppTool.CMake, "CMake", "sudo pacman -S --noconfirm cmake", null),
            (CppTool.Conan, "Conan", "python3 -m pip install --user conan", null),
            (CppTool.Clangd, "clangd", "sudo pacman -S --noconfirm clang", null),
            (CppTool.Ninja, "Ninja", "sudo pacman -S --noconfirm ninja", null),
            (CppTool.Gdb, "GDB", "sudo pacman -S --noconfirm gdb", "GDB 14 or later is needed for its built-in DAP interpreter."),
        ],
        "suse" =>
        [
            (CppTool.CMake, "CMake", "sudo zypper install -y cmake", null),
            (CppTool.Conan, "Conan", "python3 -m pip install --user conan", null),
            (CppTool.Clangd, "clangd", "sudo zypper install -y clang-tools", null),
            (CppTool.Ninja, "Ninja", "sudo zypper install -y ninja", null),
            (CppTool.Gdb, "GDB", "sudo zypper install -y gdb", "GDB 14 or later is needed for its built-in DAP interpreter."),
        ],
        "alpine" =>
        [
            (CppTool.CMake, "CMake", "sudo apk add cmake", null),
            (CppTool.Conan, "Conan", "python3 -m pip install --user conan", null),
            (CppTool.Clangd, "clangd", "sudo apk add clang clang-tools", null),
            (CppTool.Ninja, "Ninja", "sudo apk add ninja", null),
            (CppTool.Gdb, "GDB", "sudo apk add gdb", "GDB 14 or later is needed for its built-in DAP interpreter."),
        ],
        "macos" =>
        [
            (CppTool.CMake, "CMake", "brew install cmake", null),
            (CppTool.Conan, "Conan", "brew install conan", null),
            (CppTool.Clangd, "clangd", "brew install llvm", "clangd is in LLVM's bin folder: set its path in Settings | C/C++."),
            (CppTool.Ninja, "Ninja", "brew install ninja", null),
            (CppTool.Gdb, "GDB", "brew install gdb", "GDB 14 or later is needed for its built-in DAP interpreter; codesign it as Homebrew says."),
        ],
        "windows" =>
        [
            (CppTool.CMake, "CMake", "winget install Kitware.CMake", null),
            (CppTool.Conan, "Conan", "winget install JFrog.Conan", null),
            (CppTool.Clangd, "clangd", "winget install LLVM.LLVM", "clangd is in LLVM's tools folder: set its path in Settings | C/C++."),
            (CppTool.Ninja, "Ninja", "winget install Ninja-build.Ninja", null),
            (CppTool.Gdb, "GDB", null, "GDB comes with MinGW-w64/MSYS2 on Windows; name its full path in Settings | C/C++."),
        ],
        _ =>
        [
            (CppTool.CMake, "CMake", null, "Install CMake from cmake.org or your package manager."),
            (CppTool.Conan, "Conan", null, "Install Conan from conan.io or with pip."),
            (CppTool.Clangd, "clangd", null, "Install clangd from LLVM releases or your package manager."),
            (CppTool.Ninja, "Ninja", null, "Install Ninja from ninja-build.org or your package manager."),
            (CppTool.Gdb, "GDB", null, "Install GDB 14 or later from sourceware.org or your package manager."),
        ],
    };

    private static void Compilers(HashSet<CppTool> tools, List<CppInstallStep> steps, string? distribution)
    {
        if (!tools.Contains(CppTool.CCompiler) && !tools.Contains(CppTool.CxxCompiler))
        {
            return;
        }
        string? system = distribution switch
        {
            "debian" => "sudo apt install -y build-essential",
            "fedora" => "sudo dnf install -y gcc gcc-c++",
            "arch" => "sudo pacman -S --noconfirm base-devel",
            "suse" => "sudo zypper install -y gcc gcc-c++",
            "alpine" => "sudo apk add build-base",
            "macos" => "xcode-select --install",
            "windows" => "winget install Microsoft.VisualStudio.2022.BuildTools",
            _ => null,
        };
        steps.Add(new CppInstallStep(CppTool.CCompiler, "C and C++ compilers", system,
            "An external cross compiler (m68k-elf-gcc, m68k-amigaos-g++, m68k-atari-mint-g++ and the like) comes from "
            + "its own toolchain: install it, then name its full path in the wizard's compiler boxes."));
    }
}
