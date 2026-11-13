using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Passenger.App.Cpp;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Workspace.Cpp;

namespace Passenger.App.ToolWindows;

/// <summary>A recipe as the Conan window lists it: its name, the version the window offers, whether a project asks for it,
/// and every version Conan Center publishes.</summary>
public sealed record ConanRow(string Name, string Version, bool Installed, IReadOnlyList<string> Versions);

/// <summary>The Conan window: the projects' packages with nothing typed, else what Conan Center finds for the query. The
/// chosen recipe's words and versions, and a line per C/C++ project to Install, Update or Remove it through conan install
/// and CMake's own configure. A view over the folder's <see cref="ConanPackages"/>, its session's.</summary>
public sealed class ConanView : DockPanel
{
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(300);

    private readonly TextBox search;
    private readonly TextBlock status;
    private readonly ListBox list;
    private readonly StackPanel details;
    private readonly TextBox log;
    private readonly Debounce typing;
    private readonly ConanPackages model;
    private readonly List<Button> projectButtons = [];
    private CancellationTokenSource? asking;
    private CancellationTokenSource? showing;
    private IReadOnlyList<ConanRow> rows = [];
    private IReadOnlyList<string> versions = [];
    private string? outcome;

    /// <summary>What the list says of itself (what was found, why Conan Center did not answer) and its tip, shown in the
    /// status unless a change's words hold it.</summary>
    private (string Words, string? Tip) listed = (string.Empty, null);

