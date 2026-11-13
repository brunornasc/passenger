using System.Diagnostics;
using Passenger.App.Settings;
using Passenger.Core;
using Passenger.Lsp;
using Passenger.Workspace.Cpp;

namespace Passenger.App.Cpp;

/// <summary>clangd over a C/C++ project's compile_commands.json: one server for the project a file is in, told the build
/// folder CMake configures, the compilers it may ask for headers (a cross compiler's most of all) and the flags to fall
/// back to where the database has no entry yet. A server that cannot start says so in the file's banner.</summary>
public static class ClangdServer
{
    /// <summary>Why clangd cannot answer for a file, as its banner and the status bar say it.</summary>
    public sealed class Unavailable(string problem, string label) : Exception(problem)
    {
        public string Label { get; } = label;
    }

    /// <summary>Where the user-local install keeps clangd when it is not on the PATH.</summary>
    public static string UserClangd => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".local", "share", "passenger-cpp", "venv", "bin", OperatingSystem.IsWindows() ? "clangd.exe" : "clangd");

    /// <summary>clangd on the PATH, the path Settings names, or the user-local install's; null where there is none.</summary>
    public static string? Find(CppToolchain toolchain)
    {
        string named = toolchain.ClangdProgram;
        if (named.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(named))
        {
            return File.Exists(named) ? named : null;
        }
        string? found = Programs.OnPath(named) ?? Programs.OnPath(named + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        return found ?? (File.Exists(UserClangd) ? UserClangd : null);
    }

    /// <summary>Starts and initializes clangd for a project, its process made through ChildProcesses so it ends with the IDE.</summary>
    public static async Task<LanguageSession> StartAsync(string project, CppToolchain toolchain, string logFolder, CancellationToken cancel)
    {
        string program = Find(toolchain) ?? throw new Unavailable(
            $"clangd was not found: install it with Tools | C/C++ Toolchain…, or name it in {SettingsDialog.PlaceOfPage(SettingsDialog.CppPage)}", "no clangd");
        string build = toolchain.BuildPath(project);
        Directory.CreateDirectory(logFolder);
        ProcessStartInfo process = ChildProcesses.Command(program, toolchain.ClangdArguments(build), project);
        return await LanguageSession.StartAsync(new ServerStart(process, project, logFolder, ClientCapabilities.Web)
        {
            InitializationOptions = new
            {
                compilationDatabasePath = build,
                fallbackFlags = toolchain.ClangdFallbackFlags(),
            },
            // clangd pushes a file's diagnostics as it parses, naming no version of the text they answer.
            Pushes = new PushPace(EachText: false, TimeSpan.FromMilliseconds(200)),
        }, cancel);
    }
}
