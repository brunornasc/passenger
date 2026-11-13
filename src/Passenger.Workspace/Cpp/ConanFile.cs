using System.Text.RegularExpressions;
using Passenger.Core;

namespace Passenger.Workspace.Cpp;

/// <summary>A Conan recipe's parts: the packages it asks for, the generators it writes and the settings it builds for.</summary>
public sealed record ConanRecipe(string Path, bool Python, IReadOnlyList<string> Requires, IReadOnlyList<string> Generators,
    IReadOnlyList<string> Options, IReadOnlyList<string> Settings, string? Layout)
{
    /// <summary>Whether it writes the CMake generators a CMake configure takes as its toolchain.</summary>
    public bool UsesCMakeToolchain => Generators.Any(g => g.Contains("CMakeToolchain", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads conanfile.txt's sections, or the assignments of a conanfile.py, without running Conan.</summary>
public static partial class ConanFile
{
    /// <summary>The recipe in a folder, null where none is there or it cannot be read.</summary>
    public static ConanRecipe? Read(string folder)
    {
        foreach (string name in CMakeProject.ConanNames)
        {
            string path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
                continue;
            }
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
            {
                return null;
            }
            return name.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ? Python(path, text) : Sections(path, text);
        }
        return null;
    }

    /// <summary>A conanfile.txt's [section] lines, comments and blank lines passed over.</summary>
    private static ConanRecipe Sections(string path, string text)
    {
        Dictionary<string, List<string>> sections = new(StringComparer.OrdinalIgnoreCase);
        string section = string.Empty;
        foreach (string line in text.Split('\n'))
        {
            string words = line.Trim();
            if (words.Length == 0 || words.StartsWith('#'))
            {
                continue;
            }
            if (words.StartsWith('[') && words.EndsWith(']'))
            {
                section = words[1..^1].Trim();
                if (!sections.ContainsKey(section))
                {
                    sections[section] = [];
                }
            }
            else if (section.Length > 0)
            {
                sections[section].Add(words);
            }
        }
        return new ConanRecipe(PathNames.Canonical(path), false,
            Taken(sections, "requires"), Taken(sections, "generators"), Taken(sections, "options"), Taken(sections, "settings"),
            Taken(sections, "layout").FirstOrDefault());
    }

    /// <summary>A conanfile.py's assignments, whose values are strings, tuples or lists.</summary>
    private static ConanRecipe Python(string path, string text) => new(PathNames.Canonical(path), true,
        Values(text, "requires"), Values(text, "generators"), Values(text, "default_options"), Values(text, "settings"),
        CMakeLayout().IsMatch(text) ? "cmake_layout" : null);

    private static IReadOnlyList<string> Values(string text, string name)
    {
        foreach (Match match in Assignment().Matches(text))
        {
            if (!match.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string held = match.Groups["tuple"].Success ? match.Groups["tuple"].Value : match.Groups["list"].Success ? match.Groups["list"].Value
                : match.Groups["single"].Success ? match.Groups["single"].Value : match.Groups["other"].Value;
            return [.. held.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => v.Trim().Trim('"', '\'', ' ')).Where(v => v.Length > 0)];
        }
        return [];
    }

    private static IReadOnlyList<string> Taken(Dictionary<string, List<string>> sections, string name) =>
        sections.TryGetValue(name, out List<string>? lines) ? [.. lines] : [];

    [GeneratedRegex(@"\b(?<name>requires|generators|default_options|settings)\s*=\s*(?:\((?<tuple>[^)]*)\)|\[(?<list>[^\]]*)\]|""(?<single>[^""]*)""|'(?<other>[^']*)')",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\bcmake_layout\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex CMakeLayout();
}
