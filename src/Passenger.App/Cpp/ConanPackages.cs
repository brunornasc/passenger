using System.Net.Http;
using System.Text.Json;
using Passenger.Core;
using Passenger.Text;
using Passenger.Workspace.Cpp;

namespace Passenger.App.Cpp;

/// <summary>What a change to a project's Conan packages came to: whether it finished, everything Conan and CMake wrote,
/// and, where it did not, what stopped it in a line.</summary>
public sealed record ConanChange(bool Done, string Output, string? Problem = null);

/// <summary>The folder's Conan Center packages, whoever asks of it (the Conan window, the wizard): the recipes the center
/// publishes, what its pages say of them, and the one way a package goes into, moves in or comes out of a project's
/// conanfile.txt, through conan install and CMake's own configure. A change runs one at a time; a UI-thread object.</summary>
public sealed class ConanPackages
{
    private readonly Func<IReadOnlyList<CppProject>> projects;
    private readonly Func<CppToolchain> toolchain;
    private readonly Dictionary<string, ConanDetails?> details = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ConanPackage>? recipes;
    private bool busy;

    /// <param name="projects">The folder's C/C++ projects, read each time they are asked for so an opened folder's are
    /// the ones changed.</param>
    /// <param name="toolchain">The toolchain in force, read as a change begins.</param>
    public ConanPackages(Func<IReadOnlyList<CppProject>> projects, Func<CppToolchain> toolchain)
    {
        this.projects = projects;
        this.toolchain = toolchain;
    }

    /// <summary>The folder's C/C++ projects, each with its own recipe.</summary>
    public IReadOnlyList<CppProject> Projects => projects();

    /// <summary>Whether a package change is under way, so another waits.</summary>
    public bool IsBusy => busy;

    /// <summary>The recipe list as last read, none until then.</summary>
    public IReadOnlyList<ConanPackage> Known => recipes ?? [];

    /// <summary>The packages a project's recipe asks for.</summary>
    public static IReadOnlyList<string> Requires(CppProject project) => ConanFile.Read(project.Folder)?.Requires ?? [];

