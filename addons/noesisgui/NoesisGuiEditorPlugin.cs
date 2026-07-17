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
        // Path to NoesisStudio.exe; empty = use the OS file association for .xaml.
        EnsureSetting("noesis_gui/editor/studio_path", "");
        // Zero-copy rendering under the Compatibility (GL) renderer; falls back to CPU readback automatically when unsupported (Forward+, threaded GL).
        EnsureSetting("noesis_gui/rendering/zero_copy", true);

        _xamlImporter = new XamlImportPlugin();
        AddImportPlugin(_xamlImporter);

        AddToolMenuItem(StudioMenuLabel, Callable.From(OpenSelectedXamlInStudio));
    }

    public override void _ExitTree()
    {
        RemoveToolMenuItem(StudioMenuLabel);
        if (_xamlImporter != null)
        {
            RemoveImportPlugin(_xamlImporter);
            _xamlImporter = null;
        }
    }

    private const string StudioMenuLabel = "Open Selected XAML in Noesis Studio";

    private static void OpenSelectedXamlInStudio()
    {
        string[] selected = EditorInterface.Singleton.GetSelectedPaths();
        string xaml = System.Array.Find(selected ?? [],
            p => p.EndsWith(".xaml", System.StringComparison.OrdinalIgnoreCase));
        if (xaml == null)
        {
            GD.PushWarning("[NoesisGUI] Select a .xaml file in the FileSystem dock first.");
            return;
        }

        string globalPath = ProjectSettings.GlobalizePath(xaml);
        string studio = (string)ProjectSettings.GetSetting("noesis_gui/editor/studio_path", "");

        if (!string.IsNullOrEmpty(studio))
        {
            OS.CreateProcess(studio, [globalPath]);
        }
        else
        {
            OS.ShellOpen(globalPath); // OS association (Noesis Studio registers .xaml)
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
