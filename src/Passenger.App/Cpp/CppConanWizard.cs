using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.Settings;
using Passenger.Core;
using Passenger.Text;
using Passenger.Workspace.Cpp;

namespace Passenger.App.Cpp;

/// <summary>The Conan Center wizard: it reads the recipes Conan Center publishes, lets a person choose packages and the
/// versions of them, then writes conanfile.txt, runs conan install and adds the find_package() and
/// target_link_libraries() lines conan's CMakeDeps says CMakeLists.txt needs. Nothing is written until Install.</summary>
public sealed class CppConanWizard : IdeWindow
{
    private readonly AppSettings settings;
    private readonly CppProject? project;
    private readonly string folder;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox search = new() { Classes = { StyleClasses.Search }, PlaceholderText = "Filter Conan Center recipes…", Width = 320 };
    private readonly ListBox names = new() { Classes = { StyleClasses.Tree }, Height = 230 };
    private readonly ComboBox versions = new() { Classes = { StyleClasses.Form }, Width = 170 };
    private readonly ListBox chosen = new() { Classes = { StyleClasses.Tree }, Height = 100 };
    private readonly TextBox manual = new() { Classes = { StyleClasses.Search }, PlaceholderText = "Or a reference, e.g. fmt/11.0.2", Width = 320 };
    private readonly Button add = new() { Content = "Add" };
    private readonly Button remove = new() { Content = "Remove" };
    private readonly Button install = DialogFooter.Default("Install");
    private readonly Button ok = DialogFooter.Default("OK");
    private readonly TextBlock output = new() { TextWrapping = TextWrapping.Wrap, Classes = { StyleClasses.Muted } };
    private readonly List<string> selected = [];
    private IReadOnlyList<ConanPackage> packages = [];
    private bool installing;
    private bool done;

    /// <summary>What the wizard says of the recipe list and the install, as the status bar and tests read it.</summary>
    public string Status => status.Text ?? string.Empty;

    /// <summary>Whether the install finished, so the Install button has become OK.</summary>
    public bool Done => done;

    /// <summary>The packages the wizard will write: those already in the recipe, plus what a person added or removed.</summary>
    public IReadOnlyList<string> SelectedPackages => [.. selected];

