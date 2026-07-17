#if TOOLS
using Godot;
using Godot.Collections;

namespace NoesisGodot;

/// <summary>
/// Registers .xaml as an importable type so XAML files show up in the FileSystem dock and, critically, ship in
/// exported builds as XamlFile resources without any export-filter configuration.
/// </summary>
[Tool]
public partial class XamlImportPlugin : EditorImportPlugin
{
    public override string _GetImporterName() => "noesisgodot.xaml";
    public override string _GetVisibleName() => "Noesis XAML";
    public override string[] _GetRecognizedExtensions() => ["xaml"];
    public override string _GetSaveExtension() => "res";
    public override string _GetResourceType() => "Resource";
    public override float _GetPriority() => 1.0f;
    public override int _GetImportOrder() => 0;
    public override int _GetPresetCount() => 0;
    public override string _GetPresetName(int presetIndex) => "";

    public override Array<Dictionary> _GetImportOptions(string path, int presetIndex) => [];

    public override bool _GetOptionVisibility(string path, StringName optionName, Dictionary options) => true;

    public override Error _Import(string sourceFile, string savePath, Dictionary options, Array<string> platformVariants, Array<string> genFiles)
    {
        string source = FileAccess.GetFileAsString(sourceFile);
        Error readError = FileAccess.GetOpenError();
        if (readError != Error.Ok && string.IsNullOrEmpty(source))
        {
            return readError;
        }

        var resource = new XamlFile { Source = source };
        return ResourceSaver.Save(resource, $"{savePath}.res");
    }
}
#endif
