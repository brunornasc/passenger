using System.Text.RegularExpressions;
using Passenger.Core;

namespace Passenger.Workspace.Cpp;

/// <summary>A target a CMakeLists.txt declares, and the sources it names.</summary>
public sealed record CppTarget(string Name, string Kind, IReadOnlyList<string> Sources);

/// <summary>A C/C++ project in a folder: its CMakeLists.txt, its targets, the standards it names and its Conan recipe.</summary>
public sealed record CppProject(string Folder, string Path, string Name, IReadOnlyList<CppTarget> Targets, string? MinimumCmake,
    CStandard C, CxxStandard Cxx, string? Conan, IReadOnlyList<string> ConanRequires, IReadOnlyList<string> Subdirectories)
{
    /// <summary>The targets that make a program a person can run.</summary>
    public IReadOnlyList<CppTarget> Executables => [.. Targets.Where(t => t.Kind == "executable")];

    /// <summary>Whether a Conan recipe sits beside the CMakeLists.txt.</summary>
    public bool HasConan => Conan is not null;

    /// <summary>Whether the recipe asks for packages, so a configure has to run conan install first.</summary>
    public bool HasConanPackages => ConanRequires.Count > 0;

    /// <summary>The name of the executable to run when none was chosen: the first, else the project's own name.</summary>
    public string? FirstExecutable => Executables.FirstOrDefault()?.Name;

    /// <summary>What a walk's read compares, so an unchanged CMakeLists.txt does not reload the workspace.</summary>
    public string Signature => string.Join('|', Folder, Name, Path, MinimumCmake ?? string.Empty, C, Cxx, Conan ?? string.Empty,
        string.Join(',', ConanRequires), string.Join(',', Subdirectories),
        string.Join(';', Targets.Select(t => $"{t.Name}:{t.Kind}:{string.Join(',', t.Sources)}")));
}

/// <summary>Reads a CMakeLists.txt as CMake reads it for the project, its targets and its standards; a folder with only a
/// Conan recipe is a project with no targets yet. Nothing here starts CMake, so a workspace read stays quick.</summary>
public static partial class CMakeProject
{
    public const string FileName = "CMakeLists.txt";

    /// <summary>The Conan recipes a folder may hold, in the order Conan looks for them.</summary>
    public static readonly string[] ConanNames = ["conanfile.txt", "conanfile.py"];

    /// <summary>Whether a folder holds a C/C++ project of its own.</summary>
    public static bool IsAt(string folder) => File.Exists(Path.Combine(folder, FileName)) || ConanNames.Any(n => File.Exists(Path.Combine(folder, n)));

    /// <summary>The C/C++ project of the folder, null where none is there or its CMakeLists.txt cannot be read.</summary>
    public static CppProject? Read(string folder)
    {
        string path = Path.Combine(folder, FileName);
        string? text = null;
        try
        {
            if (File.Exists(path))
            {
                text = File.ReadAllText(path);
            }
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        string? conan = ConanNames.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists);
        if (text is null && conan is null)
        {
            return null;
        }
        string words = WithoutComments(text ?? string.Empty);
        string name = Project().Match(words) is { Success: true } found ? found.Groups[1].Value : Path.GetFileName(PathNames.Canonical(folder));
        List<CppTarget> targets = [];
        foreach (Match match in Executable().Matches(words))
        {
            targets.Add(new CppTarget(match.Groups[1].Value, "executable", Sources(match.Groups[2].Value)));
        }
        foreach (Match match in Library().Matches(words))
        {
            targets.Add(new CppTarget(match.Groups[1].Value, "library", Sources(match.Groups[3].Value)));
        }
        return new CppProject(PathNames.Canonical(folder), PathNames.Canonical(path), name, targets,
            Minimum().Match(words) is { Success: true } least ? least.Groups[1].Value : null,
            CppStandards.ReadC(StandardC().Match(words) is { Success: true } c ? c.Groups[1].Value : null),
            CppStandards.ReadCxx(StandardCxx().Match(words) is { Success: true } cxx ? cxx.Groups[1].Value : null),
            conan is null ? null : PathNames.Canonical(conan),
            Requires(conan, folder),
            [.. Subdirectory().Matches(words).Select(m => m.Groups[1].Value)]);
    }

    /// <summary>The C/C++ project a file or folder is in: the nearest folder above it holding a project of its own.</summary>
    public static string? Find(string path)
    {
        for (string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (IsAt(folder))
            {
                return PathNames.Canonical(folder);
            }
        }
        return null;
    }

    /// <summary>The packages the recipe asks for: a conan install is worth running only where there are some.</summary>
    private static IReadOnlyList<string> Requires(string? conan, string folder) =>
        conan is null ? [] : ConanFile.Read(folder)?.Requires ?? [];

    /// <summary>A CMake command's sources: words that are not a keyword or a CMake variable.</summary>
    private static IReadOnlyList<string> Sources(string words) =>
    [.. CppToolchain.Words(words).Where(w => !w.StartsWith('$') && w is not ("WIN32" or "MACOSX_BUNDLE" or "EXCLUDE_FROM_ALL" or "ALIAS" or "IMPORTED" or "STATIC" or "SHARED" or "MODULE" or "INTERFACE" or "OBJECT"))];

    private static string WithoutComments(string text) => string.Join('\n', text.Split('\n').Select(line => line.IndexOf('#') is var at && at >= 0 ? line[..at] : line));

    [GeneratedRegex(@"cmake_minimum_required\s*\(\s*VERSION\s+([0-9.]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Minimum();

    [GeneratedRegex(@"\bproject\s*\(\s*([A-Za-z_][\w.+-]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Project();

    [GeneratedRegex(@"\badd_executable\s*\(\s*([A-Za-z_][\w.+-]*)([^)]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex Executable();

    [GeneratedRegex(@"\badd_library\s*\(\s*([A-Za-z_][\w.+-]*)\s*(STATIC|SHARED|MODULE|INTERFACE|OBJECT)?([^)]*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex Library();

    [GeneratedRegex(@"\bset\s*\(\s*CMAKE_C_STANDARD\s+([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex StandardC();

    [GeneratedRegex(@"\bset\s*\(\s*CMAKE_CXX_STANDARD\s+([0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex StandardCxx();

    [GeneratedRegex(@"\badd_subdirectory\s*\(\s*([^\s)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Subdirectory();
}
