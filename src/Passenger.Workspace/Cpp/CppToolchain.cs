namespace Passenger.Workspace.Cpp;

/// <summary>Where the compilers come from: the system's own, a named cross toolchain, or paths a person typed.</summary>
public enum CppPreset
{
    System,
    Gcc,
    Clang,
    M68kElf,
    M68kAmigaOs,
    M68kAtariMint,
    Custom,
}

/// <summary>The CMake generator a build configures; Default lets CMake choose as it does from the command line.</summary>
public enum CppGenerator
{
    Default,
    Ninja,
    UnixMakefiles,
    NMakeMakefiles,
    VisualStudio,
}

/// <summary>The C/C++ toolchain: which compilers, which language standards, which target and which CMake and Conan
/// commands a project is configured, built and completed with. All commands are made here so a build, the wizard and
/// clangd agree on the one toolchain, and so they can be read without starting a process.</summary>
public sealed record CppToolchain
{
    /// <summary>The build folder, relative to the project's, where compile_commands.json and the binaries go.</summary>
    public const string DefaultBuildFolder = "cmake-build-debug";

    /// <summary>Where Conan's generators are written, under the build folder.</summary>
    public const string ConanFolder = "conan";

    public CppPreset Preset { get; init; } = CppPreset.System;
    public CStandard C { get; init; } = CStandard.Default;
    public CxxStandard Cxx { get; init; } = CxxStandard.Default;
    public string? CCompiler { get; init; }
    public string? CxxCompiler { get; init; }
    public string? CMake { get; init; }
    public string? Conan { get; init; }
    public string? Clangd { get; init; }
    public string? Gdb { get; init; }
    public CppGenerator Generator { get; init; } = CppGenerator.Ninja;
    public string BuildFolder { get; init; } = DefaultBuildFolder;
    public string Configuration { get; init; } = "Debug";
    public string? TargetTriple { get; init; }
    public string? Sysroot { get; init; }
    public string? ToolchainFile { get; init; }
    public string? ConanProfile { get; init; }
    public bool UseConan { get; init; } = true;
    public string? ExtraCMakeArguments { get; init; }

    /// <summary>The names Settings shows for the presets, in the order it offers them.</summary>
    public static readonly IReadOnlyList<(CppPreset Value, string Name)> Presets =
    [
        (CppPreset.System, "System (CMake's own choice)"),
        (CppPreset.Gcc, "GCC (gcc / g++)"),
        (CppPreset.Clang, "Clang (clang / clang++)"),
        (CppPreset.M68kElf, "m68k bare metal (m68k-elf-gcc)"),
        (CppPreset.M68kAmigaOs, "m68k AmigaOS (m68k-amigaos-g++)"),
        (CppPreset.M68kAtariMint, "m68k Atari MiNT (m68k-atari-mint-g++)"),
        (CppPreset.Custom, "Custom / external compilers…"),
    ];

    /// <summary>The names Settings shows for the generators, in the order it offers them.</summary>
    public static readonly IReadOnlyList<(CppGenerator Value, string Name)> Generators =
    [
        (CppGenerator.Default, "CMake's default"),
        (CppGenerator.Ninja, "Ninja"),
        (CppGenerator.UnixMakefiles, "Unix Makefiles"),
        (CppGenerator.NMakeMakefiles, "NMake Makefiles"),
        (CppGenerator.VisualStudio, "Visual Studio"),
    ];

    /// <summary>A toolchain of a preset, with the compilers and target it names; a person's paths go over its own.</summary>
    public static CppToolchain Of(CppPreset preset) => preset switch
    {
        CppPreset.Gcc => new CppToolchain { Preset = preset, CCompiler = "gcc", CxxCompiler = "g++" },
        CppPreset.Clang => new CppToolchain { Preset = preset, CCompiler = "clang", CxxCompiler = "clang++" },
        CppPreset.M68kElf => new CppToolchain { Preset = preset, CCompiler = "m68k-elf-gcc", CxxCompiler = "m68k-elf-g++", TargetTriple = "m68k-elf", UseConan = false },
        CppPreset.M68kAmigaOs => new CppToolchain { Preset = preset, CCompiler = "m68k-amigaos-gcc", CxxCompiler = "m68k-amigaos-g++", TargetTriple = "m68k-amigaos", UseConan = false },
        CppPreset.M68kAtariMint => new CppToolchain { Preset = preset, CCompiler = "m68k-atari-mint-gcc", CxxCompiler = "m68k-atari-mint-g++", TargetTriple = "m68k-atari-mint", UseConan = false },
        _ => new CppToolchain { Preset = preset },
    };

    /// <summary>Whether it names a cross compiler, whose system headers clangd must ask the compiler for.</summary>
    public bool IsCross => Preset is CppPreset.M68kElf or CppPreset.M68kAmigaOs or CppPreset.M68kAtariMint
        || Preset == CppPreset.Custom && CxxCompiler is { Length: > 0 };

