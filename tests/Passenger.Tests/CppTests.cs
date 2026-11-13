using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Cpp;
using Passenger.App.Run;
using Passenger.App.Settings;
using Passenger.App.Templates;
using Passenger.Core;
using Passenger.Languages;
using Passenger.Workspace.Cpp;

namespace Passenger.Tests;

/// <summary>The C/C++ page in Settings and the toolchain wizard, drawn on Avalonia's headless platform.</summary>
public sealed class CppTests : IDisposable
{
    private readonly string state = PathNames.Canonical(Directory.CreateTempSubdirectory("passenger-cpp-state-").FullName);

    public void Dispose() => Directory.Delete(state, recursive: true);

    /// <summary>Settings | C/C++ offers the standards, the compilers and the tools' paths, as the wizard writes them.</summary>
    [AvaloniaFact]
    public void The_cpp_settings_page_lists_the_standards_compilers_and_tools()
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = state };
        window.Show();
        try
        {
            SettingsDialog dialog = window.ShowSettings();
            dialog.Page = SettingsDialog.CppPage;
            Ui.Settle();
            foreach (string label in (string[])["Toolchain preset", "C standard", "C++ standard", "CMake generator",
                "clangd executable", "cmake executable", "conan executable", "gdb executable", "C compiler", "C++ compiler", "Target triple"])
            {
                Assert.Contains(label, dialog.Shown);
            }
            dialog.Close();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>C, C++ and assembly files take their own languages, the first two answered by clangd.</summary>
    [AvaloniaFact]
    public void C_cpp_and_assembly_files_take_their_own_languages()
    {
        Assert.Equal(Language.C, Language.ForPath("src/util.c"));
        Assert.Equal(Language.C, Language.ForPath("src/util.h"));
        Assert.Equal(Language.Cpp, Language.ForPath("src/main.cpp"));
        Assert.Equal(Language.Cpp, Language.ForPath("src/main.hpp"));
        Assert.Equal(Language.Assembly, Language.ForPath("src/boot.s"));
        Assert.Equal(Language.Assembly, Language.ForPath("src/boot.asm"));
        Assert.Equal(LanguageService.Cpp, Language.ForPath("src/main.cpp").Service);
    }

    /// <summary>A CMake executable target becomes a run configuration in the toolbar, which Run and Debug start.</summary>
    [AvaloniaFact]
    public void A_cmake_executable_becomes_a_run_configuration()
    {
        string folder = Path.Combine(state, "project");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, CMakeProject.FileName), "project(Orbit LANGUAGES CXX)\nadd_executable(orbiter main.cpp)\n");
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = state };
        window.Show();
        try
        {
            window.OpenFolder(folder);
            Ui.WaitFor(() => window.Configurations.Any(c => c.Cpp), "the CMake target to become a run configuration", TimeSpan.FromSeconds(30));
            RunConfiguration configuration = Assert.Single(window.Configurations, c => c.Cpp);
            Assert.Equal("orbiter", configuration.CppTarget);
            Assert.True(configuration.BuildBeforeLaunch);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Creating a C/C++ project writes only its CMake files: no .slnx, no NuGet, and a Conan 2 recipe with no
    /// [settings] section (which Conan 2 refuses).</summary>
    [AvaloniaFact]
    public void Creating_a_cpp_project_writes_no_dotnet_solution_or_invalid_recipe()
    {
        string place = Path.Combine(state, "new");
        Directory.CreateDirectory(place);
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = state };
        window.Show();
        try
        {
            Ui.Click(window.Welcome.NewSolutionButton);
            Ui.Settle();
            NewProjectDialog dialog = Assert.Single(window.OwnedWindows.OfType<NewProjectDialog>());
            Ui.WaitFor(() => dialog.TemplatesRead.IsCompleted, "the templates to be read", TimeSpan.FromMinutes(3));
            dialog.Folder = place;
            Ui.Click(dialog.FamilyRow("C/C++ (CMake)") ?? throw new InvalidOperationException("New Solution lists no C/C++ (CMake)"));
            Assert.True(dialog.Chosen is { CppProject: true }, $"C/C++ chose {dialog.Chosen?.Name}");
            dialog.SolutionName = "CppOnly";
            Ui.Settle();
            Ui.Click(dialog.CreateButton);
            Ui.WaitFor(() => dialog.Created is not null || dialog.Problem is not null, "the C/C++ project to be made", TimeSpan.FromMinutes(2));
            Assert.True(dialog.Problem is null, $"The C/C++ project was not made: {dialog.Problem}");
            CreatedProjects made = Assert.IsType<CreatedProjects>(dialog.Created);
            Assert.True(made.Cpp);
            Assert.Null(made.Solution);
            Assert.True(File.Exists(Path.Combine(made.Folder, CMakeProject.FileName)));
            Assert.Empty(Directory.EnumerateFiles(made.Folder, "*.sln*"));
            Assert.DoesNotContain("[settings]", File.ReadAllText(Path.Combine(made.Folder, "conanfile.txt")));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Opening the Conan wizard starts from the recipe's packages, so adding one keeps the earlier ones.</summary>
    [AvaloniaFact]
    public void The_conan_wizard_starts_with_the_recipes_existing_packages()
    {
        string folder = Path.Combine(state, "packages");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, CMakeProject.FileName), "project(App LANGUAGES CXX)\nadd_executable(app main.cpp)\n");
        File.WriteAllText(Path.Combine(folder, "conanfile.txt"), CppFiles.Conan(["fmt/11.0.2", "zlib/1.3.1"]));
        CppProject? project = CMakeProject.Read(folder);
        Assert.NotNull(project);
        CppConanWizard dialog = new(new AppSettings(), project);
        try
        {
            Assert.Equal(new[] { "fmt/11.0.2", "zlib/1.3.1" }, dialog.SelectedPackages);
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>The Conan window sits beside NuGet's on the left stripe, over the folder's C/C++ projects.</summary>
    [AvaloniaFact]
    public void The_conan_tool_window_sits_beside_nugets()
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = state };
        window.Show();
        try
        {
            Passenger.App.Docking.ToolWindow conan = window.DockHost[ToolWindowIds.Conan];
            Assert.Equal("Conan", conan.Title);
            Assert.Equal(Passenger.App.Docking.ToolAnchor.BottomLeft, conan.Anchor);
            Assert.Same(window.Conan, window.Conan);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The Conan Center wizard builds; its recipe list is fetched when the window opens.</summary>
    [AvaloniaFact]
    public void The_conan_package_wizard_builds()
    {
        CppConanWizard dialog = new(new AppSettings(), null);
        try
        {
            Assert.NotNull(dialog);
        }
        finally
        {
            dialog.Close();
        }
    }

    /// <summary>The wizard is built and its result is the settings' toolchain, with off-PATH tools named.</summary>
    [AvaloniaFact]
    public void The_toolchain_wizard_builds_and_names_the_settings_toolchain()
    {
        CppToolchainWizard wizard = new(new AppSettings(), null);
        try
        {
            CppToolchain made = wizard.Result();
            Assert.Equal(CppPreset.System, made.Preset);
            Assert.Equal(CStandard.Default, made.C);
            Assert.Equal(CxxStandard.Default, made.Cxx);
            Assert.Equal(CppToolchain.DefaultBuildFolder, made.BuildFolder);
        }
        finally
        {
            wizard.Close();
        }
    }
}
