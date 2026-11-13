using Passenger.App.Cpp;
using Passenger.Core;

namespace Passenger.App;

/// <summary>The window's Conan: its tool window over the folder's C/C++ projects and Conan Center, and its refresh after
/// another folder's projects are read.</summary>
public partial class MainWindow
{
    private ToolWindows.ConanView? conan;

    /// <summary>The Conan tool window, built as it is first shown, by a person's action, or as a test asks for it.</summary>
    public ToolWindows.ConanView Conan => conan ?? (ToolWindows.ConanView)Dock[ToolWindowIds.Conan].Content;

    private void AddConan()
    {
        Dock.Add(new Docking.ToolWindow(ToolWindowIds.Conan, "Conan", IconKeys.Package, () =>
        {
            ToolWindows.ConanView view = conan = new ToolWindows.ConanView(new ConanPackages(
                () => session.Workspace?.CppProjects ?? [], () => CppSettings.Toolchain(settings)));
            view.LinkClicked += (_, link) => Browse(link);
            view.ActionRequested += (_, action) => Request(action);
            return view;
        }, Docking.ToolAnchor.BottomLeft)
        {
            Action = AppActions.ShowConan,
            Menus = [new MenuPlace("Tools", 0, 1)],
        });
        actions!.Add(new IdeAction(AppActions.ConanRefresh, "Refresh Conan Packages", "Conan", _ => IdeErrors.Observe(Conan.RefreshAskedAsync()))
        {
            WhyNot = NeedsFolder,
        });
    }

    /// <summary>The folder's C/C++ projects followed while it is the window's: the Conan window, once built, lists the
    /// packages they ask for again.</summary>
    private void BindConan(ProjectSession next)
    {
        next.WorkspaceChanged += (_, _) =>
        {
            if (session == next && conan is { } view)
            {
                IdeErrors.Observe(view.RefreshAsync());
            }
        };
    }
}