    /// <summary>Every recipe Conan Center publishes, read once and kept until a refresh asks again.</summary>
    public async Task<IReadOnlyList<ConanPackage>> RecipesAsync(bool again, CancellationToken cancel)
    {
        if (!again && recipes is { } known)
        {
            return known;
        }
        try
        {
            IReadOnlyList<ConanPackage> found = await BackgroundWork.Run(() => ReadRecipesAsync(cancel), cancel);
            if (found.Count > 0)
            {
                recipes = found;
            }
            return found;
        }
        catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return recipes ?? [];
        }
    }

    /// <summary>What conan.io/center says of a recipe, read once and kept.</summary>
    public async Task<ConanDetails?> DetailsAsync(string name, CancellationToken cancel)
    {
        if (details.TryGetValue(name, out ConanDetails? known))
        {
            return known;
        }
        ConanDetails? found = await ReadDetailsAsync(name, cancel);
        if (found is not null)
        {
            details[name] = found;
        }
        return found;
    }

    /// <summary>Puts a recipe and version in a project's conanfile.txt, installs it and adds what CMakeDeps names to
    /// CMakeLists.txt, then configures the project. Nothing is written where the recipe is a conanfile.py.</summary>
    public async Task<ConanChange> InstallAsync(CppProject project, string name, string version, CancellationToken cancel)
    {
        if (Python(project) is { } refusal)
        {
            return refusal;
        }
        IReadOnlyList<string> requires = [.. Requires(project).Where(r => !SameRecipe(r, name)), ConanCenter.Requires(name, version)];
        return await ChangeAsync(project, requires, removed: null, cancel);
    }

    /// <summary>Takes a recipe out of a project's conanfile.txt and its find_package() and linked targets out of
    /// CMakeLists.txt, installs what is left and configures the project.</summary>
    public async Task<ConanChange> RemoveAsync(CppProject project, string name, CancellationToken cancel)
    {
        if (Python(project) is { } refusal)
        {
            return refusal;
        }
        IReadOnlyList<string> requires = [.. Requires(project).Where(r => !SameRecipe(r, name))];
        if (requires.Count == Requires(project).Count)
        {
            return new ConanChange(false, string.Empty, $"{project.Name} does not ask for {name}");
        }
        return await ChangeAsync(project, requires, removed: name, cancel);
    }

    /// <summary>Writes the recipe, installs it, edits CMakeLists.txt and configures the project, all as the wizard does.
    /// A recipe taken out has its find_package() and linked targets removed too, before the rest are found and linked
    /// again, so a removal never leaves a package CMake would look for.</summary>
    private async Task<ConanChange> ChangeAsync(CppProject project, IReadOnlyList<string> requires, string? removed, CancellationToken cancel)
    {
        if (busy)
        {
            return new ConanChange(false, string.Empty, "another Conan change is under way");
        }
        busy = true;
        try
        {
            string recipe = Path.Combine(project.Folder, "conanfile.txt");
            string before = File.Exists(recipe) ? File.ReadAllText(recipe) : CppFiles.Conan();
            Save(recipe, CppFiles.WithRequires(before, requires));
            CppToolchain chain = toolchain();
            CppCommandResult installed = await CppCommands.ConanWithProfileAsync(project, chain, cancel);
            if (!installed.Succeeded)
            {
                return new ConanChange(false, installed.Output, "conan install failed; its output says why");
            }
            (IReadOnlyList<string> find, IReadOnlyList<string> link) = ConanCenter.CmakeLines(installed.Output);
            if (find.Count == 0)
            {
                find = [.. requires.Select(r => r.Split('/')[0])];
            }
            // Only the targets of the packages the recipe now asks for: the generators folder may still hold an earlier recipe's files.
            IReadOnlyList<string> wanted = [.. requires.Select(r => r.Split('/')[0]), .. find];
            link = [.. link.Concat(ConanCenter.GeneratedTargets(chain.ConanPath(project.Folder))
                .Where(t => wanted.Any(w => t.StartsWith(w + "::", StringComparison.OrdinalIgnoreCase))))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
            string lists = Path.Combine(project.Folder, CMakeProject.FileName);
            if (!File.Exists(lists))
            {
                // A recipe alone, with no CMakeLists.txt to find or link the package in: conan install is the whole change.
                return new ConanChange(true, installed.Output);
            }
            string text = File.ReadAllText(lists);
            if (removed is { } gone)
            {
                text = CppFiles.WithoutPackages(text, gone);
            }
            if (requires.Count > 0)
            {
                text = CppFiles.WithPackages(text, find, link);
            }
            Save(lists, text);
            CppProject installedProject = CMakeProject.Read(project.Folder) ?? project;
            CppCommandResult configured = await CppCommands.EnsureConfiguredAsync(installedProject, chain, cancel);
            string output = installed.Output + configured.Output;
            return configured.Succeeded
                ? new ConanChange(true, output)
                : new ConanChange(false, output, "installed, but the CMake configure failed; its output says why");
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            return new ConanChange(false, string.Empty, failed.Message);
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>Why a conanfile.py cannot be changed here, or null where the recipe is a conanfile.txt (or there is none
    /// yet, which this writes as one).</summary>
    private static ConanChange? Python(CppProject project) =>
        project.Conan is { } path && Path.GetFileName(path).Equals("conanfile.py", StringComparison.OrdinalIgnoreCase)
            ? new ConanChange(false, string.Empty, $"{project.Name} has a conanfile.py; edit it by hand")
            : null;

    private static bool SameRecipe(string reference, string name) =>
        ConanCenter.Split(reference) is { } parts && parts.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static void Save(string path, string text)
    {
        FileText? file = FileText.Load(path);
        if (file is null)
        {
            FileText.SaveNew(path, text);
        }
        else
        {
            FileText.SaveOver(path, file, text);
        }
    }

    /// <summary>Every recipe the Conan 2 remote lists, falling back to the older remote and to the conan CLI.</summary>
    public static async Task<IReadOnlyList<ConanPackage>> ReadRecipesAsync(CancellationToken cancel)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
        foreach (string url in (string[])[ConanCenter.RecipesUrl, ConanCenter.FallbackUrl])
        {
            try
            {
                if (ConanCenter.Recipes(await client.GetStringAsync(url, cancel)) is { Count: > 0 } found)
                {
                    return found;
                }
            }
            catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException or JsonException)
            {
                // The other remote, or the conan CLI, may still answer.
            }
        }
        ChildResult listed = await ChildProcesses.RunAsync(ChildProcesses.Command("conan", ["list", "*", "-r=conancenter"]),
            limit: TimeSpan.FromSeconds(60), cancel: cancel);
        return listed.Succeeded ? ConanCenter.Listed(listed.Output) : [];
    }

    /// <summary>A recipe's own page on conan.io/center, read for the words it shows.</summary>
    public static async Task<ConanDetails?> ReadDetailsAsync(string name, CancellationToken cancel)
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(20) };
            return ConanCenter.Details(await client.GetStringAsync(ConanCenter.DetailsUrl(name), cancel));
        }
        catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return null;
        }
    }
}
