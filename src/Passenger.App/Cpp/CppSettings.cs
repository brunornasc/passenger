using Passenger.App.Settings;
using Passenger.Workspace.Cpp;

namespace Passenger.App.Cpp;

/// <summary>The C/C++ toolchain as the settings name it: a preset's compilers and target, with a person's own paths and
/// standards over them. One place reads them, so clangd, CMake, Conan, the wizard and the build commands agree.</summary>
public static class CppSettings
{
    public static CppToolchain Toolchain(AppSettings settings)
    {
        CppToolchain preset = CppToolchain.Of(settings.CppPreset);
        return preset with
        {
            C = settings.CppC,
            Cxx = settings.CppCxx,
            CCompiler = settings.CppCCompiler ?? preset.CCompiler,
            CxxCompiler = settings.CppCxxCompiler ?? preset.CxxCompiler,
            CMake = settings.CppCMake,
            Conan = settings.CppConan,
            Clangd = settings.CppClangd,
            Gdb = settings.CppGdb,
            Generator = settings.CppGenerator,
            BuildFolder = settings.CppBuildFolder,
            Configuration = settings.CppConfiguration,
            TargetTriple = settings.CppTargetTriple ?? preset.TargetTriple,
            Sysroot = settings.CppSysroot,
            ToolchainFile = settings.CppToolchainFile,
            ConanProfile = settings.CppConanProfile,
            UseConan = settings.CppUseConan,
            ExtraCMakeArguments = settings.CppExtraCMakeArguments,
        };
    }

    /// <summary>What the wizard writes into the settings, by the toolchain a person chose.</summary>
    public static Func<AppSettings, AppSettings> Applied(CppToolchain toolchain) => settings => settings with
    {
        CppPreset = toolchain.Preset,
        CppC = toolchain.C,
        CppCxx = toolchain.Cxx,
        CppCCompiler = toolchain.CCompiler,
        CppCxxCompiler = toolchain.CxxCompiler,
        CppClangd = toolchain.Clangd,
        CppCMake = toolchain.CMake,
        CppConan = toolchain.Conan,
        CppGdb = toolchain.Gdb,
        CppGenerator = toolchain.Generator,
        CppBuildFolder = toolchain.BuildFolder,
        CppConfiguration = toolchain.Configuration,
        CppTargetTriple = toolchain.TargetTriple,
        CppSysroot = toolchain.Sysroot,
        CppToolchainFile = toolchain.ToolchainFile,
        CppConanProfile = toolchain.ConanProfile,
        CppUseConan = toolchain.UseConan,
        CppExtraCMakeArguments = toolchain.ExtraCMakeArguments,
    };
}
