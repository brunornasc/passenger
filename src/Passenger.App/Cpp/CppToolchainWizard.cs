using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Settings;
using Passenger.Core;
using Passenger.Workspace.Cpp;

namespace Passenger.App.Cpp;

/// <summary>Three steps to a working C/C++ toolchain: what is installed and how to install what is missing, which
/// compilers and language standards to use, and the exact CMake, Conan and clangd commands those choices make. Nothing
/// is installed until a person presses Install, and nothing is saved until Save.</summary>
public sealed class CppToolchainWizard : IdeWindow
{
    private readonly AppSettings settings;
    private readonly CppProject? project;
    private readonly StackPanel body = new() { Spacing = 12, Margin = new Thickness(20) };
    private readonly TextBlock heading = new() { FontSize = 15, FontWeight = FontWeight.SemiBold };
    private readonly StackPanel tools = new() { Spacing = 6 };
    private readonly StackPanel installs = new() { Spacing = 10 };
    private readonly StackPanel choices = new() { Spacing = 8 };
    private readonly TextBlock review = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock said = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button back = new() { Content = "Back" };
    private readonly Button next = new() { Content = "Next" };
    private readonly Button save = DialogFooter.Default("Save");
    private readonly ComboBox way = new() { Classes = { StyleClasses.Form }, ItemsSource = new[] { "System package manager", "For me alone (no administrator)" }, SelectedIndex = 1 };
    private readonly ComboBox preset = new();
    private readonly ComboBox c = new();
    private readonly ComboBox cxx = new();
    private readonly ComboBox generator = new();
    private readonly TextBox cCompiler = new();
    private readonly TextBox cxxCompiler = new();
    private readonly TextBox buildFolder = new();
    private readonly TextBox configuration = new();
    private readonly TextBox target = new();
    private readonly TextBox sysroot = new();
    private readonly TextBox toolchainFile = new();
    private readonly TextBox conanProfile = new();
    private readonly TextBox extra = new();
    private readonly TextBox gdb = new();
    private readonly CheckBox useConan = new() { Content = "Run Conan before configuring" };
    private int step;

    public CppToolchainWizard(AppSettings settings, CppProject? project)
    {
        this.settings = settings;
        this.project = project;
        CppToolchain now = CppSettings.Toolchain(settings);
        Title = "C/C++ Toolchain";
        Width = 780;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Choices(preset, CppToolchain.Presets, now.Preset);
        Choices(c, CppStandards.CChoices, now.C);
        Choices(cxx, CppStandards.CxxChoices, now.Cxx);
        Choices(generator, CppToolchain.Generators, now.Generator);
        Field(cCompiler, now.CCompiler, "gcc, clang or an external cross compiler");
        Field(cxxCompiler, now.CxxCompiler, "g++, clang++ or an external cross compiler");
        Field(buildFolder, now.BuildFolder, CppToolchain.DefaultBuildFolder);
        Field(configuration, now.Configuration, "Debug");
        Field(target, now.TargetTriple, "m68k-elf");
        Field(sysroot, now.Sysroot, "A sysroot folder");
        Field(toolchainFile, now.ToolchainFile, "A .cmake toolchain file");
        Field(conanProfile, now.ConanProfile, "default");
        Field(extra, now.ExtraCMakeArguments, "-DFOO=bar");
        Field(gdb, now.Gdb, "gdb (GDB 14 or later)");
        useConan.IsChecked = now.UseConan;
        preset.SelectionChanged += (_, _) => PresetChanged();
        way.SelectionChanged += (_, _) => IdeErrors.Observe(DetectAsync());
        back.Click += (_, _) => Move(-1);
        next.Click += (_, _) => Move(1);
        save.Click += (_, _) => Close(Result());
        FillChoices();
        Content = DialogFooter.Page(new DockPanel { Children = { Heading(), new ScrollViewer { Content = body } } },
            DialogFooter.OkCancel(save, () => Close(null), null, back, next));
        Opened += (_, _) => IdeErrors.Observe(DetectAsync());
        ShowStep();
    }

