using Godot;

namespace NoesisGodot.Examples;

/// <summary>
/// Hardening scene: two independent NoesisViews side by side. Left: the menu (mouse input, commands). Right: widget lab (keyboard,
/// two-way bindings, continuous animation).
/// </summary>
public partial class HardeningMain : Control
{
    public override void _Ready()
    {
        GetNode<NoesisView>("Split/MenuView").ViewModel =
            new MainMenuViewModel(onQuit: () => GetTree().Quit());
        GetNode<NoesisView>("Split/LabView").ViewModel = new WidgetLabViewModel();
    }
}
