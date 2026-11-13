using Passenger.Workspace.Cpp;

namespace Passenger.UnitTests.Cpp;

/// <summary>The Conan Center list, a valid Conan 2 recipe and the CMake edits the package wizard writes.</summary>
public sealed class ConanWizardTests
{
    private const string Recipes = """
        {"results": ["fmt/10.2.1@_/_", "fmt/11.0.2@_/_", "zlib/1.3.1@_/_", "zlib/1.2.13@_/_"]}
        """;

    private const string ConanOutput = """
        Generator 'CMakeDeps' calling 'generate()'
        CMakeDeps necessary find_package() and targets for your CMakeLists.txt
            find_package(fmt)
            find_package(ZLIB)
            target_link_libraries(... fmt::fmt ZLIB::ZLIB)
        Generator 'CMakeToolchain' calling 'generate()'
        CMakeToolchain generated: conan_toolchain.cmake
        Install finished successfully
        """;

    [Fact]
    public void The_remote_answer_groups_versions_under_their_recipe_names()
    {
        IReadOnlyList<ConanPackage> packages = ConanCenter.Recipes(Recipes);
        Assert.Equal(2, packages.Count);
        ConanPackage fmt = Assert.Single(packages, p => p.Name == "fmt");
        Assert.Equal(new[] { "11.0.2", "10.2.1" }, fmt.Versions);
        Assert.Equal(new[] { "1.3.1", "1.2.13" }, Assert.Single(packages, p => p.Name == "zlib").Versions);
        Assert.Equal("fmt/11.0.2", ConanCenter.Requires("fmt", "11.0.2"));
        Assert.True(ConanCenter.Valid("fmt/11.0.2"));
        Assert.False(ConanCenter.Valid("fmt"));
    }

    [Fact]
    public void The_recipe_list_reads_conan_lists_own_output_where_the_web_is_unreachable()
    {
        IReadOnlyList<ConanPackage> packages = ConanCenter.Listed("""
            conancenter
              fmt
                fmt/11.0.2
                fmt/10.2.1
            """);
        Assert.Equal(new[] { "11.0.2", "10.2.1" }, Assert.Single(packages).Versions);
    }

    [Fact]
    public void Conans_own_words_give_the_find_package_names_and_CMake_targets()
    {
        (IReadOnlyList<string> find, IReadOnlyList<string> link) = ConanCenter.CmakeLines(ConanOutput);
        Assert.Equal(new[] { "fmt", "ZLIB" }, find);
        Assert.Equal(new[] { "fmt::fmt", "ZLIB::ZLIB" }, link);
    }

    [Fact]
    public void A_new_recipe_is_valid_conan_2_and_has_no_settings_section()
    {
        string recipe = CppFiles.Conan(["fmt/11.0.2"]);
        Assert.Contains("[requires]", recipe);
        Assert.Contains("fmt/11.0.2", recipe);
        Assert.Contains("CMakeDeps", recipe);
        Assert.Contains("CMakeToolchain", recipe);
        Assert.DoesNotContain("[settings]", recipe);
        Assert.DoesNotContain("os=Linux", recipe);
    }

    [Fact]
    public void Adding_packages_removes_an_old_settings_section_and_keeps_the_generators()
    {
        string old = "[requires]\n# a comment\nold/1.0\n\n[generators]\nCMakeDeps\nCMakeToolchain\n\n[settings]\nos=Linux\ncompiler=gcc\n";
        string updated = CppFiles.WithRequires(old, ["fmt/11.0.2", "zlib/1.3.1"]);
        Assert.DoesNotContain("[settings]", updated);
        Assert.DoesNotContain("os=Linux", updated);
        Assert.DoesNotContain("old/1.0", updated);
        Assert.Contains("fmt/11.0.2", updated);
        Assert.Contains("zlib/1.3.1", updated);
        Assert.Contains("# a comment", updated);
        Assert.Contains("CMakeDeps", updated);
        Assert.Equal(updated, CppFiles.WithRequires(updated, ["fmt/11.0.2", "zlib/1.3.1"]));
    }

    [Fact]
    public void A_second_install_writes_both_the_existing_and_the_new_package()
    {
        string first = CppFiles.Conan(["fmt/11.0.2"]);
        string second = CppFiles.WithRequires(first, ["fmt/11.0.2", "andreasbuhr-cppcoro/cci.20230629"]);
        Assert.Contains("fmt/11.0.2", second);
        Assert.Contains("andreasbuhr-cppcoro/cci.20230629", second);
        Assert.Equal(1, second.Split('\n').Count(l => l.Trim() == "fmt/11.0.2"));
    }

    [Fact]
    public void A_cmake_lists_gets_find_package_and_target_link_libraries_only_once()
    {
        const string lists = "cmake_minimum_required(VERSION 3.25)\nproject(App LANGUAGES CXX)\nadd_executable(app main.cpp)\n";
        string once = CppFiles.WithPackages(lists, ["fmt", "ZLIB"], ["fmt::fmt", "ZLIB::ZLIB"]);
        Assert.Contains("find_package(fmt REQUIRED)", once);
        Assert.Contains("find_package(ZLIB REQUIRED)", once);
        Assert.Contains("target_link_libraries(app PRIVATE fmt::fmt ZLIB::ZLIB)", once);
        string twice = CppFiles.WithPackages(once, ["fmt", "ZLIB"], ["fmt::fmt", "ZLIB::ZLIB"]);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Generated_cmake_target_files_give_the_targets_where_conans_own_words_are_missing()
    {
        string folder = Path.Combine(Path.GetTempPath(), "passenger-conan-targets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "fmtTargets.cmake"), """
                foreach(_COMPONENT ${fmt_COMPONENT_NAMES} )
                    add_library(${_COMPONENT} INTERFACE IMPORTED)
                endforeach()
                add_library(fmt::fmt INTERFACE IMPORTED)
                """);
            Assert.Equal(new[] { "fmt::fmt" }, ConanCenter.GeneratedTargets(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_recipe_with_no_packages_needs_no_conan_install_or_toolchain_file()
    {
        CppToolchain toolchain = new() { UseConan = true };
        string configure = string.Join(' ', toolchain.ConfigureArguments("/work/app", "/work/app/build", false));
        Assert.DoesNotContain("conan_toolchain.cmake", configure);
        Assert.DoesNotContain("-DCMAKE_PREFIX_PATH=", configure);
        Assert.Contains("-DCMAKE_EXPORT_COMPILE_COMMANDS=ON", configure);
    }

    [Fact]
    public void A_configure_with_packages_caches_the_conan_toolchain_and_prefix_path()
    {
        CppToolchain toolchain = new() { UseConan = true };
        string configure = string.Join(' ', toolchain.ConfigureArguments("/work/app", "/work/app/build", true));
        Assert.Contains("conan_toolchain.cmake", configure);
        Assert.Contains("-DCMAKE_PREFIX_PATH=/work/app/cmake-build-debug/conan", configure);
    }
}
