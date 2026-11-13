using System.Diagnostics;
using System.Text;
using Passenger.Core;
using Passenger.Workspace.Cpp;

namespace Passenger.App.Cpp;

/// <summary>How a C/C++ command ended and everything it wrote, shown in the Build window or the wizard.</summary>
public sealed record CppCommandResult(bool Succeeded, string Output);

/// <summary>The CMake, Conan, build and run commands of a C/C++ project, all through the one toolchain so the wizard's
/// review, the Tools menu and clangd name the same compilers, standards and build folder.</summary>
public static class CppCommands
{
    /// <summary>Whether the recipe on disk asks for packages: the workspace's copy may not have been re-read yet after
    /// the wizard wrote conanfile.txt, so a cached HasConanPackages is not enough.</summary>
    private static bool HasPackages(CppProject project, CppToolchain toolchain) =>
        toolchain.UseConan && ConanFile.Read(project.Folder) is { Requires.Count: > 0 };

    /// <summary>Conan's install and CMake's configure, the latter taking Conan's toolchain where the recipe has packages.
    /// A recipe with no packages needs no conan install at all, so Run and Debug never invoke Conan for a new project.</summary>
    public static Task<CppCommandResult> ConfigureAsync(CppProject project, CppToolchain toolchain, CancellationToken cancel)
    {
        string source = project.Folder;
        string build = toolchain.BuildPath(source);
        Directory.CreateDirectory(build);
        bool packages = HasPackages(project, toolchain);
        List<(string Label, ProcessStartInfo Start)> steps = [];
        if (packages)
        {
            IReadOnlyList<string> conan = toolchain.ConanArguments(source, build);
            steps.Add(($"conan {string.Join(' ', conan)}", ChildProcesses.Command(toolchain.ConanProgram, conan, source)));
        }
        IReadOnlyList<string> configure = toolchain.ConfigureArguments(source, build, packages);
        steps.Add(($"cmake {string.Join(' ', configure)}", ChildProcesses.Command(toolchain.CMakeProgram, configure, source)));
        return StepsAsync(steps, source, cancel);
    }

    /// <summary>Only Conan's install, when the person asks for it after changing a recipe.</summary>
    public static Task<CppCommandResult> ConanAsync(CppProject project, CppToolchain toolchain, CancellationToken cancel)
    {
        string source = project.Folder;
        string build = toolchain.BuildPath(source);
        Directory.CreateDirectory(build);
        IReadOnlyList<string> arguments = toolchain.ConanArguments(source, build);
        return StepsAsync([($"conan {string.Join(' ', arguments)}", ChildProcesses.Command(toolchain.ConanProgram, arguments, source))], source, cancel);
    }

    /// <summary>A Conan install that detects a default profile where the cache has none, then tries again.</summary>
    public static async Task<CppCommandResult> ConanWithProfileAsync(CppProject project, CppToolchain toolchain, CancellationToken cancel)
    {
        CppCommandResult result = await ConanAsync(project, toolchain, cancel);
        if (result.Succeeded || !result.Output.Contains("profile", StringComparison.OrdinalIgnoreCase))
        {
            return result;
        }
        ChildResult detected = await BackgroundWork.Run(() => ChildProcesses.RunAsync(
            ChildProcesses.Command(toolchain.ConanProgram, ["profile", "detect", "--force"], project.Folder), cancel: cancel), cancel);
        if (!detected.Succeeded)
        {
            return new CppCommandResult(false, result.Output + "\n" + detected.Error);
        }
        CppCommandResult again = await ConanAsync(project, toolchain, cancel);
        return new CppCommandResult(again.Succeeded, result.Output + "\n" + again.Output);
    }

    /// <summary>A build folder is usable only where it has a cache, and where that cache was made with the Conan
    /// toolchain the recipe's packages now need: CMake ignores a toolchain added to an existing cache, so a stale one
    /// is taken out and the project is configured afresh.</summary>
    public static async Task<CppCommandResult> EnsureConfiguredAsync(CppProject project, CppToolchain toolchain, CancellationToken cancel)
    {
        string build = toolchain.BuildPath(project.Folder);
        Directory.CreateDirectory(build);
        string cache = Path.Combine(build, "CMakeCache.txt");
        bool packages = HasPackages(project, toolchain);
        if (File.Exists(cache) && (!packages || CacheNames(cache, "conan_toolchain.cmake")))
        {
            return new CppCommandResult(true, string.Empty);
        }
        if (File.Exists(cache))
        {
            try
            {
                File.Delete(cache);
            }
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
            {
                return new CppCommandResult(false, failed.Message);
            }
        }
        return await ConfigureAsync(project, toolchain, cancel);
    }

