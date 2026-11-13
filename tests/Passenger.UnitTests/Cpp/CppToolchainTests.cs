using Passenger.Workspace.Cpp;

namespace Passenger.UnitTests.Cpp;

/// <summary>The commands one toolchain makes, read without starting a process.</summary>
public sealed class CppToolchainTests
{
    [Fact]
    public void A_configure_passes_the_chosen_standards_compilers_and_compilation_database()
    {
        CppToolchain toolchain = new() { C = CStandard.C17, Cxx = CxxStandard.Cxx20, CCompiler = "gcc-13", CxxCompiler = "g++-13" };
        IReadOnlyList<string> arguments = toolchain.ConfigureArguments("/work/app", "/work/app/cmake-build-debug", false);
        string line = string.Join(' ', arguments);
        Assert.True(line.Contains("-DCMAKE_C_STANDARD=17", StringComparison.Ordinal)
            && line.Contains("-DCMAKE_CXX_STANDARD=20", StringComparison.Ordinal), $"The standards are missing from: {line}");
        Assert.True(line.Contains("-DCMAKE_C_COMPILER=gcc-13", StringComparison.Ordinal)
            && line.Contains("-DCMAKE_CXX_COMPILER=g++-13", StringComparison.Ordinal), $"The compilers are missing from: {line}");
        Assert.True(line.Contains("-DCMAKE_EXPORT_COMPILE_COMMANDS=ON", StringComparison.Ordinal), $"clangd's compilation database is missing from: {line}");
        Assert.DoesNotContain("-DCMAKE_TOOLCHAIN_FILE=", line);
    }

    [Fact]
    public void A_cross_preset_names_its_target_and_asks_clangd_for_the_cross_compilers_headers()
    {
        CppToolchain toolchain = CppToolchain.Of(CppPreset.M68kElf);
        Assert.True(toolchain.IsCross, "An m68k preset is a cross toolchain");
        Assert.True(toolchain.CxxCompiler == "m68k-elf-g++" && toolchain.TargetTriple == "m68k-elf", "The preset names its own compiler and target");
        string configure = string.Join(' ', toolchain.ConfigureArguments("/work/app", "/work/app/build", false));
        Assert.True(configure.Contains("-DCMAKE_CXX_COMPILER_TARGET=m68k-elf", StringComparison.Ordinal)
            && configure.Contains("-DCMAKE_CXX_COMPILER=m68k-elf-g++", StringComparison.Ordinal), $"The cross target is missing from: {configure}");
        string clangd = string.Join(' ', toolchain.ClangdArguments("/work/app/build"));
        Assert.True(clangd.Contains("--query-driver=m68k-elf-gcc,m68k-elf-g++", StringComparison.Ordinal)
            && clangd.Contains("--compile-commands-dir=/work/app/build", StringComparison.Ordinal), $"clangd was not told the cross compiler and build folder: {clangd}");
        Assert.Contains("--target=m68k-elf", toolchain.ClangdFallbackFlags());
        Assert.False(toolchain.UseConan, "A bare-metal cross project does not use Conan's host packages by default");
    }

    [Fact]
    public void Conans_toolchain_is_the_configures_own_where_the_project_has_a_recipe()
    {
        CppToolchain toolchain = new() { UseConan = true };
        string source = Path.Combine(Path.GetTempPath(), "app");
        string configure = string.Join(' ', toolchain.ConfigureArguments(source, toolchain.BuildPath(source), true));
        Assert.Contains("conan_toolchain.cmake", configure);
        Assert.Contains("--build=missing", string.Join(' ', toolchain.ConanArguments(source, toolchain.BuildPath(source))));
        CppToolchain own = toolchain with { ToolchainFile = "/work/m68k.cmake" };
        string withOwn = string.Join(' ', own.ConfigureArguments(source, own.BuildPath(source), true));
        Assert.Contains("-DCMAKE_TOOLCHAIN_FILE=/work/m68k.cmake", withOwn);
        Assert.DoesNotContain("conan_toolchain.cmake", withOwn);
    }

    [Fact]
    public void Extra_arguments_split_as_a_shell_would_and_one_held_in_quotes_stays_one()
    {
        Assert.Equal(new[] { "-DFOO=bar", "-DNAME=a value", "-G", "Ninja" }, CppToolchain.Words("-DFOO=bar \"-DNAME=a value\" -G Ninja"));
        Assert.Empty(CppToolchain.Words("   "));
    }

    [Fact]
    public void A_clangd_file_keeps_the_c_and_cpp_standards_in_their_own_fragments()
    {
        CppToolchain toolchain = new() { C = CStandard.C17, Cxx = CxxStandard.Cxx20 };
        string config = toolchain.ClangdConfiguration();
        Assert.Contains("-std=c17", config);
        Assert.Contains("-std=c++20", config);
        Assert.Contains("PathMatch:", config);
        Assert.DoesNotContain("-std=c17", toolchain.ClangdFallbackFlags());
        Assert.DoesNotContain("-std=c++20", toolchain.ClangdFallbackFlags());
    }

    [Fact]
    public void Changing_a_standard_or_compiler_changes_the_signature_a_settings_change_restarts_clangd_by()
    {
        CppToolchain toolchain = new();
        Assert.NotEqual(toolchain.Signature(), (toolchain with { Cxx = CxxStandard.Cxx23 }).Signature());
        Assert.NotEqual(toolchain.Signature(), (toolchain with { CxxCompiler = "m68k-elf-g++" }).Signature());
        Assert.Equal(toolchain.Signature(), new CppToolchain().Signature());
    }
}
