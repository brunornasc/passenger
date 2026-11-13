namespace Passenger.Workspace.Cpp;

/// <summary>The C standard a CMake project builds with; Default leaves the project's own CMakeLists.txt to say it.</summary>
public enum CStandard
{
    Default,
    C89,
    C99,
    C11,
    C17,
    C23,
}

/// <summary>The C++ standard a CMake project builds with; Default leaves the project's own CMakeLists.txt to say it.</summary>
public enum CxxStandard
{
    Default,
    Cxx11,
    Cxx14,
    Cxx17,
    Cxx20,
    Cxx23,
    Cxx26,
}

/// <summary>The C and C++ standards Settings offers, as CMake's version numbers and a compiler's <c>-std=</c> flag.</summary>
public static class CppStandards
{
    public static readonly IReadOnlyList<(CStandard Value, string Name)> CChoices =
    [
        (CStandard.Default, "From CMakeLists.txt"),
        (CStandard.C89, "C89 / C90"),
        (CStandard.C99, "C99"),
        (CStandard.C11, "C11"),
        (CStandard.C17, "C17"),
        (CStandard.C23, "C23"),
    ];

    public static readonly IReadOnlyList<(CxxStandard Value, string Name)> CxxChoices =
    [
        (CxxStandard.Default, "From CMakeLists.txt"),
        (CxxStandard.Cxx11, "C++11"),
        (CxxStandard.Cxx14, "C++14"),
        (CxxStandard.Cxx17, "C++17"),
        (CxxStandard.Cxx20, "C++20"),
        (CxxStandard.Cxx23, "C++23"),
        (CxxStandard.Cxx26, "C++26"),
    ];

    /// <summary>CMake's CMAKE_C_STANDARD value; null where the project's own setting stands.</summary>
    public static string? CMakeValue(CStandard standard) => standard switch
    {
        CStandard.C89 => "90",
        CStandard.C99 => "99",
        CStandard.C11 => "11",
        CStandard.C17 => "17",
        CStandard.C23 => "23",
        _ => null,
    };

    /// <summary>CMake's CMAKE_CXX_STANDARD value; null where the project's own setting stands.</summary>
    public static string? CMakeValue(CxxStandard standard) => standard switch
    {
        CxxStandard.Cxx11 => "11",
        CxxStandard.Cxx14 => "14",
        CxxStandard.Cxx17 => "17",
        CxxStandard.Cxx20 => "20",
        CxxStandard.Cxx23 => "23",
        CxxStandard.Cxx26 => "26",
        _ => null,
    };

    /// <summary>The flag clangd falls back to where a compilation database has no entry yet.</summary>
    public static string? Flag(CStandard standard) => CMakeValue(standard) is { } number ? $"-std=c{number}" : null;

    /// <summary>The flag clangd falls back to where a compilation database has no entry yet.</summary>
    public static string? Flag(CxxStandard standard) => CMakeValue(standard) is { } number ? $"-std=c++{number}" : null;

    /// <summary>The name Settings shows for a C standard.</summary>
    public static string Name(CStandard standard) => CChoices.FirstOrDefault(c => c.Value == standard).Name ?? CChoices[0].Name;

    /// <summary>The name Settings shows for a C++ standard.</summary>
    public static string Name(CxxStandard standard) => CxxChoices.FirstOrDefault(c => c.Value == standard).Name ?? CxxChoices[0].Name;

    /// <summary>A standard read from a CMakeLists.txt's CMAKE_C_STANDARD, or Default for one it does not name.</summary>
    public static CStandard ReadC(string? number) => number?.Trim() switch
    {
        "90" or "89" => CStandard.C89,
        "99" => CStandard.C99,
        "11" => CStandard.C11,
        "17" => CStandard.C17,
        "23" => CStandard.C23,
        _ => CStandard.Default,
    };

    /// <summary>A standard read from a CMakeLists.txt's CMAKE_CXX_STANDARD, or Default for one it does not name.</summary>
    public static CxxStandard ReadCxx(string? number) => number?.Trim() switch
    {
        "11" => CxxStandard.Cxx11,
        "14" => CxxStandard.Cxx14,
        "17" => CxxStandard.Cxx17,
        "20" => CxxStandard.Cxx20,
        "23" => CxxStandard.Cxx23,
        "26" => CxxStandard.Cxx26,
        _ => CxxStandard.Default,
    };
}