    public ConanView(ConanPackages packages)
    {
        model = packages;
        search = new TextBox { Classes = { StyleClasses.Search }, PlaceholderText = "Search Conan Center recipes", VerticalAlignment = VerticalAlignment.Center };
        status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        status.Classes.Add(StyleClasses.Muted);
        Button refresh = ToolButtons.Of(AppActions.ConanRefresh, IconKeys.Rerun, "Refresh", action => ActionRequested?.Invoke(this, action));
        refresh.Margin = new Thickness(6, 0, 0, 0);
        typing = new Debounce(Pause, RefreshAsync);
        search.TextChanged += (_, _) =>
        {
            Hold(null);
            typing.Restart();
        };
        ToolFlow.SetFills(search, new ToolLayout.Fills(160, 280));
        ToolFlow.SetFills(status, new ToolLayout.Fills(120));
        Border top = ToolBars.Top(new ToolFlow { Margin = new Thickness(8, 2), Children = { search, refresh, status } });

        list = new ListBox { Classes = { StyleClasses.Tree } };
        ReusedRows.Use(list, Row);
        list.SelectionChanged += (_, _) => IdeErrors.Observe(ShowAsync());
        details = new StackPanel { Spacing = 8, Margin = new Thickness(14, 10) };
        log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            FontSize = 11,
            Height = 90,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = Brushes.Transparent,
            IsVisible = false,
            [DockProperty] = Dock.Bottom,
        }.InCodeFont();
        log.Bind(TextBox.BorderBrushProperty, log.GetResourceObservable(TokenKeys.Divider));
        DockPanel right = new();
        right.Children.Add(log);
        right.Children.Add(new ScrollViewer { Content = details });
        Children.Add(top);
        Children.Add(new PaneSplit(list, right, "2*,1,3*"));
        AttachedToVisualTree += (_, _) =>
        {
            IdeErrors.Observe(RefreshAsync());
            Dispatcher.UIThread.Post(() => search.Focus(), DispatcherPriority.Loaded);
        };
    }

    /// <summary>One of the window's actions asked for by its button.</summary>
    public event EventHandler<string>? ActionRequested;

    /// <summary>A recipe's home page clicked, for the browser.</summary>
    public event EventHandler<Uri>? LinkClicked;

    public IReadOnlyList<ConanRow> Rows => rows;

    public ConanRow? Selected => list.SelectedItem as ConanRow;

    /// <summary>The versions offered for the chosen recipe, newest first.</summary>
    public IReadOnlyList<string> Versions => versions;

    /// <summary>The version the Install and Update buttons put in.</summary>
    public string? ChosenVersion { get; set; }

    public string Query
    {
        get => search.Text ?? string.Empty;
        set => search.Text = value;
    }

    /// <summary>What the status beside the search says: what the last package change came to, else what was found, or why
    /// Conan Center did not answer.</summary>
    public string Status => status.Text ?? string.Empty;

    public string Log => log.Text ?? string.Empty;

    public bool IsBusy => model.IsBusy;

    /// <summary>Lists the packages the projects ask for with nothing typed, else what Conan Center finds for the query.</summary>
    public async Task RefreshAsync() => await RefreshAsync(false);

    /// <summary>Refresh, as the person asks for it: the recipe list read again, and the last change's words given way.</summary>
    public Task RefreshAskedAsync()
    {
        Hold(null);
        return RefreshAsync(true);
    }

    private async Task RefreshAsync(bool again)
    {
        asking?.Cancel();
        CancellationTokenSource cancel = asking = new();
        string? chosen = Selected?.Name;
        using IDisposable held = BackgroundWork.Scope();
        try
        {
            Tell(Query.Trim().Length == 0 ? "Reading the packages the projects ask for…" : "Searching Conan Center…");
            IReadOnlyList<ConanPackage> recipes = await model.RecipesAsync(again, cancel.Token);
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            IReadOnlyList<ConanRow> found = RowsOf(recipes);
            List(found, chosen);
            Tell(Query.Trim().Length == 0
                ? $"{Plural.Of(found.Count, "package")} installed"
                : found.Count == 0 ? "Nothing found" : $"{Plural.Of(found.Count, "recipe")} found",
                recipes.Count == 0 ? "Conan Center did not answer; the projects' own packages are listed" : null);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            // A newer list took its place.
        }
    }

    /// <summary>The rows: the projects' own packages with nothing typed, else the recipes Conan Center names for the query.</summary>
    private IReadOnlyList<ConanRow> RowsOf(IReadOnlyList<ConanPackage> recipes)
    {
        Dictionary<string, string> installed = new(StringComparer.OrdinalIgnoreCase);
        foreach (CppProject project in model.Projects)
        {
            foreach (string reference in ConanPackages.Requires(project))
            {
                if (ConanCenter.Split(reference) is { } parts && !installed.ContainsKey(parts.Name))
                {
                    installed[parts.Name] = parts.Version;
                }
            }
        }
        string query = Query.Trim();
        if (query.Length == 0)
        {
            return [.. installed.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => new ConanRow(p.Key, p.Value, true,
                recipes.FirstOrDefault(r => r.Name.Equals(p.Key, StringComparison.OrdinalIgnoreCase))?.Versions ?? [p.Value]))];
        }
        return [.. recipes.Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(400)
            .Select(r => new ConanRow(r.Name, r.Versions.FirstOrDefault() ?? string.Empty, installed.ContainsKey(r.Name), r.Versions))];
    }

    /// <summary>Shows rows, the recipe chosen before chosen again, else the first.</summary>
    private void List(IReadOnlyList<ConanRow> found, string? chosen)
    {
        rows = found;
        list.ItemsSource = rows;
        if (chosen is not null && rows.FirstOrDefault(r => r.Name.Equals(chosen, StringComparison.OrdinalIgnoreCase)) is { } again)
        {
            list.SelectedIndex = IndexOf(again);
        }
        else if (rows.Count > 0)
        {
            list.SelectedIndex = 0;
        }
    }

    private int IndexOf(ConanRow row)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], row))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>A change's words held in the status over the list's, or, with null, the list's own shown again.</summary>
    private void Hold(string? words)
    {
        outcome = words;
        ShowStatus();
    }

    /// <summary>The list's words, shown in the status unless a change's are held there.</summary>
    private void Tell(string words, string? tip = null)
    {
        listed = (words, tip);
        ShowStatus();
    }

    private void ShowStatus()
    {
        status.Text = outcome ?? listed.Words;
        ToolTip.SetTip(status, outcome is null ? listed.Tip : string.Join("\n", new[] { outcome, listed.Words }.Where(w => w.Length > 0)));
    }

    /// <summary>The chosen recipe's page, its versions and a line per project.</summary>
    private async Task ShowAsync()
    {
        showing?.Cancel();
        CancellationTokenSource cancel = showing = new();
        details.Children.Clear();
        versions = [];
        if (Selected is not { } row)
        {
            return;
        }
        using IDisposable held = BackgroundWork.Scope();
        ConanDetails? found = await model.DetailsAsync(row.Name, cancel.Token);
        if (cancel.IsCancellationRequested)
        {
            return;
        }
        versions = row.Versions.Count > 0 ? row.Versions : row.Version.Length > 0 ? [row.Version] : [];
        ChosenVersion = row.Version.Length > 0 ? row.Version : versions.FirstOrDefault();
        ComboBox picker = new() { Name = "Versions", ItemsSource = versions, SelectedItem = ChosenVersion, MinWidth = 140 };
        picker.SelectionChanged += (_, _) =>
        {
            ChosenVersion = picker.SelectedItem as string;
            ShowProjects(row);
        };
        details.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { new TextBlock { Text = row.Name, FontSize = 16, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center }, picker },
        });
        if (found?.Description is { Length: > 0 } text)
        {
            details.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        }
        if (found?.Homepage is { Length: > 0 } home && Uri.TryCreate(home, UriKind.Absolute, out Uri? page))
        {
            Button link = new() { Classes = { StyleClasses.Link }, Content = home, HorizontalAlignment = HorizontalAlignment.Left };
            link.Click += (_, _) => LinkClicked?.Invoke(this, page);
            details.Children.Add(link);
        }
        if (found is { Licenses.Count: > 0 })
        {
            details.Children.Add(Muted("License: " + string.Join(", ", found.Licenses)));
        }
        if (found is { Labels.Count: > 0 })
        {
            details.Children.Add(Muted("Labels: " + string.Join(", ", found.Labels)));
        }
        details.Children.Add(new StackPanel { Name = "projects", Spacing = 4, Margin = new Thickness(0, 8, 0, 0) });
        ShowProjects(row);
    }

    private static TextBlock Muted(string text)
    {
        TextBlock block = new() { Text = text, FontSize = 12 };
        block.Classes.Add(StyleClasses.Muted);
        return block;
    }

    /// <summary>A line per project: the version it asks for, and Install, Update or Remove for the recipe chosen.</summary>
    private void ShowProjects(ConanRow row)
    {
        if (details.Children.OfType<StackPanel>().FirstOrDefault(p => p.Name == "projects") is not { } panel)
        {
            return;
        }
        panel.Children.Clear();
        projectButtons.Clear();
        panel.Children.Add(new TextBlock { Text = "Projects", FontWeight = FontWeight.SemiBold });
        IReadOnlyList<CppProject> projects = model.Projects;
        if (projects.Count == 0)
        {
            panel.Children.Add(Muted("Open a folder with a CMakeLists.txt or conanfile.txt first."));
            return;
        }
        foreach (CppProject project in projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            string? installed = ConanPackages.Requires(project).Select(ConanCenter.Split)
                .FirstOrDefault(p => p is { } parts && parts.Name.Equals(row.Name, StringComparison.OrdinalIgnoreCase))?.Version;
            TextBlock version = new()
            {
                Text = installed ?? "not installed",
                Width = 110,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            version.Bind(TextBlock.ForegroundProperty, version.GetResourceObservable(installed is null ? TokenKeys.TextMuted : TokenKeys.Text));
            TextBlock name = new() { Text = project.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolFlow.SetFills(name, new ToolLayout.Fills(100, 220));
            ToolFlow line = new() { Spacing = 8, Children = { name, version } };
            if (installed is null)
            {
                line.Children.Add(Action("Install", () => InstallAsync(project, row)));
            }
            else
            {
                if (ChosenVersion is { } chosen && !chosen.Equals(installed, StringComparison.OrdinalIgnoreCase))
                {
                    line.Children.Add(Action(VersionOrder.Instance.Compare(chosen, installed) > 0 ? "Update" : "Downgrade", () => InstallAsync(project, row)));
                }
                line.Children.Add(Action("Remove", () => RemoveAsync(project, row)));
            }
            panel.Children.Add(line);
        }
    }

    private Button Action(string label, Func<Task> run)
    {
        Button button = new() { Content = label, IsEnabled = !model.IsBusy };
        button.Click += (_, _) => IdeErrors.Observe(run());
        projectButtons.Add(button);
        return button;
    }

    private Task InstallAsync(CppProject project, ConanRow row)
    {
        string? version = ChosenVersion ?? row.Version;
        return version is { Length: > 0 } ? ChangeAsync(model.InstallAsync(project, row.Name, version, CancellationToken.None), $"{row.Name}/{version}") : Task.CompletedTask;
    }

    private Task RemoveAsync(CppProject project, ConanRow row) =>
        ChangeAsync(model.RemoveAsync(project, row.Name, CancellationToken.None), row.Name);

    /// <summary>One change, its output in the log, its end in the status, and the projects listed again from what the
    /// change wrote.</summary>
    private async Task ChangeAsync(Task<ConanChange> change, string title)
    {
        Hold("Working…");
        EnableActions();
        ConanChange ended = await change;
        if (ended.Output.Length > 0)
        {
            log.IsVisible = true;
            log.Text = ended.Output;
            log.CaretIndex = log.Text.Length;
        }
        Hold(ended.Done ? $"{title}: done" : $"{title}: {ended.Problem ?? "failed; the log says why"}");
        EnableActions();
        await RefreshAsync();
    }

    /// <summary>The projects' Install, Update and Remove, enabled only while no change is under way.</summary>
    private void EnableActions()
    {
        foreach (Button button in projectButtons)
        {
            button.IsEnabled = !model.IsBusy;
        }
    }

    /// <summary>A recipe's row: its package icon, its name (strong where a project asks for it) and the version offered.</summary>
    private Control Row()
    {
        TextBlock version = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        version.Classes.Add(StyleClasses.Muted);
        RowIcon icon = new() { Width = 14, Height = 14, Margin = new Thickness(6, 0, 6, 0) };
        icon.DataKey = IconKeys.Package;
        TextBlock name = new() { VerticalAlignment = VerticalAlignment.Center };
        StackPanel line = new()
        {
            Orientation = Orientation.Horizontal,
            Height = ListRow.Height,
            Children = { icon, name, version },
        };
        return line.Showing<ConanRow>(row =>
        {
            name.Text = row.Name;
            name.FontWeight = row.Installed ? FontWeight.SemiBold : FontWeight.Normal;
            version.Text = row.Version;
            ToolTip.SetTip(line, row.Versions.Count > 1 ? $"{row.Versions.Count} versions" : null);
        });
    }
}