    /// <summary>The toolchain the person chose, with off-PATH programs named so every command finds them again.</summary>
    public CppToolchain Result()
    {
        CppToolchain chosen = CppToolchain.Of(Selected(preset, CppToolchain.Presets)) with
        {
            C = Selected(c, CppStandards.CChoices),
            Cxx = Selected(cxx, CppStandards.CxxChoices),
            Generator = Selected(generator, CppToolchain.Generators),
            CCompiler = Text(cCompiler),
            CxxCompiler = Text(cxxCompiler),
            BuildFolder = Text(buildFolder) ?? CppToolchain.DefaultBuildFolder,
            Configuration = Text(configuration) ?? "Debug",
            TargetTriple = Text(target),
            Sysroot = Text(sysroot),
            ToolchainFile = Text(toolchainFile),
            ConanProfile = Text(conanProfile),
            ExtraCMakeArguments = Text(extra),
            UseConan = useConan.IsChecked == true,
        };
        return chosen with
        {
            Clangd = settings.CppClangd ?? CppToolDetection.Find("clangd"),
            CMake = settings.CppCMake ?? CppToolDetection.Find("cmake"),
            Conan = settings.CppConan ?? CppToolDetection.Find("conan"),
            Gdb = Text(gdb) ?? settings.CppGdb ?? CppToolDetection.Find("gdb"),
        };
    }

    private Control Heading()
    {
        DockPanel.SetDock(heading, Dock.Top);
        heading.Margin = new Thickness(20, 16, 20, 0);
        return heading;
    }

    private void FillChoices()
    {
        choices.Children.Clear();
        choices.Children.Add(Row("Toolchain preset", preset));
        choices.Children.Add(Row("C standard", c));
        choices.Children.Add(Row("C++ standard", cxx));
        choices.Children.Add(Row("CMake generator", generator));
        choices.Children.Add(Row("C compiler", cCompiler));
        choices.Children.Add(Row("C++ compiler", cxxCompiler));
        choices.Children.Add(Row("Build folder", buildFolder));
        choices.Children.Add(Row("Build type", configuration));
        choices.Children.Add(Row("Target triple", target));
        choices.Children.Add(Row("Sysroot", sysroot));
        choices.Children.Add(Row("CMake toolchain file", toolchainFile));
        choices.Children.Add(Row("Conan profile", conanProfile));
        choices.Children.Add(Row("Extra CMake arguments", extra));
        choices.Children.Add(Row("gdb executable", gdb));
        choices.Children.Add(useConan);
    }

