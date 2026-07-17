#if TOOLS
using Godot;

namespace NoesisGodot;

/// <summary>
/// Editor entry point for the NoesisGUI plugin. Node registration itself happens via [GlobalClass] on NoesisView,
/// so this stays minimal. Future home for: XAML preview dock, Noesis Studio "open in" integration.
/// </summary>
[Tool]
public partial class NoesisGuiEditorPlugin : EditorPlugin
{
    private XamlImportPlugin _xamlImporter;

    public override void _EnterTree()
    {
        EnsureSetting("noesis_gui/license/name", "");
        EnsureSetting("noesis_gui/license/key", "");
        EnsureSetting("noesis_gui/resources/root", "res://UI");
        EnsureSetting("noesis_gui/resources/fonts", "res://UI/Fonts");
        EnsureSetting("noesis_gui/logging/silence_hot_reload", false);
        EnsureSetting("noesis_gui/theme/xaml", "Theme/NoesisTheme.DarkBlue.xaml");

        _xamlImporter = new XamlImportPlugin();
        AddImportPlugin(_xamlImporter);
    }

    public override void _ExitTree()
    {
        if (_xamlImporter != null)
        {
            RemoveImportPlugin(_xamlImporter);
            _xamlImporter = null;
        }
    }

    private static void EnsureSetting(string name, Variant defaultValue)
    {
        if (!ProjectSettings.HasSetting(name))
        {
            ProjectSettings.SetSetting(name, defaultValue);
        }
        ProjectSettings.SetInitialValue(name, defaultValue);

        // Typed property info + basic flag: without these, custom settings can be hidden in the Project Settings dialog unless "Advanced Settings"
        // is enabled.
        ProjectSettings.AddPropertyInfo(new Godot.Collections.Dictionary
        {
            { "name", name },
            { "type", (int)defaultValue.VariantType },
        });
        ProjectSettings.SetAsBasic(name, true);
    }
}
#endif