    /// <summary>The program a tool is run as: the path Settings names, else its name on the PATH.</summary>
    public string Program(string? named, string fallback) => string.IsNullOrWhiteSpace(named) ? fallback : named.Trim();

    public string CMakeProgram => Program(CMake, "cmake");
    public string ConanProgram => Program(Conan, "conan");
    public string ClangdProgram => Program(Clangd, "clangd");
    public string GdbProgram => Program(Gdb, "gdb");

    /// <summary>The build folder under a project, as a full path; the folder a wizard and clangd both name.</summary>
    public string BuildPath(string source) => Path.GetFullPath(Path.Combine(source, BuildFolder));

    /// <summary>Where Conan writes its CMakeToolchain and CMakeDeps generators under the build folder.</summary>
    public string ConanPath(string source) => Path.Combine(BuildPath(source), ConanFolder);

    /// <summary>The CMake toolchain file a configure uses: the person's own, else Conan's where the recipe has packages.</summary>
    public string? Toolchain(string source, bool hasPackages) => !string.IsNullOrWhiteSpace(ToolchainFile) ? ToolchainFile.Trim()
        : UseConan && hasPackages ? Path.Combine(ConanPath(source), "conan_toolchain.cmake") : null;

    /// <summary>Conan 2's install command: its generators make the toolchain and the package configs CMake finds.</summary>
    public IReadOnlyList<string> ConanArguments(string source, string build) =>
    [
        "install", source, "--output-folder", Path.Combine(build, ConanFolder), "--build=missing",
        "-s", $"build_type={Configuration}", "-s", $"compiler.cppstd={CppStandards.CMakeValue(Cxx) ?? "17"}",
        .. ConanProfile is { Length: > 0 } profile ? (string[])["-pr", profile.Trim()] : [],
    ];

    /// <summary>The cmake configure: compilers, standards, cross target, sysroot, a toolchain file and a compilation
    /// database, which clangd reads.</summary>
    public IReadOnlyList<string> ConfigureArguments(string source, string build, bool hasPackages)
    {
        List<string> arguments =
        [
            "-S", source, "-B", build, "-DCMAKE_EXPORT_COMPILE_COMMANDS=ON",
            $"-DCMAKE_BUILD_TYPE={Configuration}",
        ];
        string? generator = GeneratorName();
        if (generator is not null)
        {
            arguments.AddRange(["-G", generator]);
        }
        if (CCompiler is { Length: > 0 } cc)
        {
            arguments.Add($"-DCMAKE_C_COMPILER={cc.Trim()}");
        }
        if (CxxCompiler is { Length: > 0 } cxx)
        {
            arguments.Add($"-DCMAKE_CXX_COMPILER={cxx.Trim()}");
        }
        if (CppStandards.CMakeValue(C) is { } c)
        {
            arguments.AddRange([$"-DCMAKE_C_STANDARD={c}", "-DCMAKE_C_STANDARD_REQUIRED=ON", "-DCMAKE_C_EXTENSIONS=OFF"]);
        }
        if (CppStandards.CMakeValue(Cxx) is { } cxxStandard)
        {
            arguments.AddRange([$"-DCMAKE_CXX_STANDARD={cxxStandard}", "-DCMAKE_CXX_STANDARD_REQUIRED=ON", "-DCMAKE_CXX_EXTENSIONS=OFF"]);
        }
        if (TargetTriple is { Length: > 0 } triple)
        {
            arguments.AddRange([$"-DCMAKE_C_COMPILER_TARGET={triple.Trim()}", $"-DCMAKE_CXX_COMPILER_TARGET={triple.Trim()}"]);
        }
        if (Sysroot is { Length: > 0 } root)
        {
            arguments.Add($"-DCMAKE_SYSROOT={root.Trim()}");
        }
        if (Toolchain(source, hasPackages) is { } file)
        {
            arguments.Add($"-DCMAKE_TOOLCHAIN_FILE={file}");
        }
        // Cached too, so a ninja re-run of CMake finds the Conan generators even where the toolchain was not loaded.
        if (hasPackages)
        {
            arguments.Add($"-DCMAKE_PREFIX_PATH={ConanPath(source)}");
        }
        arguments.AddRange(Words(ExtraCMakeArguments));
        return arguments;
    }

    /// <summary>The cmake build of a target, or every target where none is named.</summary>
    public IReadOnlyList<string> BuildArguments(string build, string? target = null) =>
    [
        "--build", build, "--config", Configuration, "--parallel",
        .. target is { Length: > 0 } named ? (string[])["--target", named] : [],
    ];

    /// <summary>clangd's arguments: the compilation database of the build folder, the compilers whose built-in headers it
    /// may ask for (a cross compiler's most of all), and an index kept in the background.</summary>
    public IReadOnlyList<string> ClangdArguments(string build) =>
    [
        "--background-index", "--compile-commands-dir=" + build, "--query-driver=" + QueryDriver(),
        "--completion-style=detailed", "--pch-storage=memory", "--header-insertion=never", "--log=error",
    ];

