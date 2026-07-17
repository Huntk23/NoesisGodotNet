using Godot;

namespace NoesisGodot.Examples;

/// <summary>Example root: wires a ViewModel into the NoesisView child.</summary>
public partial class Main : Control
{
    public override void _Ready()
    {
        var view = GetNode<NoesisView>("NoesisView");
        view.ViewModel = new MainMenuViewModel(onQuit: () => GetTree().Quit());
    }
}
