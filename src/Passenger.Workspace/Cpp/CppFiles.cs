using System.Text.Json;
using System.Text.RegularExpressions;

namespace Passenger.Workspace.Cpp;

/// <summary>The files a new C or C++ project is written from, and the edits the Conan wizard makes to a project's
/// conanfile.txt and CMakeLists.txt. Conan 2 takes settings from profiles, never from a conanfile.txt section.</summary>
public static partial class CppFiles
{
    /// <summary>The project's files, each by its path from the project's folder.</summary>
    public static IReadOnlyList<(string Path, string Text)> For(string name, bool cxx, CppToolchain toolchain)
    {
        List<(string Path, string Text)> files =
        [
            (CMakeProject.FileName, Lists(name, cxx, toolchain)),
            (cxx ? "main.cpp" : "main.c", Source(name, cxx)),
            ("CMakePresets.json", Presets(name, cxx, toolchain)),
            (".clangd", toolchain.ClangdConfiguration()),
            (".gitignore", $"/{toolchain.BuildFolder}/\nCMakeUserPresets.json\n"),
        ];
        if (toolchain.UseConan)
        {
            files.Add(("conanfile.txt", Conan()));
        }
        return files;
    }

    private static string Lists(string name, bool cxx, CppToolchain toolchain)
    {
        string language = cxx ? "CXX" : "C";
        string standard = cxx ? $"set(CMAKE_CXX_STANDARD {CppStandards.CMakeValue(toolchain.Cxx) ?? "20"})\nset(CMAKE_CXX_STANDARD_REQUIRED ON)"
            : $"set(CMAKE_C_STANDARD {CppStandards.CMakeValue(toolchain.C) ?? "17"})\nset(CMAKE_C_STANDARD_REQUIRED ON)";
        return $"""
            cmake_minimum_required(VERSION 3.25)
            project({name} LANGUAGES {language})
            {standard}
            add_executable({name} {(cxx ? "main.cpp" : "main.c")})

            # Packages chosen in Tools | Install C/C++ dependencies with Conan are added below.
            """;
    }

    private static string Source(string name, bool cxx) => cxx
        ? $$"""
            #include <iostream>

            int main() {
                std::cout << "Hello from {{name}}!" << std::endl;
                return 0;
            }
            """
        : $$"""
            #include <stdio.h>

            int main(void) {
                printf("Hello from {{name}}!\n");
                return 0;
            }
            """;

