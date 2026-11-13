using Passenger.Core;
using Passenger.Workspace.Cpp;

namespace Passenger.UnitTests.Cpp;

/// <summary>A CMakeLists.txt read as a project, its targets and its standards.</summary>
public sealed class CMakeProjectTests : IDisposable
{
    private readonly string root = PathNames.Canonical(Directory.CreateTempSubdirectory("passenger-unit-").FullName);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void A_lists_declares_the_targets_standards_and_minimum_version_without_running_cmake()
    {
        Write(CMakeProject.FileName, """
            cmake_minimum_required(VERSION 3.25) # a comment
            project(Orbit LANGUAGES C CXX)
            set(CMAKE_C_STANDARD 11)
            set(CMAKE_CXX_STANDARD 20)
            add_executable(orbit main.cpp util.cpp)
            add_library(core STATIC core.cpp)
            add_subdirectory(tests)
            """);
        CppProject? project = CMakeProject.Read(root);
        Assert.NotNull(project);
        Assert.Equal("Orbit", project.Name);
        Assert.Equal("3.25", project.MinimumCmake);
        Assert.Equal(CStandard.C11, project.C);
        Assert.Equal(CxxStandard.Cxx20, project.Cxx);
        Assert.Equal(["orbit"], project.Executables.Select(t => t.Name));
        Assert.Equal(2, project.Targets.Count);
        Assert.Contains("tests", project.Subdirectories);
        Assert.Contains("main.cpp", project.Targets[0].Sources);
    }

    [Fact]
    public void A_conan_recipe_alone_is_a_project_and_find_walks_up_to_its_folder()
    {
        Write("conanfile.txt", "[requires]\nfmt/11.0.2\n\n[generators]\nCMakeDeps\nCMakeToolchain\n");
        string nested = Directory.CreateDirectory(Path.Combine(root, "src")).FullName;
        Assert.Equal(root, CMakeProject.Find(nested));
        CppProject? project = CMakeProject.Read(root);
        Assert.NotNull(project);
        Assert.True(project.HasConan, "A conanfile.txt beside no CMakeLists.txt is still a C/C++ project");
        Assert.Empty(project.Targets);
        ConanRecipe? recipe = ConanFile.Read(root);
        Assert.NotNull(recipe);
        Assert.Equal(["fmt/11.0.2"], recipe.Requires);
        Assert.True(recipe.UsesCMakeToolchain, "CMakeToolchain makes the toolchain a configure takes");
    }

    [Fact]
    public void A_python_recipe_reads_its_requires_and_generators()
    {
        Write("conanfile.py", """
            from conan import ConanFile
            class App(ConanFile):
                settings = "os", "compiler", "build_type", "arch"
                requires = ("fmt/11.0.2", "zlib/1.3.1")
                generators = ("CMakeDeps", "CMakeToolchain")
                default_options = {"fmt/*:header_only": True}
                def layout(self):
                    cmake_layout(self)
            """);
        ConanRecipe? recipe = ConanFile.Read(root);
        Assert.NotNull(recipe);
        Assert.True(recipe.Python, "A conanfile.py is a Python recipe");
        Assert.Equal(["fmt/11.0.2", "zlib/1.3.1"], recipe.Requires);
        Assert.Equal(["CMakeDeps", "CMakeToolchain"], recipe.Generators);
        Assert.Equal("cmake_layout", recipe.Layout);
    }

    [Fact]
    public void A_recipe_with_packages_is_reported_so_only_its_configure_runs_conan()
    {
        Write(CMakeProject.FileName, "project(App)\nadd_executable(app main.cpp)\n");
        Write("conanfile.txt", "[requires]\nfmt/11.0.2\n\n[generators]\nCMakeDeps\nCMakeToolchain\n");
        CppProject? project = CMakeProject.Read(root);
        Assert.NotNull(project);
        Assert.True(project.HasConanPackages);
        Assert.Equal(new[] { "fmt/11.0.2" }, project.ConanRequires);
        Assert.Contains("fmt/11.0.2", project.Signature);
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(root, name), text);
}