    /// <summary>A wizard's row: a label beside its editor, wider than the label so long paths fit.</summary>
    private static Control Row(string label, Control field) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children =
        {
            new TextBlock { Text = label, Width = 180, VerticalAlignment = VerticalAlignment.Center },
            field,
        },
    };

    private static void Choices<T>(ComboBox box, IReadOnlyList<(T Value, string Name)> values, T current) where T : struct, Enum
    {
        box.Classes.Add(StyleClasses.Form);
        box.ItemsSource = values.Select(v => v.Name).ToArray();
        box.SelectedIndex = Math.Max(0, values.Select(v => v.Value).ToList().IndexOf(current));
        box.Width = 320;
    }

    private static T Selected<T>(ComboBox box, IReadOnlyList<(T Value, string Name)> values) where T : struct, Enum =>
        values[Math.Clamp(box.SelectedIndex, 0, values.Count - 1)].Value;

    private static void Field(TextBox box, string? value, string placeholder)
    {
        box.Classes.Add(StyleClasses.Search);
        box.Text = value ?? string.Empty;
        box.PlaceholderText = placeholder;
        box.Width = 320;
    }

    private static string? Text(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();

    private void PresetChanged()
    {
        CppToolchain named = CppToolchain.Of(Selected(preset, CppToolchain.Presets));
        cCompiler.Text = named.CCompiler ?? string.Empty;
        cxxCompiler.Text = named.CxxCompiler ?? string.Empty;
        target.Text = named.TargetTriple ?? string.Empty;
        useConan.IsChecked = named.UseConan;
    }

    private void Move(int by)
    {
        step = Math.Clamp(step + by, 0, 2);
        ShowStep();
    }

    private void ShowStep()
    {
        heading.Text = step switch
        {
            0 => "1. Tools on this machine",
            1 => "2. Compilers and language standards",
            _ => "3. What Passenger will run",
        };
        body.Children.Clear();
        switch (step)
        {
            case 0:
                body.Children.Add(new TextBlock { Text = "Passenger needs CMake, Conan, clangd, a generator and C/C++ compilers. "
                    + "Install what is missing here, or copy a command and run it yourself.", TextWrapping = TextWrapping.Wrap });
                body.Children.Add(Row("Install with", way));
                body.Children.Add(tools);
                body.Children.Add(installs);
                break;
            case 1:
                body.Children.Add(new TextBlock { Text = "Choose the C and C++ versions, the compilers and, for a cross "
                    + "toolchain such as m68k, its target triple and sysroot.", TextWrapping = TextWrapping.Wrap });
                body.Children.Add(choices);
                break;
            default:
                review.Text = CppCommands.Review(project, Result());
                body.Children.Add(new TextBlock { Text = "These are the commands the toolchain makes. Save to use them; "
                    + "nothing has been installed or built by this review.", TextWrapping = TextWrapping.Wrap });
                body.Children.Add(review);
                break;
        }
        body.Children.Add(said);
        back.IsVisible = step > 0;
        next.IsVisible = step < 2;
        save.IsEnabled = step == 2;
    }

    /// <summary>Asks every tool for its version off the UI thread, then lists what is missing and how to install it.</summary>
    private async Task DetectAsync()
    {
        CppToolchain current = Result();
        IReadOnlyList<CppToolStatus> statuses = await CppToolDetection.DetectAsync(current, CancellationToken.None);
        tools.Children.Clear();
        installs.Children.Clear();
        foreach (CppToolStatus status in statuses)
        {
            tools.Children.Add(new TextBlock
            {
                Text = status.Found ? $"✓ {status.Label}: {status.Version} ({status.Path})" : $"✗ {status.Label}: not found",
                TextWrapping = TextWrapping.Wrap,
            });
        }
        CppInstallWay chosen = way.SelectedIndex == 0 ? CppInstallWay.System : CppInstallWay.UserLocal;
        foreach (CppInstallStep install in CppTools.Plan(statuses.Where(s => !s.Found).Select(s => s.Tool), CppToolDetection.Distribution(), chosen))
        {
            installs.Children.Add(InstallRow(install));
        }
        if (installs.Children.Count == 0)
        {
            installs.Children.Add(new TextBlock { Text = "Every tool is installed. Go on to choose the compilers and standards." });
        }
    }

    private Control InstallRow(CppInstallStep install)
    {
        StackPanel row = new() { Spacing = 6 };
        row.Children.Add(new TextBlock { Text = install.Label, FontWeight = FontWeight.SemiBold });
        if (install.Command is { } command)
        {
            TextBox line = new() { Text = command, IsReadOnly = true, Width = 700, TextWrapping = TextWrapping.Wrap };
            row.Children.Add(line);
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
            Button run = new() { Content = "Install" };
            run.Click += (_, _) => IdeErrors.Observe(InstallAsync(install, run));
            Button copy = new() { Content = "Copy" };
            copy.Click += (_, _) =>
            {
                if (Clipboard is { } clipboard)
                {
                    IdeErrors.Observe(Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, command));
                }
            };
            buttons.Children.Add(run);
            buttons.Children.Add(copy);
            row.Children.Add(buttons);
        }
        if (install.Note is { } note)
        {
            row.Children.Add(new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap, Classes = { StyleClasses.Muted } });
        }
        return row;
    }

    /// <summary>Runs an install command the person asked for, then looks for the tools again; no command runs unasked.</summary>
    private async Task InstallAsync(CppInstallStep install, Button run)
    {
        if (install.Command is not { } command)
        {
            return;
        }
        run.IsEnabled = false;
        said.Text = $"Installing {install.Label}…";
        string shell = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/sh";
        string[] arguments = OperatingSystem.IsWindows() ? ["-NoProfile", "-Command", command] : ["-c", command];
        ProcessStartInfo start = ChildProcesses.Command(shell, arguments, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        ChildResult result = await BackgroundWork.Run(() => ChildProcesses.RunAsync(start, limit: TimeSpan.FromMinutes(10)));
        said.Text = result.Succeeded ? $"{install.Label} installed. Looking for the tools again…" : $"{install.Label} could not be installed: {CppTools.Version(result.Error)}";
        await DetectAsync();
        run.IsEnabled = true;
    }
}
