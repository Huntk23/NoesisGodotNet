using System.IO;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Serves XAML files to Noesis from the Godot virtual filesystem (res://), so XAML ships inside the exported PCK like any other asset.
///
/// URIs are resolved against ProjectSettings 'noesisgui/resources/root' unless they are already absolute res:// paths.
/// </summary>
public class GodotXamlProvider : Noesis.XamlProvider
{
    public override Stream LoadXaml(System.Uri uri)
    {
        string resPath = GodotResourceUtil.ToResPath(uri);
        return GodotResourceUtil.OpenRead(resPath, "XAML");
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

        string root = NoesisServer.GetSetting("noesisgui/resources/root", "res://UI");
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
