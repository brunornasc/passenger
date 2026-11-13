using System.Text.Json;
using System.Text.RegularExpressions;
using Passenger.Core;

namespace Passenger.Workspace.Cpp;

/// <summary>A recipe on Conan Center and the versions of it that are published.</summary>
public sealed record ConanPackage(string Name, IReadOnlyList<string> Versions);

/// <summary>The Conan Center remote's recipe list, and the CMake words a conan install prints for CMakeDeps. All of it
/// read without starting a process, so the wizard's list and its CMake edits can be tested.</summary>
public static partial class ConanCenter
{
    /// <summary>The Conan 2 remote the installed conan client names, asked for every recipe.</summary>
    public const string RecipesUrl = "https://center2.conan.io/v2/conans/search?q=*";

    /// <summary>The older remote, tried where the first cannot be reached.</summary>
    public const string FallbackUrl = "https://center.conan.io/v2/conans/search?q=*";

    /// <summary>Where a person browses the packages the wizard lists.</summary>
    public const string Browse = "https://conan.io/center";

    /// <summary>The refs a /v2/conans/search answer gives, grouped by recipe name with its newest versions first.</summary>
    public static IReadOnlyList<ConanPackage> Recipes(string json)
    {
        using JsonDocument read = JsonDocument.Parse(json);
        IReadOnlyList<JsonElement> results = JsonMembers.Items(read.RootElement, "results");
        if (results.Count == 0)
        {
            return [];
        }
        Dictionary<string, List<string>> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement result in results)
        {
            if (result.ValueKind != JsonValueKind.String || Ref().Match(result.GetString() ?? string.Empty) is not { Success: true } found)
            {
                continue;
            }
            if (!byName.TryGetValue(found.Groups[1].Value, out List<string>? versions))
            {
                byName[found.Groups[1].Value] = versions = [];
            }
            versions.Add(found.Groups[2].Value);
        }
        return [.. byName.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ConanPackage(p.Key, [.. p.Value.Distinct(StringComparer.OrdinalIgnoreCase).OrderDescending(StringComparer.OrdinalIgnoreCase)]))];
    }

    /// <summary>The refs a `conan list "*" -r=conancenter` prints, as a fallback where the web API is unreachable.</summary>
    public static IReadOnlyList<ConanPackage> Listed(string output)
    {
        Dictionary<string, List<string>> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in output.Split('\n'))
        {
            if (Ref().Match(line.Trim()) is { Success: true } found)
            {
                if (!byName.TryGetValue(found.Groups[1].Value, out List<string>? versions))
                {
                    byName[found.Groups[1].Value] = versions = [];
                }
                versions.Add(found.Groups[2].Value);
            }
        }
        return [.. byName.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new ConanPackage(p.Key, [.. p.Value.Distinct(StringComparer.OrdinalIgnoreCase).OrderDescending(StringComparer.OrdinalIgnoreCase)]))];
    }

    /// <summary>A package reference as conanfile.txt's [requires] writes it.</summary>
    public static string Requires(string name, string version) => $"{name}/{version}";

    /// <summary>Whether a reference typed by hand names a recipe and a version, as conanfile.txt takes them.</summary>
    public static bool Valid(string reference) => Ref().Match(reference.Trim()) is { Success: true } found
        && found.Groups[2].Value.Length > 0;

    /// <summary>The find_package names and CMake targets conan install says CMakeDeps made, read from its own words.</summary>
    public static (IReadOnlyList<string> Find, IReadOnlyList<string> Link) CmakeLines(string output)
    {
        List<string> find = [];
        List<string> link = [];
        bool inside = false;
        foreach (string line in output.Split('\n'))
        {
            string words = line.Trim();
            if (words.Contains("CMakeDeps necessary", StringComparison.OrdinalIgnoreCase))
            {
                inside = true;
                continue;
            }
            if (!inside)
            {
                continue;
            }
            if (words.StartsWith("find_package(", StringComparison.OrdinalIgnoreCase) && Find().Match(words) is { Success: true } package)
            {
                find.Add(package.Groups[1].Value);
            }
            else if (words.StartsWith("target_link_libraries(", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match target in Target().Matches(words))
                {
                    link.Add(target.Value);
                }
            }
            else if (words.StartsWith("Generator 'CMakeToolchain'", StringComparison.OrdinalIgnoreCase) || words.StartsWith(">>>", StringComparison.Ordinal))
            {
                inside = false;
            }
        }
        return ([.. find.Distinct(StringComparer.OrdinalIgnoreCase)], [.. link.Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>The imported targets declared by generated *Targets.cmake files, where conan's own words are missing.</summary>
    public static IReadOnlyList<string> GeneratedTargets(string folder)
    {
        List<string> targets = [];
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*Targets.cmake").Concat(Directory.EnumerateFiles(folder, "*Target-*.cmake")))
            {
                foreach (Match match in Imported().Matches(File.ReadAllText(file)))
                {
                    if (match.Groups[1].Value.Contains("::", StringComparison.Ordinal))
                    {
                        targets.Add(match.Groups[1].Value);
                    }
                }
            }
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return [.. targets.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    [GeneratedRegex(@"^\s*([A-Za-z0-9_.+-]+)/([A-Za-z0-9_.+-]+)(?:@[^/\s]+/[^/\s]+)?\s*$")]
    private static partial Regex Ref();

    [GeneratedRegex(@"^find_package\(\s*([A-Za-z0-9_.+-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Find();

    [GeneratedRegex(@"\b[A-Za-z0-9_.+-]+::[A-Za-z0-9_.+-]+")]
    private static partial Regex Target();

    [GeneratedRegex(@"add_library\(\s*([A-Za-z0-9_.+:-]+)\s+(?:INTERFACE|STATIC|SHARED|MODULE|UNKNOWN)?\s*IMPORTED", RegexOptions.IgnoreCase)]
    private static partial Regex Imported();
}