    /// <summary>The compiler globs clangd may run to learn their include paths; a named compiler comes first.</summary>
    public string QueryDriver()
    {
        List<string> drivers = [];
        foreach (string? compiler in (string?[])[CCompiler, CxxCompiler])
        {
            if (!string.IsNullOrWhiteSpace(compiler) && !drivers.Contains(compiler.Trim()))
            {
                drivers.Add(compiler.Trim());
            }
        }
        drivers.AddRange(OperatingSystem.IsWindows()
            ? (string[])["C:/Program Files/LLVM/bin/clang*", "C:/mingw*/bin/*g++*"]
            : (string[])["/usr/bin/*gcc*", "/usr/bin/*g++*", "/usr/bin/*clang*", "/usr/bin/*clang++*", "/usr/local/bin/*g++*", "/opt/*/bin/*g++*"]);
        return string.Join(',', drivers);
    }

    /// <summary>The flags clangd falls back to for every file: a cross target's own, but no -std, which a C file and a
    /// C++ file cannot share (the .clangd file names each language's own).</summary>
    public IReadOnlyList<string> ClangdFallbackFlags()
    {
        List<string> flags = [];
        if (TargetTriple is { Length: > 0 } triple)
        {
            flags.Add("--target=" + triple.Trim());
        }
        if (Sysroot is { Length: > 0 } root)
        {
            flags.Add("--sysroot=" + root.Trim());
        }
        return flags;
    }

    /// <summary>A .clangd file's words: a language's own standard by its file name, the cross target and sysroot for every
    /// file, and the compiler clangd asks for a cross target's headers.</summary>
    public string ClangdConfiguration()
    {
        List<string> shared = [.. ClangdFallbackFlags()];
        string compiler = string.IsNullOrWhiteSpace(CxxCompiler) ? string.Empty : $"  Compiler: {CxxCompiler!.Trim()}\n";
        return $"""
            If:
              PathMatch: '.*\.(c|h)$'
            CompileFlags:
              Add: {Flags(CppStandards.Flag(C))}
            ---
            If:
              PathMatch: '.*\.(cc|cp|cpp|cxx|c\+\+|hpp|hh|hxx|h\+\+|ipp|inl|tpp|ino)$'
            CompileFlags:
              Add: {Flags(CppStandards.Flag(Cxx))}
            ---
            CompileFlags:
              Add: {Flags(null)}
            {compiler}
            """;

        string Flags(string? standard) => standard is not null ? "[" + standard + "]"
            : shared.Count == 0 ? "[]" : "[" + string.Join(", ", shared) + "]";
    }

    /// <summary>The binary a CMake target makes, in the build folder (or its configuration folder on a multi-config generator).</summary>
    public string BinaryOf(string source, string target)
    {
        string build = BuildPath(source);
        string multi = Generator is CppGenerator.VisualStudio ? Path.Combine(build, Configuration) : build;
        return Path.Combine(multi, target + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
    }

    /// <summary>A signature of what changes clangd or a configure, so a settings change restarts the server only when it must.</summary>
    public string Signature() => string.Join('|', Preset, C, Cxx, CCompiler ?? string.Empty, CxxCompiler ?? string.Empty, Clangd ?? string.Empty,
        BuildFolder, Configuration, TargetTriple ?? string.Empty, Sysroot ?? string.Empty, ToolchainFile ?? string.Empty, Generator);

    private string? GeneratorName() => Generator switch
    {
        CppGenerator.Ninja => "Ninja",
        CppGenerator.UnixMakefiles => "Unix Makefiles",
        CppGenerator.NMakeMakefiles => "NMake Makefiles",
        CppGenerator.VisualStudio => "Visual Studio 17 2022",
        _ => null,
    };

    /// <summary>An extra arguments' command line split as a shell would, quotes held round a word with spaces.</summary>
    public static IReadOnlyList<string> Words(string? line)
    {
        List<string> words = [];
        if (string.IsNullOrWhiteSpace(line))
        {
            return words;
        }
        string held = string.Empty;
        char quote = '\0';
        foreach (char at in line.Trim())
        {
            if (quote != '\0')
            {
                if (at == quote)
                {
                    quote = '\0';
                }
                else
                {
                    held += at;
                }
            }
            else if (at is '"' or '\'')
            {
                quote = at;
            }
            else if (char.IsWhiteSpace(at))
            {
                Add(words, ref held);
            }
            else
            {
                held += at;
            }
        }
        Add(words, ref held);
        return words;

        static void Add(List<string> into, ref string held)
        {
            if (held.Length > 0)
            {
                into.Add(held);
                held = string.Empty;
            }
        }
    }
}
