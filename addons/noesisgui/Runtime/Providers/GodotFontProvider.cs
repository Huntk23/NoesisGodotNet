using System.IO;
using Godot;

namespace NoesisGodot;

/// <summary>
/// Exposes .ttf/.otf files under 'noesisgui/resources/fonts' (and any folder referenced by XAML font URIs)
/// to Noesis text rendering.
///
/// XAML usage: FontFamily="./#My Font Name"  (family name, not filename - same convention as WPF/Noesis).
/// </summary>
public class GodotFontProvider : Noesis.FontProvider
{
    public override void ScanFolder(System.Uri folder)
    {
        string resFolder = ResolveFolder(folder);
        using var dir = DirAccess.Open(resFolder);
        if (dir == null)
        {
            return; // no fonts folder is fine — system fallbacks still apply
        }

        dir.ListDirBegin();
        for (string file = dir.GetNext(); !string.IsNullOrEmpty(file); file = dir.GetNext())
        {
            if (dir.CurrentIsDir())
            {
                continue;
            }
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".ttf" or ".otf" or ".ttc")
            {
                RegisterFont(folder, file);
            }
        }
        dir.ListDirEnd();
    }

    public override Stream OpenFont(System.Uri folder, string filename)
    {
        string resFolder = ResolveFolder(folder);
        return GodotResourceUtil.OpenRead($"{resFolder.TrimEnd('/')}/{filename}", "Font");
    }

    private static string ResolveFolder(System.Uri folder)
    {
        string raw = folder == null ? "" : folder.IsAbsoluteUri ? folder.AbsolutePath : folder.OriginalString;
        raw = raw.Replace('\\', '/').Trim('/');

        if (raw.StartsWith("res://"))
        {
            return raw;
        }
        if (string.IsNullOrEmpty(raw) || raw == ".")
        {
            return NoesisServer.GetSetting("noesisgui/resources/fonts", "res://UI/Fonts");
        }

        string root = NoesisServer.GetSetting("noesisgui/resources/root", "res://UI");
        return $"{root.TrimEnd('/')}/{raw}";
    }
}
