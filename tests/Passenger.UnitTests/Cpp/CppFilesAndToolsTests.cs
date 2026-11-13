using System.Text.Json;
using Passenger.Workspace.Cpp;

namespace Passenger.UnitTests.Cpp;

/// <summary>The files a new project is written from and the commands a wizard offers to install what is missing.</summary>
public sealed class CppFilesAndToolsTests
{
    [Fact]
    public void A_new_cpp_project_writes_cmake_conan_presets_clangd_and_a_source_file()
    {
        CppToolchain toolchain = new() { Cxx = CxxStandard.Cxx20, UseConan = true };
        IReadOnlyList<(string Path, string Text)> files = CppFiles.For("Orbit", true, toolchain);
        Dictionary<string, string> byName = files.ToDictionary(f => f.Path, f => f.Text);
        Assert.True(byName.ContainsKey(CMakeProject.FileName) && byName.ContainsKey("main.cpp")
            && byName.ContainsKey("conanfile.txt") && byName.ContainsKey(".clangd") && byName.ContainsKey("CMakePresets.json"),
            $"The project's files are: {string.Join(", ", byName.Keys)}");
        Assert.Contains("CMAKE_CXX_STANDARD 20", byName[CMakeProject.FileName]);
        Assert.Contains("-std=c++20", byName[".clangd"]);
        using JsonDocument presets = JsonDocument.Parse(byName["CMakePresets.json"]);
        Assert.Equal(6, presets.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("Ninja", presets.RootElement.GetProperty("configurePresets")[0].GetProperty("generator").GetString());
        Assert.Contains("CMakeToolchain", byName["conanfile.txt"]);
    }

    [Fact]
    public void A_new_c_project_writes_c_alone_and_no_cxx_standard()
    {
        IReadOnlyList<(string Path, string Text)> files = CppFiles.For("Kernel", false, new CppToolchain { UseConan = false });
        Dictionary<string, string> byName = files.ToDictionary(f => f.Path, f => f.Text);
        Assert.Contains("main.c", byName.Keys);
        Assert.DoesNotContain("conanfile.txt", byName.Keys);
        Assert.Contains("LANGUAGES C", byName[CMakeProject.FileName]);
        Assert.DoesNotContain("CXX", byName[CMakeProject.FileName]);
    }

    [Fact]
    public void A_user_local_install_needs_no_administrator_and_a_system_one_names_the_distribution()
    {
        IReadOnlyList<CppInstallStep> local = CppTools.Plan([CppTool.CMake, CppTool.Clangd], "debian", CppInstallWay.UserLocal);
        Assert.Single(local);
        Assert.Contains("pip install cmake clangd", local[0].Command!);
        Assert.Contains("venv", local[0].Command!);
        Assert.DoesNotContain("sudo", local[0].Command!);
        IReadOnlyList<CppInstallStep> system = CppTools.Plan([CppTool.CMake, CppTool.Ninja], "debian", CppInstallWay.System);
        Assert.Equal(2, system.Count);
        Assert.Contains("apt install -y cmake", system[0].Command!);
        Assert.Contains("ninja-build", system[1].Command!);
    }

    [Fact]
    public void A_missing_cross_compiler_is_said_to_come_from_its_own_toolchain()
    {
        IReadOnlyList<CppInstallStep> steps = CppTools.Plan([CppTool.CxxCompiler], "debian", CppInstallWay.System);
        Assert.Single(steps);
        Assert.Contains("m68k", steps[0].Note!);
    }

    [Fact]
    public void A_missing_gdb_is_a_system_install_with_a_note_for_a_user_local_one()
    {
        IReadOnlyList<CppInstallStep> system = CppTools.Plan([CppTool.Gdb], "debian", CppInstallWay.System);
        Assert.Single(system);
        Assert.Contains("apt install -y gdb", system[0].Command!);
        IReadOnlyList<CppInstallStep> local = CppTools.Plan([CppTool.Gdb], "debian", CppInstallWay.UserLocal);
        Assert.Single(local);
        Assert.Null(local[0].Command);
        Assert.Contains("system debugger", local[0].Note!);
    }

    [Fact]
    public void The_gdb_check_names_the_configured_program()
    {
        CppToolchain toolchain = new() { Gdb = "/opt/gdb/bin/gdb" };
        CppToolCheck check = CppTools.Checks(toolchain).Single(c => c.Tool == CppTool.Gdb);
        Assert.Equal("/opt/gdb/bin/gdb", check.Program);
        Assert.Equal("/opt/gdb/bin/gdb", toolchain.GdbProgram);
        Assert.Equal("gdb", new CppToolchain().GdbProgram);
    }

    [Fact]
    public void A_versions_first_line_is_kept_short()
    {
        Assert.Equal("clangd version 22.1.1", CppTools.Version("clangd version 22.1.1\nFeatures: linux\n"));
        Assert.Equal("cmake version 3.30.5", CppTools.Version("\n  cmake version 3.30.5\n"));
    }
}
