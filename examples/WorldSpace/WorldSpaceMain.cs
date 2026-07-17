using Godot;

namespace NoesisGodot.Examples;

/// <summary>World-space example: the themed control lab on a floating 3D panel.</summary>
public partial class WorldSpaceMain : Node3D
{
    private NoesisView3D _panel;
    private float _time;

    public override void _Ready()
    {
        _panel = GetNode<NoesisView3D>("Panel");
        _panel.ViewModel = new ThemeLabViewModel();
    }

    public override void _Process(double delta)
    {
        // Gentle bob so it reads as "in the world" — input still lands correctly because picking uses the collider's live transform.
        _time += (float)delta;
        _panel.Position = new Vector3(0, Mathf.Sin(_time * 0.8f) * 0.02f, 0);
    }
}
