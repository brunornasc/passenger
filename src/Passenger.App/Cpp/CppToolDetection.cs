using Passenger.Core;
using Passenger.Workspace.Cpp;

namespace Passenger.App.Cpp;

/// <summary>Finds and asks the C/C++ tools on the machine: what is there, what version it says, and which distribution's
/// install commands a wizard should offer. Every program is started through ChildProcesses, so none outlives the IDE.</summary>
public static class CppToolDetection
{
    /// <summary>How long a tool may take to say its version before it is taken as missing.</summary>
    private static readonly TimeSpan Asking = TimeSpan.FromSeconds(15);

    /// <summary>The distribution's family, as the wizard's system install commands name it.</summary>
    public static string Distribution()
    {
        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }
        if (OperatingSystem.IsMacOS())
        {
            return "macos";
        }
        try
        {
            string file = File.ReadAllText("/etc/os-release");
            return file.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("ID=", StringComparison.Ordinal)) is { } id
                ? id[3..].Trim('"') switch
                {
                    "debian" or "ubuntu" or "linuxmint" or "pop" or "neon" or "raspbian" => "debian",
                    "fedora" or "rhel" or "centos" or "rocky" or "almalinux" => "fedora",
                    "arch" or "manjaro" or "endeavouros" => "arch",
                    "opensuse-leap" or "opensuse-tumbleweed" or "sles" or "suse" => "suse",
                    "alpine" => "alpine",
                    _ => "unknown",
                }
                : "unknown";
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    /// <summary>A program on the PATH, or the user-local C/C++ tools folder's, or a full path a person named.</summary>
    public static string? Find(string named)
    {
        if (named.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(named))
        {
            return File.Exists(named) ? named : null;
        }
        string? onPath = Programs.OnPath(named) ?? Programs.OnPath(named + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        if (onPath is not null)
        {
            return onPath;
        }
        string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "passenger-cpp", "venv", "bin", named);
        return File.Exists(user) ? user : null;
    }

    /// <summary>What the command line in a Settings box runs, said under it as it is typed.</summary>
    public static async Task<string> DescribedAsync(string? line, string fallback)
    {
        IReadOnlyList<string> words = CppToolchain.Words(line);
        string named = words.Count > 0 ? words[0] : fallback;
        string? found = Find(named);
        if (found is null)
        {
            return $"{named} was not found";
        }
        return await VersionAsync(found, CancellationToken.None);
    }

    /// <summary>Every tool of the toolchain as the wizard found it, asked off the UI thread.</summary>
    public static async Task<IReadOnlyList<CppToolStatus>> DetectAsync(CppToolchain toolchain, CancellationToken cancel)
    {
        List<CppToolStatus> statuses = [];
        foreach (CppToolCheck check in CppTools.Checks(toolchain))
        {
            if (cancel.IsCancellationRequested)
            {
                break;
            }
            string? found = Find(check.Program);
            string? version = found is null ? null : await VersionAsync(found, cancel);
            statuses.Add(new CppToolStatus(check.Tool, check.Label, found, version));
        }
        return statuses;
    }

    /// <summary>A tool's own version words, or why it could not be asked.</summary>
    private static async Task<string> VersionAsync(string program, CancellationToken cancel)
    {
        try
        {
            ChildResult result = await BackgroundWork.Run(() => ChildProcesses.RunAsync(
                ChildProcesses.Command(program, ["--version"]), limit: Asking, cancel: cancel), cancel);
            return result.Succeeded ? CppTools.Version(result.Output) : $"ended with {ExitCodes.Said(result.ExitCode)}";
        }
        catch (OperationCanceledException)
        {
            return "not asked";
        }
        catch (Exception failed) when (ChildFailures.Of(failed))
        {
            return failed.Message;
        }
    }
}
