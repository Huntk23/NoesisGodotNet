using System.IO;
using System.Text;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Serves XAML files to Noesis from the Godot virtual filesystem (res://), so XAML ships inside the exported PCK like any other asset.
///
/// URIs are resolved against ProjectSettings 'noesis_gui/resources/root' unless they are already absolute res:// paths.
///
/// Two load paths:
///  - Raw file (editor runs): direct read, hot-reload friendly.
///  - Imported XamlFile resource (exported builds): the raw .xaml isn't in the PCK, but the artifact produced by XamlImportPlugin is.
/// </summary>
public class GodotXamlProvider : Noesis.XamlProvider
{
    public override Stream LoadXaml(System.Uri uri)
    {
        string resPath = GodotResourceUtil.ToResPath(uri);

        if (Godot.FileAccess.FileExists(resPath))
        {
            return GodotResourceUtil.OpenRead(resPath, "XAML");
        }

        if (ResourceLoader.Exists(resPath) &&
            ResourceLoader.Load(resPath) is XamlFile xamlFile)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(xamlFile.Source), writable: false);
        }

        GD.PushWarning($"[NoesisGUI] XAML not found: {resPath}");
        return null;
    }
}

/// <summary>Shared res:// resolution helpers for the providers.</summary>
public static class GodotResourceUtil
{
    public static string ToResPath(System.Uri uri)
    {
        // Noesis passes relative URIs like "MainMenu.xaml" or "Images/logo.png", possibly with a leading '/'. Absolute res:// paths pass through.
        string raw = uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString;
        return ToResPath(raw);
    }

    public static string ToResPath(string raw)
    {
        raw = raw.Replace('\\', '/').TrimStart('/');

        if (raw.StartsWith("res://"))
        {
            return raw;
        }

        string root = NoesisServer.GetSetting("noesis_gui/resources/root", "res://UI");
        return $"{root.TrimEnd('/')}/{raw}";
    }

    public static Stream OpenRead(string resPath, string kind)
    {
        if (!Godot.FileAccess.FileExists(resPath))
        {
            GD.PushWarning($"[NoesisGUI] {kind} not found: {resPath}");
            return null; // Noesis treats null as "not found"
        }

        byte[] bytes = Godot.FileAccess.GetFileAsBytes(resPath);
        return new MemoryStream(bytes, writable: false);
    }
}