    private static string Presets(string name, bool cxx, CppToolchain toolchain)
    {
        string generator = toolchain.Generator switch
        {
            CppGenerator.Ninja => "Ninja",
            CppGenerator.UnixMakefiles => "Unix Makefiles",
            CppGenerator.NMakeMakefiles => "NMake Makefiles",
            CppGenerator.VisualStudio => "Visual Studio 17 2022",
            _ => "Ninja",
        };
        string standard = cxx ? "CMAKE_CXX_STANDARD" : "CMAKE_C_STANDARD";
        object presets = new
        {
            version = 6,
            configurePresets = new[]
            {
                new
                {
                    name = "default",
                    displayName = name,
                    generator,
                    binaryDir = toolchain.BuildFolder,
                    cacheVariables = new Dictionary<string, string>
                    {
                        ["CMAKE_BUILD_TYPE"] = toolchain.Configuration,
                        ["CMAKE_EXPORT_COMPILE_COMMANDS"] = "ON",
                        [standard] = cxx ? CppStandards.CMakeValue(toolchain.Cxx) ?? "20" : CppStandards.CMakeValue(toolchain.C) ?? "17",
                    },
                },
            },
            buildPresets = new[] { new { name = "default", configurePreset = "default", configuration = toolchain.Configuration } },
        };
        return JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>A Conan 2 recipe with the packages given (or none), whose CMake generators Passenger's configure takes
    /// as its toolchain. Settings belong to a profile, so no [settings] section is written.</summary>
    public static string Conan(IReadOnlyList<string>? requires = null)
    {
        List<string> lines = ["[requires]"];
        lines.AddRange((requires ?? []).Where(r => r.Length > 0).Select(r => r.Trim()));
        lines.AddRange(["", "[generators]", "CMakeDeps", "CMakeToolchain", ""]);
        return string.Join('\n', lines);
    }

    /// <summary>A recipe with these packages in [requires], the other sections left as they were and an old [settings]
    /// section (which Conan 2 refuses) taken out; the CMake generators are added where the recipe lacks them.</summary>
    public static string WithRequires(string text, IReadOnlyList<string> requires)
    {
        List<string> lines = [.. text.Split('\n')];
        List<string> kept = [];
        string section = string.Empty;
        int at = -1;
        foreach (string line in lines)
        {
            string words = line.Trim();
            if (words.StartsWith('[') && words.EndsWith(']'))
            {
                section = words[1..^1].Trim();
                if (section.Equals("settings", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                kept.Add(line);
                if (section.Equals("requires", StringComparison.OrdinalIgnoreCase) && at < 0)
                {
                    at = kept.Count;
                }
                continue;
            }
            if (section.Equals("settings", StringComparison.OrdinalIgnoreCase)
                || section.Equals("requires", StringComparison.OrdinalIgnoreCase) && words.Length > 0 && !words.StartsWith('#'))
            {
                continue;
            }
            kept.Add(line);
        }
        if (at < 0)
        {
            kept.InsertRange(0, ["[requires]"]);
            at = 1;
        }
        kept.InsertRange(at, requires.Where(r => r.Length > 0).Select(r => r.Trim()));
        string joined = string.Join('\n', kept);
        if (!joined.Contains("CMakeDeps", StringComparison.OrdinalIgnoreCase))
        {
            joined = joined.TrimEnd() + "\n\n[generators]\nCMakeDeps\nCMakeToolchain\n";
        }
        return joined;
    }

    /// <summary>A CMakeLists.txt with find_package() for each package and a target_link_libraries() line for the first
    /// executable, as the Conan wizard writes them. Lines already there are left alone, so a second install adds only
    /// what is new.</summary>
    public static string WithPackages(string text, IReadOnlyList<string> findPackages, IReadOnlyList<string> linkTargets)
    {
        List<string> lines = [.. text.Split('\n')];
        string? target = lines.Select(l => Executable().Match(l)).FirstOrDefault(m => m.Success)?.Groups[1].Value;
        List<string> finds = [.. findPackages.Where(p => p.Length > 0 && !lines.Any(l => l.Contains($"find_package({p}", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        int firstExecutable = lines.FindIndex(l => Executable().IsMatch(l));
        if (finds.Count > 0)
        {
            int where = firstExecutable < 0 ? lines.Count : firstExecutable;
            lines.InsertRange(where, finds.Select(p => $"find_package({p} REQUIRED)"));
        }
        List<string> targets = [.. linkTargets.Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (targets.Count > 0 && target is not null)
        {
            int linked = lines.FindIndex(l => l.Contains($"target_link_libraries({target}", StringComparison.OrdinalIgnoreCase));
            List<string> missing = [.. targets.Where(t => !lines.Any(l => l.Contains(t, StringComparison.Ordinal)))];
            if (missing.Count == 0)
            {
                return string.Join('\n', lines);
            }
            if (linked >= 0)
            {
                int close = lines[linked].LastIndexOf(')');
                lines[linked] = close < 0 ? lines[linked] + " " + string.Join(' ', missing)
                    : lines[linked].Insert(close, " " + string.Join(' ', missing));
            }
            else
            {
                lines.Add($"target_link_libraries({target} PRIVATE {string.Join(' ', targets)})");
            }
        }
        return string.Join('\n', lines);
    }

    [GeneratedRegex(@"\badd_executable\s*\(\s*([A-Za-z_][\w.+-]*)", RegexOptions.IgnoreCase)]
    private static partial Regex Executable();
}