    private static bool CacheNames(string cache, string words)
    {
        try
        {
            return File.ReadAllText(cache).Contains(words, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>cmake --build of a target, or every target where none is named; a project never configured yet, or one
    /// whose packages changed, is configured first so CMake: Build works on a fresh clone as Run and Debug do.</summary>
    public static async Task<CppCommandResult> BuildAsync(CppProject project, CppToolchain toolchain, string? target, CancellationToken cancel)
    {
        CppCommandResult configured = await EnsureConfiguredAsync(project, toolchain, cancel);
        if (!configured.Succeeded)
        {
            return configured;
        }
        string build = toolchain.BuildPath(project.Folder);
        IReadOnlyList<string> arguments = toolchain.BuildArguments(build, target);
        CppCommandResult built = await StepsAsync([($"cmake {string.Join(' ', arguments)}", ChildProcesses.Command(toolchain.CMakeProgram, arguments, project.Folder))],
            project.Folder, cancel);
        return new CppCommandResult(built.Succeeded, configured.Output + built.Output);
    }

    /// <summary>A CMake target's binary, run in the project's folder; the build is a command of its own before this.</summary>
    public static Task<CppCommandResult> RunAsync(CppProject project, CppToolchain toolchain, string target, CancellationToken cancel)
    {
        string program = toolchain.BinaryOf(project.Folder, target);
        return StepsAsync([(program, ChildProcesses.Command(program, [], project.Folder))], project.Folder, cancel);
    }

    /// <summary>The commands the toolchain will run, as the wizard's last step says them before anything is saved.</summary>
    public static string Review(CppProject? project, CppToolchain toolchain)
    {
        string source = project?.Folder ?? "<the C/C++ project>";
        string build = toolchain.BuildPath(source);
        StringBuilder words = new();
        words.AppendLine($"Build folder: {build}");
        bool packages = project is not null && HasPackages(project, toolchain);
        if (packages)
        {
            words.AppendLine($"Conan: {toolchain.ConanProgram} {string.Join(' ', toolchain.ConanArguments(source, build))}");
        }
        words.AppendLine($"CMake: {toolchain.CMakeProgram} {string.Join(' ', toolchain.ConfigureArguments(source, build, packages))}");
        words.AppendLine($"Build: {toolchain.CMakeProgram} {string.Join(' ', toolchain.BuildArguments(build))}");
        words.AppendLine($"clangd: {toolchain.ClangdProgram} {string.Join(' ', toolchain.ClangdArguments(build))}");
        return words.ToString();
    }

    private static async Task<CppCommandResult> StepsAsync(IReadOnlyList<(string Label, ProcessStartInfo Start)> steps, string folder, CancellationToken cancel)
    {
        StringBuilder output = new();
        foreach ((string label, ProcessStartInfo start) in steps)
        {
            output.AppendLine($"> {label}");
            try
            {
                ChildResult result = await BackgroundWork.Run(() => ChildProcesses.RunAsync(start, cancel: cancel), cancel);
                output.Append(result.Output);
                if (result.Error.Length > 0)
                {
                    output.AppendLine().Append(result.Error);
                }
                if (!result.Succeeded)
                {
                    output.AppendLine($"Ended with {ExitCodes.Said(result.ExitCode)}");
                    return new CppCommandResult(false, output.ToString());
                }
            }
            catch (OperationCanceledException)
            {
                output.AppendLine("Stopped");
                return new CppCommandResult(false, output.ToString());
            }
            catch (Exception failed) when (ChildFailures.Of(failed))
            {
                output.AppendLine(failed.Message);
                return new CppCommandResult(false, output.ToString());
            }
        }
        return new CppCommandResult(true, output.ToString());
    }
}
