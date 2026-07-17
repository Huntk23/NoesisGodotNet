using Godot;

namespace NoesisGodot.Examples;

public partial class ThemeShowcaseMain : Control
{
    public override void _Ready()
    {
        GetNode<NoesisView>("ThemeView").ViewModel = new ThemeLabViewModel();
    }
}
