using Godot;

namespace NoesisGodot;

/// <summary>
/// Import artifact for .xaml files: the markup wrapped in a Godot Resource.
///
/// In the editor, providers read raw .xaml straight from the disk (hot-reload friendly). In exported builds the raw file isn't in the PCK,
/// but this imported resource is, and GodotXamlProvider falls back to it.
/// </summary>
[GlobalClass]
public partial class XamlFile : Resource
{
    [Export] public string Source { get; set; } = "";
}
