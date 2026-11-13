using Passenger.Core;
using Passenger.Workspace;
using Passenger.Workspace.Cpp;

namespace Passenger.UnitTests.Cpp;

/// <summary>A folder's C/C++ projects, found by the workspace read as Godot projects are.</summary>
public sealed class WorkspaceCppTests : IDisposable
{
    private readonly string root = PathNames.Canonical(Directory.CreateTempSubdirectory("passenger-unit-").FullName);

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void A_folder_with_a_cmake_lists_is_read_as_a_cpp_project_of_the_workspace()
    {
        File.WriteAllText(Path.Combine(root, CMakeProject.FileName), "cmake_minimum_required(VERSION 3.25)\nproject(Orbit LANGUAGES CXX)\nadd_executable(orbit main.cpp)\n");
        WorkspaceModel workspace = WorkspaceModel.Read(root);
        Assert.Single(workspace.CppProjects);
        CppProject project = workspace.CppProjects[0];
        Assert.Equal("Orbit", project.Name);
        Assert.Equal("orbit", project.FirstExecutable);
        Assert.Equal(root, project.Folder);
    }

    [Fact]
    public void A_file_under_a_cpp_project_finds_the_innermost_project()
    {
        string nested = Directory.CreateDirectory(Path.Combine(root, "lib")).FullName;
        File.WriteAllText(Path.Combine(root, CMakeProject.FileName), "project(Root)\n");
        File.WriteAllText(Path.Combine(nested, CMakeProject.FileName), "project(Lib)\nadd_library(lib STATIC lib.cpp)\n");
        File.WriteAllText(Path.Combine(nested, "lib.cpp"), "int f() { return 1; }\n");
        WorkspaceModel workspace = WorkspaceModel.Read(root);
        Assert.Equal(2, workspace.CppProjects.Count);
        CppProject? found = workspace.CppOf(Path.Combine(nested, "lib.cpp"));
        Assert.NotNull(found);
        Assert.Equal("Lib", found.Name);
    }

    [Fact]
    public void A_reread_keeps_the_cpp_projects_that_are_still_there()
    {
        File.WriteAllText(Path.Combine(root, CMakeProject.FileName), "project(Orbit)\n");
        WorkspaceModel workspace = WorkspaceModel.Read(root);
        Assert.Single(workspace.Reread().CppProjects);
        File.Delete(Path.Combine(root, CMakeProject.FileName));
        Assert.Empty(workspace.Reread().CppProjects);
    }
}
