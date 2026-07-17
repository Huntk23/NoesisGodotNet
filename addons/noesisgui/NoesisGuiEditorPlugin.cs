#if TOOLS
using Godot;

namespace NoesisGodot;

/// <summary>
/// Editor entry point for the NoesisGUI plugin. Node registration itself happens via [GlobalClass] on NoesisView,
/// so this stays minimal. Future home for: .xaml import plugin, XAML preview dock, Noesis Studio "open in" integration.
/// </summary>
[Tool]
public partial class NoesisGuiEditorPlugin : EditorPlugin
{
    public override void _EnterTree()
    {
        EnsureSetting("noesisgui/license/name", "");
        EnsureSetting("noesisgui/license/key", "");
        EnsureSetting("noesisgui/resources/root", "res://UI");
        EnsureSetting("noesisgui/resources/fonts", "res://UI/Fonts");
    }

    private static void EnsureSetting(string name, Variant defaultValue)
    {
        if (!ProjectSettings.HasSetting(name))
        {
            ProjectSettings.SetSetting(name, defaultValue);
        }
        ProjectSettings.SetInitialValue(name, defaultValue);
    }
}
#endif