    public CppConanWizard(AppSettings settings, CppProject? project)
    {
        this.settings = settings;
        this.project = project;
        folder = project?.Folder ?? string.Empty;
        Title = "Install C/C++ dependencies with Conan";
        Width = 760;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        search.TextChanged += (_, _) => FillNames();
        names.SelectionChanged += (_, _) => ShowVersions();
        add.Click += (_, _) => Add();
        remove.Click += (_, _) => Remove();
        install.Click += (_, _) => IdeErrors.Observe(InstallAsync());
        ok.Click += (_, _) => Close(true);
        ok.IsVisible = false;
        ok.IsDefault = false;
        Opened += (_, _) => IdeErrors.Observe(LoadAsync());

        StackPanel body = new() { Spacing = 10, Margin = new Thickness(20) };
        body.Children.Add(new TextBlock { Text = "Packages available at conan.io/center", FontWeight = FontWeight.SemiBold });
        body.Children.Add(status);
        body.Children.Add(search);
        body.Children.Add(names);
        StackPanel versionRow = new() { Orientation = Orientation.Horizontal, Spacing = 10, Children = { versions, add } };
        body.Children.Add(versionRow);
        body.Children.Add(new TextBlock { Text = "Chosen packages", FontWeight = FontWeight.SemiBold });
        body.Children.Add(chosen);
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { manual, remove } });
        body.Children.Add(output);
        Content = DialogFooter.Page(new ScrollViewer { Content = body }, DialogFooter.OkCancel(install, () => Close(false), null, ok));
        ShowVersions();
        LoadExisting();
    }

    /// <summary>Starts from the packages the recipe already asks for, so adding one never takes the others out.</summary>
    private void LoadExisting()
    {
        if (folder.Length == 0 || !File.Exists(Path.Combine(folder, "conanfile.txt")))
        {
            return;
        }
        foreach (string reference in ConanFile.Read(folder)?.Requires ?? [])
        {
            if (ConanCenter.Valid(reference) && !selected.Contains(reference, StringComparer.OrdinalIgnoreCase))
            {
                selected.Add(reference);
            }
        }
        chosen.ItemsSource = selected.ToArray();
    }

    /// <summary>Reads every recipe the Conan 2 remote lists, falling back to the older remote and to the conan CLI.</summary>
    private async Task LoadAsync()
    {
        status.Text = $"Reading the recipe list from {ConanCenter.Browse}…";
        packages = await RecipesAsync();
        FillNames();
        status.Text = packages.Count > 0
            ? $"{packages.Count} recipes. Choose one, its version, then Add; Install writes conanfile.txt and CMakeLists.txt."
            : "Conan Center could not be reached. Type a reference (name/version) below and press Add, then Install.";
    }

    private static async Task<IReadOnlyList<ConanPackage>> RecipesAsync()
    {
        try
        {
            return await BackgroundWork.Run(ReadAsync);
        }
        catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return [];
        }

        static async Task<IReadOnlyList<ConanPackage>> ReadAsync()
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
            foreach (string url in (string[])[ConanCenter.RecipesUrl, ConanCenter.FallbackUrl])
            {
                try
                {
                    if (ConanCenter.Recipes(await client.GetStringAsync(url)) is { Count: > 0 } found)
                    {
                        return found;
                    }
                }
                catch (Exception failed) when (failed is HttpRequestException or TaskCanceledException or JsonException)
                {
                    // The other remote, or the conan CLI, may still answer.
                }
            }
            ChildResult listed = await ChildProcesses.RunAsync(ChildProcesses.Command("conan", ["list", "*", "-r=conancenter"]),
                limit: TimeSpan.FromSeconds(60));
            return listed.Succeeded ? ConanCenter.Listed(listed.Output) : [];
        }
    }

    private void FillNames()
    {
        string query = search.Text ?? string.Empty;
        string[] shown = [.. packages.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name).Take(400)];
        names.ItemsSource = shown;
        if (names.SelectedIndex < 0 && shown.Length > 0)
        {
            names.SelectedIndex = 0;
        }
        ShowVersions();
    }

    private void ShowVersions()
    {
        if (names.SelectedItem is string name && packages.FirstOrDefault(p => p.Name == name) is { } package)
        {
            versions.ItemsSource = package.Versions.ToArray();
            versions.SelectedIndex = 0;
        }
        else
        {
            versions.ItemsSource = null;
        }
    }

    private void Add()
    {
        string? reference = manual.Text is { Length: > 0 } typed ? typed.Trim()
            : names.SelectedItem is string name && versions.SelectedItem is string version ? ConanCenter.Requires(name, version) : null;
        if (reference is null || !ConanCenter.Valid(reference))
        {
            status.Text = "Choose a recipe and a version, or type a reference such as fmt/11.0.2.";
            return;
        }
        if (!selected.Contains(reference, StringComparer.OrdinalIgnoreCase))
        {
            selected.Add(reference);
            chosen.ItemsSource = selected.ToArray();
        }
        manual.Text = string.Empty;
    }

    private void Remove()
    {
        if (chosen.SelectedItem is string reference && selected.Remove(reference))
        {
            chosen.ItemsSource = selected.ToArray();
        }
    }

    /// <summary>Writes the recipe, installs it, and uses conan's own CMakeDeps words to update CMakeLists.txt.</summary>
    private async Task InstallAsync()
    {
        if (installing)
        {
            return;
        }
        if (project is null)
        {
            status.Text = "Open a folder with a CMakeLists.txt first.";
            return;
        }
        if (selected.Count == 0)
        {
            status.Text = "Add at least one package first.";
            return;
        }
        installing = true;
        install.IsEnabled = false;
        CppToolchain toolchain = CppSettings.Toolchain(settings);
        status.Text = $"Installing {Plural.Listed(selected)} with Conan…";
        try
        {
            string recipe = Path.Combine(folder, "conanfile.txt");
            string before = File.Exists(recipe) ? File.ReadAllText(recipe) : CppFiles.Conan();
            Save(recipe, CppFiles.WithRequires(before, selected));
            CppCommandResult result = await CppCommands.ConanWithProfileAsync(project, toolchain, CancellationToken.None);
            output.Text = result.Output;
            if (!result.Succeeded)
            {
                status.Text = "conan install failed: its output is below. Check the reference and the Conan profile.";
                return;
            }
            (IReadOnlyList<string> find, IReadOnlyList<string> link) = ConanCenter.CmakeLines(result.Output);
            if (find.Count == 0)
            {
                find = [.. selected.Select(r => r.Split('/')[0])];
            }
            // Every generated target, so a second install keeps the first package linked too.
            link = [.. link.Concat(ConanCenter.GeneratedTargets(toolchain.ConanPath(folder))).Distinct(StringComparer.OrdinalIgnoreCase)];
            string lists = Path.Combine(folder, CMakeProject.FileName);
            if (File.Exists(lists))
            {
                Save(lists, CppFiles.WithPackages(File.ReadAllText(lists), find, link));
            }
            // Configure now with the toolchain and prefix path, so a build never guesses where fmt-config.cmake is.
            CppProject installed = CMakeProject.Read(folder) ?? project;
            CppCommandResult configured = await CppCommands.EnsureConfiguredAsync(installed, toolchain, CancellationToken.None);
            output.Text += configured.Output;
            if (!configured.Succeeded)
            {
                status.Text = "Installed, but the CMake configure failed: its output is below. Check the build folder.";
                return;
            }
            done = true;
            install.IsVisible = false;
            install.IsDefault = false;
            ok.IsVisible = true;
            ok.IsDefault = true;
            status.Text = $"Installed {Plural.Listed([.. find])}. CMakeLists.txt finds and links them and the project is configured; press OK to close, then Run or Debug.";
        }
        catch (Exception failed) when (failed is IOException or UnauthorizedAccessException)
        {
            status.Text = failed.Message;
        }
        finally
        {
            installing = false;
            install.IsEnabled = true;
        }
    }

    private static void Save(string path, string text)
    {
        FileText? file = FileText.Load(path);
        if (file is null)
        {
            FileText.SaveNew(path, text);
        }
        else
        {
            FileText.SaveOver(path, file, text);
        }
    }
}
