using Avalonia.Controls;
using Passenger.App.Cpp;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Workspace;
using Passenger.Workspace.Cpp;

namespace Passenger.App;

/// <summary>The C/C++ tools in the Tools menu: the wizard that installs and chooses a toolchain, and the CMake, Conan,
/// build and run commands of the C/C++ project in the folder. Their output goes to the Build window as a .NET build's does.</summary>
public partial class MainWindow
{
    private enum CppStep
    {
        Configure,
        ConanInstall,
        Build,
        Run,
    }

    private void AddCpp()
    {
        actions!.Add(new IdeAction(AppActions.CppToolchainWizard, "C/C++ Toolchain…", "IDE", _ => IdeErrors.Observe(ShowCppWizardAsync()))
        {
            Menus = [new MenuPlace("Tools", 1, 2)],
            Words = ["cmake", "conan", "clangd", "c++", "cpp", "m68k", "cross", "compiler", "toolchain", "install"],
            Works = AppActions.CppToolchainWizard,
        });
        actions.Add(new IdeAction(AppActions.CppConfigure, "CMake: Configure…", "IDE", _ => IdeErrors.Observe(CppCommandAsync(CppStep.Configure)))
        {
            Menus = [new MenuPlace("Tools", 1, 3)],
            Words = ["cmake", "configure", "compile_commands.json", "c++", "cpp"],
            Works = AppActions.CppConfigure,
        });
        actions.Add(new IdeAction(AppActions.CppConanInstall, "Conan: Install…", "IDE", _ => IdeErrors.Observe(CppCommandAsync(CppStep.ConanInstall)))
        {
            Menus = [new MenuPlace("Tools", 1, 4)],
            Words = ["conan", "install", "packages", "dependencies", "c++", "cpp"],
            Works = AppActions.CppConanInstall,
        });
        actions.Add(new IdeAction(AppActions.CppConanPackages, "Install C/C++ dependencies with Conan…", "IDE", _ => { ShowCppConanWizard(); })
        {
            Menus = [new MenuPlace("Tools", 1, 5)],
            Words = ["conan", "conan center", "package", "packages", "dependencies", "install", "c++", "cpp", "find_package"],
            Works = AppActions.CppConanPackages,
        });
        actions.Add(new IdeAction(AppActions.CppBuild, "CMake: Build", "IDE", _ => IdeErrors.Observe(CppCommandAsync(CppStep.Build)))
        {
            Menus = [new MenuPlace("Tools", 1, 6)],
            Words = ["cmake", "build", "c++", "cpp", "m68k"],
            Works = AppActions.CppBuild,
        });
        actions.Add(new IdeAction(AppActions.CppRun, "C/C++: Run Target…", "IDE", _ => IdeErrors.Observe(CppCommandAsync(CppStep.Run)))
        {
            Menus = [new MenuPlace("Tools", 1, 7)],
            Words = ["cmake", "run", "target", "executable", "c++", "cpp"],
            Works = AppActions.CppRun,
        });
    }

    /// <summary>Whether the open folder is a C/C++ project alone, with no .NET solution or projects to build.</summary>
    private bool CppOnly() => session.Workspace is { CppProjects.Count: > 0, Projects.Count: 0, Solution: null };

    /// <summary>The C/C++ project the active file is in, else the first in the folder.</summary>
    private CppProject? CppProject()
    {
        WorkspaceModel? workspace = session.Workspace;
        if (workspace is null || workspace.CppProjects.Count == 0)
        {
            return null;
        }
        return Editors.Active?.Path is { } active ? workspace.CppOf(active) ?? workspace.CppProjects[0] : workspace.CppProjects[0];
    }

    /// <summary>The wizard, over the folder's C/C++ project where it has one; its Save puts the toolchain in force, which
    /// starts clangd again and changes the CMake, Conan and clangd commands.</summary>
    private async Task ShowCppWizardAsync()
    {
        CppToolchainWizard dialog = new(settings, CppProject());
        if (await dialog.ShowDialog<CppToolchain?>(this) is { } chosen)
        {
            ApplySettings(CppSettings.Applied(chosen)(settings));
            status.Say("C/C++ toolchain saved: clangd and the CMake commands use it now", null);
        }
    }

    /// <summary>The Conan Center wizard, over the C/C++ project in the folder; it writes the recipe and CMakeLists.txt
    /// itself, so the folder watcher takes the new packages into the next Run or Debug.</summary>
    public CppConanWizard ShowCppConanWizard()
    {
        CppConanWizard dialog = new(settings, CppProject());
        IdeErrors.Observe(dialog.ShowDialog(this));
        return dialog;
    }

    /// <summary>One CMake or Conan command, or a target built and run, its output in the Build window.</summary>
    private async Task CppCommandAsync(CppStep what)
    {
        CppProject? project = CppProject();
        if (project is null)
        {
            status.Say("Open a folder with a CMakeLists.txt or conanfile.txt first", ProblemSeverity.Error);
            return;
        }
        string? target = what == CppStep.Run ? await AskTargetAsync(project) : null;
        if (what == CppStep.Run && target is null)
        {
            return;
        }
        string title = what switch
        {
            CppStep.Configure => "Configuring " + project.Name,
            CppStep.ConanInstall => "Installing Conan packages for " + project.Name,
            CppStep.Build => "Building " + project.Name,
            _ => "Running " + target,
        };
        CppToolchain toolchain = CppSettings.Toolchain(settings);
        Bottom.Build.Clear();
        Bottom.Build.SetRunning(true);
        Dock.Show(ToolWindowIds.Build);
        status.Say(title + "…", null);
        CppCommandResult result = await CommandAsync(what, project, toolchain, target);
        Bottom.Build.Append(result.Output);
        Bottom.Build.SetRunning(false);
        status.Say(result.Succeeded ? title + " finished" : title + " failed: see the Build window", result.Succeeded ? null : ProblemSeverity.Error);
    }

    private static async Task<CppCommandResult> CommandAsync(CppStep what, CppProject project, CppToolchain toolchain, string? target)
    {
        switch (what)
        {
            case CppStep.Configure:
                return await CppCommands.ConfigureAsync(project, toolchain, CancellationToken.None);
            case CppStep.ConanInstall:
                return await CppCommands.ConanAsync(project, toolchain, CancellationToken.None);
            case CppStep.Build:
                return await CppCommands.BuildAsync(project, toolchain, null, CancellationToken.None);
            default:
                CppCommandResult built = await CppCommands.BuildAsync(project, toolchain, target, CancellationToken.None);
                if (!built.Succeeded)
                {
                    return built;
                }
                CppCommandResult ran = await CppCommands.RunAsync(project, toolchain, target!, CancellationToken.None);
                return new CppCommandResult(ran.Succeeded, built.Output + ran.Output);
        }
    }

    /// <summary>The CMake executable to run, where the project declares more than one.</summary>
    private async Task<string?> AskTargetAsync(CppProject project)
    {
        IReadOnlyList<string> names = [.. project.Executables.Select(t => t.Name)];
        if (names.Count == 0)
        {
            status.Say(project.Name + " declares no executable target in its CMakeLists.txt", ProblemSeverity.Error);
            return null;
        }
        if (names.Count == 1)
        {
            return names[0];
        }
        ComboBox box = new() { ItemsSource = names, SelectedIndex = 0, Classes = { StyleClasses.Form } };
        ConfirmDialog dialog = new("Run a C/C++ target", "Choose the CMake executable to build and run.", "Run", detail: box);
        return await dialog.ShowDialog<bool>(this) && box.SelectedItem is string chosen ? chosen : null;
    }
}
